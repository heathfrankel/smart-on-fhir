using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CefSharp;
using CefSharp.WinForms;
using Hl7.Fhir.Model;
using Hl7.Fhir.Support;
using Newtonsoft.Json;

namespace EHRApp
{
    public static class SimulatedFhirServer
    {
        internal static Dictionary<string, IPatientData> LaunchContexts { get; } = new Dictionary<string, IPatientData>();

        public static void RegisterHandler(CefSettings settings)
        {
            settings.RegisterScheme(new CefCustomScheme
            {
                IsSecure = true,
                DomainName = "sqlonfhir-dstu2.azurewebsites.net",
                SchemeName = "https",
                IsCorsEnabled = true,
                IsCSPBypassing = false,
                IsDisplayIsolated = false,
                IsFetchEnabled = true,
                IsLocal = false,
                IsStandard = true,
                SchemeHandlerFactory = new CustomProtocolSchemeHandlerFactory()
            });

            // R4 server too!
            settings.RegisterScheme(new CefCustomScheme
            {
                IsSecure = true,
                DomainName = "sqlonfhir-r4.azurewebsites.net",
                SchemeName = "https",
                IsCorsEnabled = true,
                IsCSPBypassing = false,
                IsDisplayIsolated = false,
                IsFetchEnabled = true,
                IsLocal = false,
                IsStandard = true,
                SchemeHandlerFactory = new CustomProtocolSchemeHandlerFactory()
            });
        }
    }

    public class CustomProtocolSchemeHandlerFactory : ISchemeHandlerFactory
    {
        public const string SchemeName = "customFileProtocol";

        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
        {
            return new CustomProtocolSchemeHandler();
        }
    }

    /// <summary>
    /// Models a response from an OpenID Connect/OAuth 2 token endpoint
    /// </summary>
    public class TokenResponse
    {
        public string access_token { get; set; }
        public string token_type { get; set; }
        public int expires_in { get; set; }
        public string id_token { get; set; }
        public string scope { get; set; }
        public string refresh_token { get; set; }
        public string error_description { get; set; }

        public string patient { get; set; }
        public string encounter { get; set; }
    }

    public class CustomProtocolSchemeHandler : ResourceHandler
    {
        // Specifies where you bundled app resides.
        // Basically path to your index.html
        private string frontendFolderPath;
        Dictionary<string, string> Bearers = new Dictionary<string, string>();
        Dictionary<string, string> Codes = new Dictionary<string, string>();

        public CustomProtocolSchemeHandler()
        {
            frontendFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "./bundle/");
        }

