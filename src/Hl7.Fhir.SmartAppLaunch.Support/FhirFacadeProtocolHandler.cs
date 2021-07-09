using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using CefSharp;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.SmartAppLaunch;
using Hl7.Fhir.Support;
using Hl7.Fhir.Utility;
using Hl7.Fhir.WebApi;
using Newtonsoft.Json;

namespace Hl7.Fhir.SmartAppLaunch
{
    public class FhirFacadeProtocolSchemeHandlerFactory : ISchemeHandlerFactory
    {
        public FhirFacadeProtocolSchemeHandlerFactory(SmartApplicationDetails app, IFhirSmartAppContext launchContext, Func<IFhirSystemServiceR4<IServiceProvider>> facadeFactory)
        {
            _app = app;
            _launchContext = launchContext;
            _facadeFactory = facadeFactory;
        }
        private SmartApplicationDetails _app;
        private IFhirSmartAppContext _launchContext;
        private Func<IFhirSystemServiceR4<IServiceProvider>> _facadeFactory;

        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
        {
            return new FhirFacadeProtocolSchemeHandler(_app, _launchContext, _facadeFactory());
        }
    }

    public class FhirFacadeProtocolSchemeHandler : ResourceHandler
    {
        readonly string[] SearchQueryParameterNames = { "_summary", "_sort", "_count", "_format" };
        readonly string[] OperationQueryParameterNames = { "_summary", "_format" };


        public FhirFacadeProtocolSchemeHandler(SmartApplicationDetails app, IFhirSmartAppContext launchContext, IFhirSystemServiceR4<IServiceProvider> facade)
        {
            _app = app;
            _launchContext = launchContext;
            _facade = facade;
        }
        private SmartApplicationDetails _app;
        private IFhirSmartAppContext _launchContext;
        private IFhirSystemServiceR4<IServiceProvider> _facade;


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
                // This is the CORS request, and that's good as we do want to support CORS calls to our "facade"
                base.StatusCode = 200;
                //if (!string.IsNullOrEmpty(_app.AllowedHosts))
                //    base.Headers.Add("Access-Control-Allow-Origin", _app.AllowedHosts);
                base.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,OPTIONS");
                base.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept, authorization");
                callback.Continue();
                return CefReturnValue.Continue;
            }
            var uri = new Uri(request.Url);
            Console.WriteLine($"-----------------\r\n{request.Url}");

