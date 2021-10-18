using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using CefSharp;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Utility;
using Hl7.Fhir.WebApi;

namespace Hl7.Fhir.SmartAppLaunch
{
    public class FhirFacadeProtocolSchemeHandlerFactory<TSP> : FhirBaseProtocolSchemeHandlerFactory, ISchemeHandlerFactory
        where TSP : class
    {
        public FhirFacadeProtocolSchemeHandlerFactory(SmartSessions sessionManager, string fhirServerBaseUrl, string identityServerBaseUrl, Func<IFhirSystemServiceR4<TSP>> facadeFactory, bool applySmartScopes = false)
            : base(sessionManager, fhirServerBaseUrl, identityServerBaseUrl)
        {
            _facadeFactory = facadeFactory;
            _applySmartScopes = applySmartScopes;
        }
        private Func<IFhirSystemServiceR4<TSP>> _facadeFactory;
        private bool _applySmartScopes;

        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
        {
            var session = _sessionManager.GetSession(browser.MainFrame.Identifier);
            System.Diagnostics.Trace.WriteLine($"{request.Method}: {request.Url}");
            System.Diagnostics.Trace.WriteLine($"  Content-type: {request.Headers["Content-Type"]}");
            string HeaderAuth = request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(HeaderAuth))
            {
                System.Diagnostics.Trace.WriteLine($"  Authorization: {HeaderAuth}");
                System.Diagnostics.Trace.WriteLine($"{session.app.Name}: {session.context.Bearer}");
                foreach (var p in session.context.ContextProperties)
                {
                    System.Diagnostics.Trace.WriteLine($"  {p.Key}: {p.Value}");
                }
            }
            if (session == null)
                return null;
            return new FhirFacadeProtocolSchemeHandler<TSP>(session.app, session.context, _fhirServerBaseUrl, _identityServerBaseUrl, _facadeFactory(), _applySmartScopes);
        }
    }

    public class FhirFacadeProtocolSchemeHandler<TSP> : FhirBaseProtocolSchemeHandler
        where TSP : class
    {
        readonly string[] SearchQueryParameterNames = { "_summary", "_sort", "_count", "_format" };
        readonly string[] OperationQueryParameterNames = { "_summary", "_format" };


        public FhirFacadeProtocolSchemeHandler(SmartApplicationDetails app, IFhirSmartAppContext launchContext, string fhirServerBaseUrl, string identityServerBaseUrl, IFhirSystemServiceR4<TSP> facade, bool applySmartScopes)
            : base(app, launchContext, fhirServerBaseUrl, identityServerBaseUrl)
        {
            _facade = facade;
            _applySmartScopes = applySmartScopes;
        }
        private IFhirSystemServiceR4<TSP> _facade;
        private bool _applySmartScopes;


        /// <summary>
        /// Process request and craft response.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="callback"></param>
        /// <returns></returns>
        /// <remarks>
        /// Sample of processing the background execution using Task.Run from the cefsharp documentation
        /// https://github.com/cefsharp/CefSharp/blob/20dad8aced51e077780ce3fe3b9a3766c15a7102/CefSharp.Example/FlashResourceHandler.cs#L13
        /// </remarks>
        public override CefReturnValue ProcessRequestAsync(IRequest request, ICallback callback)
        {
            if (request.Method == "OPTIONS")
            {
                return WriteOptionsOutput(callback);
            }
            var uri = new Uri(request.Url);
            Console.WriteLine($"-----------------\r\n{request.Url}");

            // Check the bearer header (as all calls to this API MUST be provided the bearer token, otherwise are straight rejected)
            string bearer = request.GetHeaderByName("authorization");
            if (!string.IsNullOrEmpty(_launchContext.Bearer))
            {
                if (bearer != "Bearer " + _launchContext.Bearer)
                {
                    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.Security, "Invalid Bearer token provided for this request");
                    return CefReturnValue.Continue;
                }
            }
            // TODO: Check that the session hasn't expired (not critical as the window is within the control of the PMS system)
            //if (_launchContext.ExpiresAt < DateTimeOffset.Now) 
            //{
            //    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.Security, "Authorization has expired, please re-authorize");
            //    return CefReturnValue.Continue;
            //}

            try
            {
                // This is a regular FHIR request
                var headers = new List<KeyValuePair<string, IEnumerable<string>>>();
                foreach (var key in request.Headers?.AllKeys)
                {
                    headers.Add(new KeyValuePair<string, IEnumerable<string>>(key, request.Headers.GetValues(key)));
                }
                ModelBaseInputs<TSP> requestDetails = new ModelBaseInputs<TSP>(_launchContext.Principal, null, request.Method, uri, new Uri($"https://{_fhirServerBaseUrl}"), null, headers, null);
                if (request.Method == "GET")
                {
                    // The metadata routes are the only ones that are permitted without the bearer token
                    if (uri.LocalPath == "/.well-known/smart-configuration")
                    {
                        return ProcessWellKnownSmartConfigurationRequest(callback);
                    }
                    if (uri.LocalPath == "/metadata" || uri.LocalPath == "/")
                    {
                        return ProcessFhirMetadataRequest(callback, requestDetails);
                    }

                    if (string.IsNullOrEmpty(bearer))
                    {
                        SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.Security, "No Bearer token provided for this request");
                        return CefReturnValue.Continue;
                    }

                    if (!uri.LocalPath.StartsWith("/$") && !uri.LocalPath.StartsWith("/_") && uri.LocalPath.Length > 2)
                    {
                        // This is not a system operation or history, so it must be a resource type
                        string resourceType = uri.LocalPath.Substring(1);
                        if (resourceType.Contains("/"))
                            resourceType = resourceType.Substring(0, resourceType.IndexOf("/"));
                        if (!string.IsNullOrEmpty(resourceType))
                        {
                            var rs = _facade.GetResourceService(requestDetails, resourceType);
                            if (rs == null)
                            {
                                SetErrorResponse(callback, HttpStatusCode.NotFound, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotSupported, $"Resource type {resourceType} is not supported");
                                return CefReturnValue.Continue;
                            }
                            // For queries that leverage the summary parameter (that's on the URL
                            var summary = GetSummaryParameter(uri);

                            // GET for a specific resource
                            ResourceIdentity ri = new ResourceIdentity(uri);
                            if (ri.IsRestResourceIdentity())
                            {
                                // Check that this is within the smart context
                                if (_applySmartScopes)
                                {
                                    var scopeRead = SmartScopes.HasSecureAccess_SmartOnFhir(requestDetails, resourceType, SmartOperation.read);
                                    if (scopeRead?.ReadAccess == false)
                                    {
                                        SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User?.Identity.Name}/App {_app.Name} does not have read access on {resourceType}");
                                        return CefReturnValue.Continue;
                                    }
                                    if (resourceType == "Patient")
                                    {
                                        if (requestDetails.User.PatientInContext() != ri.Id)
                                        {
                                            SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User?.Identity.Name}/App {_app.Name} does not have access to {resourceType}/{ri.Id}");
                                            return CefReturnValue.Continue;
                                        }
                                    }
                                }
                                System.Threading.Tasks.Task.Run(() => rs.Get(ri.Id, ri.VersionId, summary).ContinueWith(r =>
                                {
                                    if (r.Exception != null)
                                    {
                                        SetErrorResponse(callback, r.Exception);
                                        return null;
                                    }
                                    if (r.Result == null)
                                    {
                                        SetErrorResponse(callback, HttpStatusCode.NotFound, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotFound, $"Resource {resourceType}/{ri.Id} was not found");
                                        return null;
                                    }
                                    else
                                    {
                                        // Check for security access to this resource before returning it
                                        if (resourceType != "Patient")
                                        {
                                            // This will need to be done by the Model classes, not the facade
                                        }

                                        // All good return the resource
                                        var statusCode = r.Result?.HasAnnotation<HttpStatusCode>() == true ? r.Result.Annotation<HttpStatusCode>() : HttpStatusCode.OK;
                                        SetResponse(callback, statusCode, r.Result);
                                    }
                                    return r.Result;
                                }));
                                return CefReturnValue.ContinueAsync;
                            }

                            // Search for the resource type
                            var parameters = TupledParameters(uri, SearchQueryParameterNames).ToList(); // convert to a list so that we can append the patient ID when required
                            int? pagesize = GetIntParameter(uri, FhirParameter.COUNT);
                            if (_applySmartScopes)
                            {
                                var scopeSearch = SmartScopes.HasSecureAccess_SmartOnFhir(requestDetails, resourceType, SmartOperation.search);
                                if (scopeSearch?.SearchAccess == false)
                                {
                                    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User?.Identity.Name}/App {_app.Name} does not have search access on {resourceType}");
                                    return CefReturnValue.Continue;
                                }
                                if (scopeSearch?.SmartUserType == SmartUserType.patient)
                                {
                                    if (!string.IsNullOrEmpty(requestDetails.User.PatientInContext()))
                                    {
                                        if (resourceType == "Patient")
                                        {
                                            parameters.Add(new KeyValuePair<string, string>("_id", requestDetails.User.PatientInContext()));
                                        }
                                        else if (Hl7.Fhir.Model.ModelInfo.SearchParameters.Any(sp => sp.Resource == resourceType && sp.Name == "patient"))
                                        {
                                            parameters.Add(new KeyValuePair<string, string>("patient", requestDetails.User.PatientInContext()));
                                        }
                                        else if (Hl7.Fhir.Model.ModelInfo.SearchParameters.Any(sp => sp.Resource == resourceType && sp.Name == "subject"))
                                        {
                                            parameters.Add(new KeyValuePair<string, string>("subject", requestDetails.User.PatientInContext()));
                                        }
                                    }
                                }
                            }
                            System.Threading.Tasks.Task.Run(() => rs.Search(parameters, pagesize, summary).ContinueWith(r =>
                            {
                                if (r.Exception != null)
                                {
                                    SetErrorResponse(callback, r.Exception);
                                    return null;
                                }
                                var statusCode = r.Result.HasAnnotation<HttpStatusCode>() ? r.Result.Annotation<HttpStatusCode>() : HttpStatusCode.OK;
                                SetResponse(callback, statusCode, r.Result);
                                return r.Result;
                            }));
                            return CefReturnValue.ContinueAsync;
                        }

                        // This was not a recognized GET request, so we can just respond with a 404
                        SetErrorResponse(callback, HttpStatusCode.NotFound, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotSupported, "Unsupported GET operation");
                        return CefReturnValue.Continue;
                    }

                    // This was not a recognized GET request, so we can just respond with a 404
                    SetErrorResponse(callback, HttpStatusCode.NotFound, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotSupported, "Unsupported system level GET operation");
                    return CefReturnValue.Continue;
                }
                if (request.Method == "POST")
                {
                    if (string.IsNullOrEmpty(bearer))
                    {
                        SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.Security, "No Bearer token provided for this request");
                        return CefReturnValue.Continue;
                    }
                    if (uri.LocalPath == "/")
                    {
                        Bundle b = null;
                        if (request.PostData != null)
                        {
                            var data = request.PostData.Elements.FirstOrDefault();
                            var body = data.GetBody();
                            if (request.GetHeaderByName("Content-Type").Contains("xml"))
                                b = new Hl7.Fhir.Serialization.FhirXmlParser().Parse<Bundle>(body);
                            else
                                b = new Hl7.Fhir.Serialization.FhirJsonParser().Parse<Bundle>(body);
                        }

                        System.Threading.Tasks.Task.Run(() => _facade.ProcessBatch(requestDetails, b).ContinueWith(r =>
                        {
                            if (r.Exception != null)
                            {
                                SetErrorResponse(callback, r.Exception);
                                return null;
                            }
                            var statusCode = r.Result.HasAnnotation<HttpStatusCode>() ? r.Result.Annotation<HttpStatusCode>() : HttpStatusCode.OK;
                            SetResponse(callback, statusCode, r.Result);
                            return r.Result;
                        }));
                        return CefReturnValue.ContinueAsync;
                    }

                    // TODO: support creating new resources

                    // This was not a recognized POST request, so we can just respond with a 404
                    SetErrorResponse(callback, HttpStatusCode.BadRequest, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotSupported, "Unsupported POST operation");
                    return CefReturnValue.Continue;
                }
                if (request.Method == "PUT")
                {
                    if (string.IsNullOrEmpty(bearer))
                    {
                        SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.Security, "No Bearer token provided for this request");
                        return CefReturnValue.Continue;
                    }
                    // TODO: support updating resources (or creating with client allocated ID)

                    // This was not a recognized PUT request, so we can just respond with a 404
                    SetErrorResponse(callback, HttpStatusCode.BadRequest, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotSupported, "Unsupported PUT operation");
                    return CefReturnValue.Continue;
                }

                // This was an unknown request type
                SetErrorResponse(callback, HttpStatusCode.BadRequest, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.NotSupported, "Unknown request");
                return CefReturnValue.Continue;
            }
            catch (Exception ex)
            {
                // Totally unknown request encountered
                SetErrorResponse(callback, ex);
                return CefReturnValue.Continue;
            }
        }

        private CefReturnValue ProcessFhirMetadataRequest(ICallback callback, ModelBaseInputs<TSP> requestDetails)
        {
            System.Threading.Tasks.Task.Run(() => _facade.GetConformance(requestDetails, Rest.SummaryType.False).ContinueWith<CapabilityStatement>(r =>
            {
                if (r.Exception != null)
                {
                    SetErrorResponse(callback, r.Exception);
                    return null;
                }
                base.StatusCode = (int)HttpStatusCode.OK;

                // As per the documentation http://hl7.org/fhir/smart-app-launch/conformance/index.html
                CapabilityStatement cs = r.Result;

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
                extension.AddExtension("token", new FhirUri($"https://{_identityServerBaseUrl}/token"));
                extension.AddExtension("authorize", new FhirUri($"https://{_identityServerBaseUrl}/authorize"));

                base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(r.Result));
                Console.WriteLine($"Success: {base.Stream.Length}");
                base.MimeType = "application/fhir+json";
                if (!callback.IsDisposed)
                    callback.Continue();
                return r.Result;
            }));
            return CefReturnValue.ContinueAsync;
        }

        private CefReturnValue ProcessWellKnownSmartConfigurationRequest(ICallback callback)
        {
            base.StatusCode = (int)HttpStatusCode.OK;

            // http://build.fhir.org/ig/HL7/smart-app-launch/conformance.html
            FhirSmartAppLaunchConfiguration smart_config = new FhirSmartAppLaunchConfiguration();
            // populate the context based on the data we know
            smart_config.issuer = $"https://{_identityServerBaseUrl}";
            smart_config.authorization_endpoint = $"https://{_identityServerBaseUrl}/authorize";
            smart_config.token_endpoint = $"https://{_identityServerBaseUrl}/token";
            smart_config.scopes_supported = _app.AllowedScopes;
            smart_config.response_types_supported = new[] { "code", "code id_token" };
            var capabilities = new List<string> { "launch-ehr", "permission-v2", "context-ehr-patient", "authorize-post" };
            if (String.IsNullOrEmpty(_app.ClientSecret))
                capabilities.Add("client-public");
            else
                capabilities.Add("client-confidential-symmetric");
            smart_config.capabilities = capabilities;

            var jsonSettings = new Newtonsoft.Json.JsonSerializerSettings() { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore };
            base.Stream = new MemoryStream();
            StreamWriter sw = new StreamWriter(base.Stream, System.Text.Encoding.UTF8, 4096, true);
            sw.Write(Newtonsoft.Json.JsonConvert.SerializeObject(smart_config, settings: jsonSettings));
            sw.Flush();
            base.Headers.Add("Cache-Control", "no-store");
            base.Headers.Add("Pragma", "no-cache");
            base.MimeType = "application/json;charset=UTF-8";

            Console.WriteLine($"Success: {base.Stream.Length}");
            if (!callback.IsDisposed)
                callback.Continue();
            return CefReturnValue.Continue;
        }

        /// <summary>
        /// Retrieve all the parameters from a Request URL
        /// </summary>
        /// <param name="requestUri"></param>
        /// <param name="excludeParameters">Do not include any parameters from the provided collection</param>
        /// <returns></returns>
        public static IEnumerable<KeyValuePair<string, string>> TupledParameters(Uri requestUri, string[] excludeParameters = null)
        {
            var list = new List<KeyValuePair<string, string>>();

            string query = requestUri.OriginalString;
            if (query.Contains("?"))
            {
                query = query.Substring(query.IndexOf("?") + 1);
                System.Collections.Specialized.NameValueCollection nvp = System.Web.HttpUtility.ParseQueryString(query);

                if (nvp.HasKeys())
                {
                    foreach (string key in nvp.Keys)
                    {
                        if (excludeParameters == null || !excludeParameters.Contains(key))
                        {
                            foreach (string val in nvp.GetValues(key))
                                list.Add(new KeyValuePair<string, string>(key, val));
                        }
                    }
                }
            }
            return list;
        }

        public static string GetParameter(Uri requestUri, string keyParam)
        {
            System.Collections.Specialized.NameValueCollection nvp = System.Web.HttpUtility.ParseQueryString(requestUri.OriginalString);

            if (nvp.HasKeys())
            {
                return nvp.GetValues(keyParam)?.FirstOrDefault();
            }
            return null;
        }

        public static Hl7.Fhir.Rest.SummaryType GetSummaryParameter(Uri request)
        {
            string s = GetParameter(request, FhirParameter.SUMMARY);
            if (s == null)
                return Hl7.Fhir.Rest.SummaryType.False;

            switch (s.ToLower())
            {
                case "true": return Hl7.Fhir.Rest.SummaryType.True;
                case "false": return Hl7.Fhir.Rest.SummaryType.False;
                case "text": return Hl7.Fhir.Rest.SummaryType.Text;
                case "data": return Hl7.Fhir.Rest.SummaryType.Data;
                case "count": return Hl7.Fhir.Rest.SummaryType.Count;
                default: return Hl7.Fhir.Rest.SummaryType.False;
            }
        }
        public static int? GetIntParameter(Uri request, string name)
        {
            string s = GetParameter(request, name);
            int n;
            return (int.TryParse(s, out n)) ? n : (int?)null;
        }
    }

    public static class FhirParameter
    {
        public const string SUMMARY = "_summary";
        public const string COUNT = "_count";
        public const string SINCE = "_since";
        public const string SORT = "_sort";
    }
}