        // Process request and craft response.
        public override CefReturnValue ProcessRequestAsync(IRequest request, ICallback callback)
        {
            if (request.Method == "OPTIONS")
            {
                // This is the CORS request, and that's good
                base.StatusCode = 200;
                // base.Headers.Add("Access-Control-Allow-Origin", "*");
                base.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,OPTIONS");
                base.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept, authorization");
                callback.Continue();
                return CefReturnValue.Continue;
            }
            var uri = new Uri(request.Url);
            Console.WriteLine($"-----------------\r\n{request.Url}");
            try
            {
                // Request for the identity server queries (token/authorize)
                if (uri.OriginalString.StartsWith("https://sqlonfhir-r4.azurewebsites.net/identity/authorize"))
                {
                    // validate the bearer
                    var keyValuePairs = uri.Query.Split('&').Select(p => { var pair = p.Split('='); return new KeyValuePair<string, string>(pair[0], Uri.UnescapeDataString(pair[1])); }).ToList();
                    foreach (var item in keyValuePairs)
                    {
                        Console.WriteLine($"Authorize: {item.Key} = {item.Value}");
                    }
                    string redirectUri = keyValuePairs.FirstOrDefault(k => k.Key == "redirect_uri").Value;
                    string state = keyValuePairs.FirstOrDefault(k => k.Key == "state").Value;
                    string clientId = keyValuePairs.FirstOrDefault(k => k.Key == "client_id").Value;
                    string code = Guid.NewGuid().ToFhirId();
                    if (Codes.ContainsKey(redirectUri))
                        Codes.Remove(redirectUri);
                    Codes.Add(redirectUri, code);

                    base.StatusCode = (int)System.Net.HttpStatusCode.Redirect;
                    base.Headers.Remove("Location");
                    base.Headers.Add("Location", $"{redirectUri}?code={code}&state={state}");

                    callback.Continue();
                    return CefReturnValue.Continue;
                }

                if (uri.OriginalString.StartsWith("https://sqlonfhir-r4.azurewebsites.net/identity/token"))
                {
                    // validate the token
                    if (request.PostData != null)
                    {
                        var data = request.PostData.Elements.FirstOrDefault();
                        var body = data.GetBody();
                        var keyValuePairs = body.Split('&').Select(p => { var pair = p.Split('='); return new KeyValuePair<string, string>(pair[0], Uri.UnescapeDataString(pair[1])); }).ToList();
                        foreach (var item in keyValuePairs)
                        {
                            Console.WriteLine($"Token: {item.Key} = {item.Value}");
                        }
                    }

                    TokenResponse responseToken = new TokenResponse()
                    {
                        access_token = "asldkjfhaslkdjfh",
                        token_type = "Bearer",
                        expires_in = 3600,
                        scope = "patient/Observation.read patient/Patient.read",
                        patient = "pat1"
                    };

                    base.StatusCode = (int)System.Net.HttpStatusCode.OK;
                    string json = JsonConvert.SerializeObject(responseToken);
                    Console.WriteLine($"Token: {json}");
                    base.Stream = new System.IO.MemoryStream(System.Text.UTF8Encoding.UTF8.GetBytes(json));
                    base.Headers.Add("Cache-Control", "no-store");
                    base.Headers.Add("Pragma", "no-cache");
                    base.MimeType = "application/json;charset=UTF-8";

                    callback.Continue();
                    return CefReturnValue.Continue;
                }

                // This is a regular request
                Hl7.Fhir.Rest.FhirClient server = new Hl7.Fhir.Rest.FhirClient("https://sqlonfhir-r4.azurewebsites.net");
                server.OnAfterResponse += (sender, args) => 
                {
                    base.Charset = args.RawResponse.CharacterSet;
                    foreach (string header in args.RawResponse.Headers.AllKeys)
                    {
                        base.Headers.Add(header, args.RawResponse.Headers[header]);
                    }
                };
                server.PreferredFormat = Hl7.Fhir.Rest.ResourceFormat.Json;
                string redirectedUrl = server.Endpoint.OriginalString.TrimEnd('/') + uri.PathAndQuery;
                System.Diagnostics.Trace.WriteLine($"{redirectedUrl}");
                System.Threading.Tasks.Task<Hl7.Fhir.Model.Resource> t = server.GetAsync(redirectedUrl).ContinueWith<Hl7.Fhir.Model.Resource>(r =>
                {
                    if (r.Exception != null)
                    {
                        System.Diagnostics.Trace.WriteLine($"Error: {r.Exception.Message}");
                        if (r.Exception.InnerException is Hl7.Fhir.Rest.FhirOperationException fe)
                        {
                            base.StatusCode = (int)fe.Status;
                            if (fe.Outcome != null)
                                base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(fe.Outcome));
                            callback.Continue();
                            System.Diagnostics.Trace.WriteLine($"Error (inner): {fe.Message}");
                            return null;
                        }
                    }
                    base.StatusCode = 200;

                    if (r.Result is Hl7.Fhir.Model.CapabilityStatement cs)
                    {
                        // As per the documentation http://hl7.org/fhir/smart-app-launch/conformance/index.html

                        // Update the security node with our internal security node
                        if (cs.Rest[0].Security == null)
                            cs.Rest[0].Security = new Hl7.Fhir.Model.CapabilityStatement.SecurityComponent();
                        Hl7.Fhir.Model.CapabilityStatement.SecurityComponent security = cs.Rest[0].Security;
                        if (!security.Service.Any(cc => cc.Coding.Any(c => c.System == "http://hl7.org/fhir/restful-security-service" && c.Code == "SMART-on-FHIR")))
                            security.Service.Add(new Hl7.Fhir.Model.CodeableConcept("http://hl7.org/fhir/restful-security-service", "SMART-on-FHIR"));
                        var extension = security.GetExtension("http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris");
                        if (extension == null)
                        {
                            extension = new Extension() { Url = "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris" };
                            security.Extension.Add(extension);
                        }
                        // remove the existing authentications, and put in our own
                        extension.Extension.Clear();
                        extension.AddExtension("token", new FhirUri("https://sqlonfhir-r4.azurewebsites.net/identity/token"));
                        extension.AddExtension("authorize", new FhirUri("https://sqlonfhir-r4.azurewebsites.net/identity/authorize"));
                    }

                    base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(r.Result));
                    Console.WriteLine($"Success: {base.Stream.Length}");
                    base.MimeType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(base.Headers["Content-Type"]).MediaType;
                    callback.Continue();
                    return r.Result;
                });
                return CefReturnValue.ContinueAsync;
            }
            catch (Exception ex)
            {
                callback.Dispose();
                return CefReturnValue.Cancel;
            }
        }

        // Added for security reasons.
        // In this code it is used to verify that requested file is descendant to your index.html.
        public bool IsRequestedPathInsideFolder(DirectoryInfo path, DirectoryInfo folder)
        {
            if (path.Parent == null)
            {
                return false;
            }

            if (string.Equals(path.Parent.FullName, folder.FullName, StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }

            return IsRequestedPathInsideFolder(path.Parent, folder);
        }
    }
}