            // Check the bearer header (as all calls to this API MUST be provided the bearer token, otherwise are straight rejected)
            if (!string.IsNullOrEmpty(_launchContext.Bearer))
            {
                string bearer = request.GetHeaderByName("authorization");
                if (bearer != "Bearer " + _launchContext.Bearer)
                {
                    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.Security, "No Bearer token provided for this request");
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
                ModelBaseInputs<IServiceProvider> requestDetails = new ModelBaseInputs<IServiceProvider>(_launchContext.Principal, null, request.Method, uri, new Uri($"https://{AuthProtocolSchemeHandlerFactory.FhirFacadeAddress(_launchContext)}"), null, headers, null);
                if (request.Method == "GET")
                {
                    if (uri.LocalPath == "/.well-known/smart-configuration")
                    {
                        return ProcessWellKnownSmartConfigurationRequest(callback);
                    }
                    if (uri.LocalPath == "/metadata" || uri.LocalPath == "/")
                    {
                        return ProcessFhirMetadataRequest(callback, requestDetails);
                    }

                    if (!uri.LocalPath.StartsWith("/$") && !uri.LocalPath.StartsWith("/_") && uri.LocalPath.Length > 2)
                    {
                        // This is not an operation or history, so it must be a resource type
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
                                System.Threading.Tasks.Task.Run(() => rs.Get(ri.Id, ri.VersionId, summary).ContinueWith(r =>
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

                            // Search for the resource type
                            var parameters = TupledParameters(uri, SearchQueryParameterNames);
                            int? pagesize = GetIntParameter(uri, FhirParameter.COUNT);
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
                        SetErrorResponse(callback, HttpStatusCode.NotFound, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotSupported, "Unsupported system level GET operation");
                        return CefReturnValue.Continue;
                    }

                    // This was not a recognized GET request, so we can just respond with a 404
                    SetErrorResponse(callback, HttpStatusCode.NotFound, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotSupported, "Unsupported GET operation");
                    return CefReturnValue.Continue;
                }
                if (request.Method == "POST")
                {
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

        /// <summary>
        /// Send a custom error message back
        /// </summary>
        /// <param name="callback"></param>
        /// <param name="status"></param>
        /// <param name="severity"></param>
        /// <param name="issueType"></param>
        /// <param name="message"></param>
        /// <param name="coding"></param>
        private void SetErrorResponse(ICallback callback, HttpStatusCode status, OperationOutcome.IssueSeverity severity, OperationOutcome.IssueType issueType, string message, Coding coding = null)
        {
            System.Diagnostics.Trace.WriteLine($"Error: {message}");
            OperationOutcome result = new OperationOutcome();
            var issue = new OperationOutcome.IssueComponent()
            {
                Severity = severity,
                Code = issueType,
                Details = new CodeableConcept() { Text = message }
            };
            if (coding != null)
                issue.Details.Coding.Add(coding);
            result.Issue.Add(issue);

            SetResponse(callback, status, result);  
        }

        /// <summary>
        /// Set this FHIR Resource as the response to the request
        /// </summary>
        /// <param name="callback"></param>
        /// <param name="status"></param>
        /// <param name="resource"></param>
        private void SetResponse(ICallback callback, HttpStatusCode status, Resource resource)
        {
            base.Headers.Add("Cache-Control", "no-store");
            base.Headers.Add("Pragma", "no-cache");
            base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(resource));
            base.MimeType = "application/fhir+json";
            base.StatusCode = (int)status;

            if (!callback.IsDisposed)
                callback.Continue();
        }

        private CefReturnValue ProcessFhirMetadataRequest(ICallback callback, ModelBaseInputs<IServiceProvider> requestDetails)
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
                extension.AddExtension("token", new FhirUri($"https://{AuthProtocolSchemeHandlerFactory.AuthAddress(_launchContext)}/token"));
                extension.AddExtension("authorize", new FhirUri($"https://{AuthProtocolSchemeHandlerFactory.AuthAddress(_launchContext)}/authorize"));

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
            smart_config.issuer = _app.Issuer;
            smart_config.authorization_endpoint = $"https://{AuthProtocolSchemeHandlerFactory.AuthAddress(_launchContext)}/authorize";
            smart_config.token_endpoint = $"https://{AuthProtocolSchemeHandlerFactory.AuthAddress(_launchContext)}/token";
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
            callback.Continue();
            return CefReturnValue.Continue;
        }

        private void SetErrorResponse(ICallback callback, Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error: {ex.Message}");
            base.Headers.Add("Cache-Control", "no-store");
            base.Headers.Add("Pragma", "no-cache");

            OperationOutcome result = new OperationOutcome();
            HttpStatusCode? status = null;
            // process the exception (and all it's inner exceptions
            while (ex != null)
            {
                if (ex is Hl7.Fhir.Rest.FhirOperationException fe)
                {
                    if (!status.HasValue)
                        status = fe.Status;
                    if (fe.Outcome != null)
                    {
                        result.Issue.AddRange(fe.Outcome.Issue);
                    }
                }
                else
                {
                    result.Issue.Add(new OperationOutcome.IssueComponent()
                    {
                        Severity = OperationOutcome.IssueSeverity.Fatal,
                        Code = OperationOutcome.IssueType.Exception,
                        Details = new CodeableConcept() { Text = ex.Message }
                    });
                }
                ex = ex.InnerException;
            }

            base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(result));
            base.MimeType = "application/fhir+json";
            if (status.HasValue)
                base.StatusCode = (int)status.Value;
            else
                base.StatusCode = (int)HttpStatusCode.InternalServerError;

            if (!callback.IsDisposed)
                callback.Continue();
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
                query = query.Substring(query.IndexOf("?")+1);
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
