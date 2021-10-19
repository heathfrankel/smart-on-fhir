using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Sockets;
using System.Net;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using System.Net.Http;
using Hl7.Fhir.Rest.Legacy;
using Hl7.Fhir.Support;
using Hl7.Fhir.SmartAppLaunch;
using System.IO;
using Hl7.Fhir.DemoFileSystemFhirServer;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace Test.Fhir.SmartAppLaunch.Support
{
    [TestClass]
    public class FacadeProtocolHandlerTests
    {
        #region << Test prepare and cleanup >>
        private Hl7.Fhir.WebApi.IFhirSystemServiceR4<IServiceProvider> _systemService;
        private SmartSessions _mgr;
        [TestInitialize]
        public void PrepareTests()
        {
            _systemService = new DirectorySystemService<IServiceProvider>();
            DirectorySystemService<IServiceProvider>.Directory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\.fhir\\ehrapp-testdata";
            if (!System.IO.Directory.Exists(DirectorySystemService<IServiceProvider>.Directory))
                System.IO.Directory.CreateDirectory(DirectorySystemService<IServiceProvider>.Directory);
            (_systemService as DirectorySystemService<IServiceProvider>).InitializeIndexes();
            _mgr = new SmartSessions();
        }

        [TestCleanup]
        public void CleanupTests()
        {
        }

        public static void DebugDumpOutputXml(Base fragment)
        {
            if (fragment == null)
                Console.WriteLine("(null)");
            else
            {
                var doc = System.Xml.Linq.XDocument.Parse(new FhirXmlSerializer().SerializeToString(fragment));
                Console.WriteLine(doc.ToString(System.Xml.Linq.SaveOptions.None));
            }
        }
        public static void DebugDumpOutputJson(Base fragment)
        {
            if (fragment == null)
                Console.WriteLine("(null)");
            else
            {
                Console.WriteLine(new FhirJsonSerializer(new SerializerSettings() { Pretty = true }).SerializeToString(fragment));
            }
        }
        public string GetIdToken(SmartApplicationDetails app, IFhirSmartAppContext context)
        {
            return GenerateProviderJWTForNcsr(DateTime.Now, app, context);
        }

        public static string GenerateProviderJWTForNcsr(DateTime creationTime, SmartApplicationDetails app, IFhirSmartAppContext context)
        {
            var payload = new
            {
                sub = (context as MockFhirSmartAppContext).PractitionerId,
                name = (context as MockFhirSmartAppContext).PractitionerName,
                profile = "https://example.org/Practitioner/" + (context as MockFhirSmartAppContext).PractitionerId,
                iss = app.Issuer,
                aud = app.Audience,
                exp = creationTime.AddHours(1).ToUnixTime(),
                jti = Guid.NewGuid().ToFhirId(),
                iat = creationTime.AddMinutes(-1).ToUnixTime(),
                nbf = creationTime.AddMinutes(-1).ToUnixTime(),
                providerNo = (context as MockFhirSmartAppContext).MedicareProviderNumber,
                isDelegate = "N",
                roles = new[] { "Practitioner", "PracticeManager" },
                pmsver = "Example EHR v1.34",
                practitioner = context.ContextProperties.FirstOrDefault(cp => cp.Key == "practitioner").Value,
                practitionerrole = context.ContextProperties.FirstOrDefault(cp => cp.Key == "practitionerrole").Value,
                organization = context.ContextProperties.FirstOrDefault(cp => cp.Key == "organization").Value
            };

            // Provide the NASH Digital Certificate (with Private Key)
            X509Certificate2 cert = GetNashCertificate();

            var extraHeaders = new System.Collections.Generic.Dictionary<string, object>();
            extraHeaders.Add("typ", "JWT");
            extraHeaders.Add(JwtHeaderParameterNames.X5c, GetNashPublicKeyChain(cert));

            // Now generate the Identity Token
            string token = Jose.JWT.Encode(payload, cert.GetRSAPrivateKey(), Jose.JwsAlgorithm.RS256, extraHeaders);
            return token;
        }

        public static X509Certificate2 GetNashCertificate()
        {
            // This could be replaced with reading from the Certificate Store, or some other mechanism
            // TODO: retrieve your NASH certificate
            return null;
        }

        public static string[] GetNashPublicKeyChain(X509Certificate2 cert)
        {
            var certBytes = cert.Export(X509ContentType.Cert);
            var certPublic = new X509Certificate2(certBytes);
            return new[] { Convert.ToBase64String(certPublic.GetRawCertData()) };
        }

        public static string GetNashPublicKey()
        {
            var cert = GetNashCertificate();
            var certBytes = cert.Export(X509ContentType.Cert);
            var certPublic = new X509Certificate2(certBytes);
            var publicKey = Convert.ToBase64String(UTF8Encoding.UTF8.GetBytes("-----BEGIN CERTIFICATE-----\r\n" + Convert.ToBase64String(certPublic.GetRawCertData()) + "\r\n-----END CERTIFICATE-----"));
            return publicKey;
        }

        private static IFhirSmartAppContext GetSmartAppUserContext(string bearer)
        {
            return new MockFhirSmartAppContext()
            {
                PractitionerId = "example",
                PractitionerName = "Dr Example",
                MedicareProviderNumber = "1234567a",
                Bearer = bearer,
                Scopes = "user/*.* openid profile launch-ehr"
            };
        }

        private static SmartApplicationDetails GetSmartAppDetails()
        {
            return new SmartApplicationDetails()
            {
                Name = "Unit Test App",
                Url = "http://example.org/faketestapp",
                AllowedScopes = new[] { "user/*.*", "openid", "profile", "launch-ehr" }
            };
        }
        #endregion

        [TestMethod]
        public void ReadCapabilityStatement()
        {
            string requestPath = "";
            string bearer = null;

            SmartApplicationDetails appDetails = GetSmartAppDetails();
            IFhirSmartAppContext smartAppContext = GetSmartAppUserContext(bearer);
            (smartAppContext as MockFhirSmartAppContext).Principal = smartAppContext.ToPrincipal(appDetails, GetIdToken(appDetails, smartAppContext));

            string resultContent = PerformGetRequest(requestPath, bearer, appDetails, smartAppContext, out MockResponse response);
            System.Diagnostics.Trace.WriteLine($"{response.StatusCode}: {response.StatusText}  {response.ErrorCode} mimeType: {response.MimeType}");
            DebugDumpOutputJson(new FhirJsonParser().Parse(resultContent));
            Assert.AreEqual((int)HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/fhir+json", response.MimeType);
        }

        [TestMethod]
        public void ReadPatient()
        {
            string requestPath = "Patient/example";
            string bearer = Guid.NewGuid().ToFhirId();

            SmartApplicationDetails appDetails = GetSmartAppDetails();
            IFhirSmartAppContext smartAppContext = GetSmartAppUserContext(bearer);
            (smartAppContext as MockFhirSmartAppContext).Principal = smartAppContext.ToPrincipal(appDetails, GetIdToken(appDetails, smartAppContext));

            string resultContent = PerformGetRequest(requestPath, bearer, appDetails, smartAppContext, out MockResponse response);
            System.Diagnostics.Trace.WriteLine($"{response.StatusCode}: {response.StatusText}  {response.ErrorCode} mimeType: {response.MimeType}");
            DebugDumpOutputJson(new FhirJsonParser().Parse(resultContent));
            Assert.AreEqual((int)HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/fhir+json", response.MimeType);
        }

        [TestMethod]
        public void SearchPatient()
        {
            string requestPath = "Patient";
            string bearer = Guid.NewGuid().ToFhirId();

            SmartApplicationDetails appDetails = GetSmartAppDetails();
            IFhirSmartAppContext smartAppContext = GetSmartAppUserContext(bearer);
            (smartAppContext as MockFhirSmartAppContext).Principal = smartAppContext.ToPrincipal(appDetails, GetIdToken(appDetails, smartAppContext));

            string resultContent = PerformGetRequest(requestPath, bearer, appDetails, smartAppContext, out MockResponse response);
            System.Diagnostics.Trace.WriteLine($"{response.StatusCode}: {response.StatusText}  {response.ErrorCode} mimeType: {response.MimeType}");
            DebugDumpOutputJson(new FhirJsonParser().Parse(resultContent));
            Assert.AreEqual((int)HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/fhir+json", response.MimeType);
        }

        [TestMethod]
        public void SearchPatients()
        {
            string requestPath = "Patient?_id=2035224";
            string bearer = Guid.NewGuid().ToFhirId();

            SmartApplicationDetails appDetails = GetSmartAppDetails();
            IFhirSmartAppContext smartAppContext = GetSmartAppUserContext(bearer);
            (smartAppContext as MockFhirSmartAppContext).Principal = smartAppContext.ToPrincipal(appDetails, GetIdToken(appDetails, smartAppContext));

            string resultContent = PerformGetRequest(requestPath, bearer, appDetails, smartAppContext, out MockResponse response);
            System.Diagnostics.Trace.WriteLine($"{response.StatusCode}: {response.StatusText}  {response.ErrorCode} mimeType: {response.MimeType}");
            DebugDumpOutputJson(new FhirJsonParser().Parse(resultContent));
            Assert.AreEqual((int)HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/fhir+json", response.MimeType);
        }

        [TestMethod]
        public void CreatePatient()
        {

        }

        [TestMethod]
        public void UpdatePatient()
        {

        }

        [TestMethod]
        public void SearchDocumentReferencePermitted()
        {
            string requestPath = "DocumentReference?patient=Patient/2034921";
            string bearer = Guid.NewGuid().ToFhirId();

            SmartApplicationDetails appDetails = GetSmartAppDetails();
            IFhirSmartAppContext smartAppContext = GetSmartAppUserContext(bearer);
            (smartAppContext as MockFhirSmartAppContext).Principal = smartAppContext.ToPrincipal(appDetails, GetIdToken(appDetails, smartAppContext));

            string resultContent = PerformGetRequest(requestPath, bearer, appDetails, smartAppContext, out MockResponse response);
            System.Diagnostics.Trace.WriteLine($"{response.StatusCode}: {response.StatusText}  {response.ErrorCode} mimeType: {response.MimeType}");
            DebugDumpOutputJson(new FhirJsonParser().Parse(resultContent));
            Assert.AreEqual((int)HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/fhir+json", response.MimeType);
        }

        [TestMethod]
        public void SearchDocumentReferenceDenied()
        {
            string requestPath = "DocumentReference";
            string bearer = Guid.NewGuid().ToFhirId();

            SmartApplicationDetails appDetails = GetSmartAppDetails();
            appDetails.AllowedScopes = new[] { "user/Patient.*", "openid", "profile", "launch-ehr" };
            IFhirSmartAppContext smartAppContext = GetSmartAppUserContext(bearer);
            smartAppContext.Scopes = "user/Patient.* openid profile launch-ehr";
            (smartAppContext as MockFhirSmartAppContext).Principal = smartAppContext.ToPrincipal(appDetails, GetIdToken(appDetails, smartAppContext));

            string resultContent = PerformGetRequest(requestPath, bearer, appDetails, smartAppContext, out MockResponse response);
            System.Diagnostics.Trace.WriteLine($"{response.StatusCode}: {response.StatusText}  {response.ErrorCode} mimeType: {response.MimeType}");
            DebugDumpOutputJson(new FhirJsonParser().Parse(resultContent));
            Assert.AreEqual((int)HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.AreEqual("application/fhir+json", response.MimeType);
        }

        private string PerformGetRequest(string requestPath, string bearer, SmartApplicationDetails appDetails, IFhirSmartAppContext smartAppContext, long sessionIdentifier, out MockResponse response)
        {
            _mgr.RegisterSession(sessionIdentifier, appDetails, smartAppContext);
            string baseUrl = "https://example.org";
            string identityUrl = "https://example.org/identity";
            FhirFacadeProtocolSchemeHandlerFactory<IServiceProvider> factory = new FhirFacadeProtocolSchemeHandlerFactory<IServiceProvider>(
                _mgr, baseUrl, identityUrl, () => { return _systemService; }, true);

            var request = new CefSharp.Request()
            {
                Url = $"{baseUrl}/{requestPath}",
            };
            if (!string.IsNullOrEmpty(bearer))
                request.SetHeaderByName("Authorization", $"Bearer {bearer}", true);
            CefSharp.IFrame frame = new MockFrame(sessionIdentifier);
            CefSharp.IBrowser browser = new MockBrowser(frame);
            var t = factory.Create(browser, frame, "https", request);
            var callback = new MockCallback();
            bool handleRequest;
            var result = t.Open(request, out handleRequest, callback);
            response = new MockResponse();
            t.GetResponseHeaders(response, out long responseLength, out string redirectURl);
            if (responseLength == -1)
            {
                // need to wait some time
                int nDelay = 100;
                while (!callback.IsContinue && !callback.IsCancelled)
                    System.Threading.Tasks.Task.Delay(nDelay);
                // call the GetResponse once more to get the final headers?
                t.GetResponseHeaders(response, out responseLength, out redirectURl);
            }
            var responseStream = (t as FhirFacadeProtocolSchemeHandler<IServiceProvider>).Stream;
            responseStream.Seek(0, SeekOrigin.Begin);
            StreamReader sr = new StreamReader(responseStream);
            return sr.ReadToEnd();
        }

        private string PerformGetRequest(string requestPath, string bearer, SmartApplicationDetails appDetails, IFhirSmartAppContext smartAppContext, out MockResponse response)
        {
            long sessionIdentifier = 5;
            return PerformGetRequest(requestPath, bearer, appDetails, smartAppContext, sessionIdentifier, out response);
        }
    }
}
