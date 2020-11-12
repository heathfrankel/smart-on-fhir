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
        public FhirFacadeProtocolSchemeHandlerFactory(IFhirSmartAppContext launchContext, Func<IFhirSystemServiceR4<IServiceProvider>> facadeFactory)
        {
            _launchContext = launchContext;
            _facadeFactory = facadeFactory;
        }
        private IFhirSmartAppContext _launchContext;
        private Func<IFhirSystemServiceR4<IServiceProvider>> _facadeFactory;

        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
        {
            return new FhirFacadeProtocolSchemeHandler(_launchContext, _facadeFactory());
        }
    }

    public class FhirFacadeProtocolSchemeHandler : ResourceHandler
    {
        private IFhirSmartAppContext _launchContext;
        private IFhirSystemServiceR4<IServiceProvider> _facade;

        readonly string[] SearchQueryParameterNames = { "_summary", "_sort", "_count", "_format" };
        readonly string[] OperationQueryParameterNames = { "_summary", "_format" };


        public FhirFacadeProtocolSchemeHandler(IFhirSmartAppContext launchContext, IFhirSystemServiceR4<IServiceProvider> facade)
        {
            _launchContext = launchContext;
            _facade = facade;
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
                // This is a regular request
                Hl7.Fhir.Rest.FhirClient server = new Hl7.Fhir.Rest.FhirClient("http://localhost:4178");
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
                var headers = new List<KeyValuePair<string, IEnumerable<string>>>();
                ModelBaseInputs<IServiceProvider> requestDetails = new ModelBaseInputs<IServiceProvider>(null, null, request.Method, uri, new Uri($"https://{_launchContext.LaunchContext}.comcare-fhir.localhost"), null, headers, null);
                if (request.Method == "GET")
                {
                    if (uri.LocalPath == "/metadata")
                    {
                        _facade.GetConformance(requestDetails, Rest.SummaryType.False).ContinueWith<CapabilityStatement>(r => 
                        {
                            if (r.Exception != null)
                            {
                                System.Diagnostics.Trace.WriteLine($"Error: {r.Exception.Message}");
                                if (r.Exception.InnerException is Hl7.Fhir.Rest.FhirOperationException fe)
                                {
                                    base.StatusCode = (int)fe.Status;
                                    if (fe.Outcome != null)
                                    {
                                        base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(fe.Outcome));
                                        base.MimeType = "application/fhir+json";
                                    }
                                    callback.Continue();
                                    System.Diagnostics.Trace.WriteLine($"Error (inner): {fe.Message}");
                                    return null;
                                }
                            }
                            base.StatusCode = 200;

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
                            extension.AddExtension("token", new FhirUri($"https://{_launchContext.LaunchContext}.identity.localhost/token"));
                            extension.AddExtension("authorize", new FhirUri($"https://{_launchContext.LaunchContext}.identity.localhost/authorize"));

                            base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(r.Result));
                            Console.WriteLine($"Success: {base.Stream.Length}");
                            base.MimeType = "application/fhir+json";
                            callback.Continue();
                            return r.Result;
                        });
                        return CefReturnValue.ContinueAsync;
                    }

                    if (!uri.LocalPath.StartsWith("/$") && !uri.LocalPath.StartsWith("/_") && uri.LocalPath.Length > 2)
                    {
                        // This is not an operation or history, so it must be a resource type
                        string resourceType = uri.LocalPath.Substring(1);
                        if (resourceType.Contains("/"))
                            resourceType = resourceType.Substring(0, resourceType.IndexOf("/"));
                        var rs = _facade.GetResourceService(requestDetails, resourceType);
                        if (rs == null)
                        {
                            System.Diagnostics.Trace.WriteLine($"Error: resource type not handled {resourceType}");
                            base.StatusCode = (int)HttpStatusCode.BadRequest;
                            //    base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(fe.Outcome));
                            //    base.MimeType = "application/fhir+json";
                            callback.Continue();
                            return CefReturnValue.Continue;
                        }

                        // GET for a specific resource
                        ResourceIdentity ri = new ResourceIdentity(uri);
                        if (ri.IsRestResourceIdentity())
                        {
                            rs.Get(ri.Id, ri.VersionId, SummaryType.False).ContinueWith(r =>
                            {
                                if (r.Exception != null)
                                {
                                    System.Diagnostics.Trace.WriteLine($"Error: {r.Exception.Message}");
                                    if (r.Exception.InnerException is Hl7.Fhir.Rest.FhirOperationException fe)
                                    {
                                        base.StatusCode = (int)fe.Status;
                                        if (fe.Outcome != null)
                                        {
                                            base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(fe.Outcome));
                                            base.MimeType = "application/fhir+json";
                                        }
                                        callback.Continue();
                                        System.Diagnostics.Trace.WriteLine($"Error (inner): {fe.Message}");
                                        return null;
                                    }
                                }
                                if (r.Result.HasAnnotation<HttpStatusCode>())
                                    base.StatusCode = (int)r.Result.Annotation<HttpStatusCode>();
                                else
                                    base.StatusCode = 200;

                                base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(r.Result));
                                Console.WriteLine($"Success: {base.Stream.Length}");
                                base.MimeType = "application/fhir+json";
                                callback.Continue();
                                return r.Result;
                            });
                            return CefReturnValue.ContinueAsync;
                        }

                        // Search for the resource type
                        var parameters = TupledParameters(uri, SearchQueryParameterNames);
                        int? pagesize = GetIntParameter(uri, FhirParameter.COUNT);
                        var summary = GetSummaryParameter(uri);
                        rs.Search(parameters, pagesize, summary).ContinueWith(r =>
                        {
                            if (r.Exception != null)
                            {
                                System.Diagnostics.Trace.WriteLine($"Error: {r.Exception.Message}");
                                if (r.Exception.InnerException is Hl7.Fhir.Rest.FhirOperationException fe)
                                {
                                    base.StatusCode = (int)fe.Status;
                                    if (fe.Outcome != null)
                                    {
                                        base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(fe.Outcome));
                                        base.MimeType = "application/fhir+json";
                                    }
                                    callback.Continue();
                                    System.Diagnostics.Trace.WriteLine($"Error (inner): {fe.Message}");
                                    return null;
                                }
                            }
                            if (r.Result.HasAnnotation<HttpStatusCode>())
                                base.StatusCode = (int)r.Result.Annotation<HttpStatusCode>();
                            else
                                base.StatusCode = 200;

                            base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(r.Result));
                            Console.WriteLine($"Success: {base.Stream.Length}");
                            base.MimeType = "application/fhir+json";
                            callback.Continue();
                            return r.Result;
                        });
                        return CefReturnValue.ContinueAsync;
                    }

                    System.Threading.Tasks.Task<Hl7.Fhir.Model.Resource> t = server.GetAsync(redirectedUrl).ContinueWith<Hl7.Fhir.Model.Resource>(r =>
                    {
                        if (r.Exception != null)
                        {
                            System.Diagnostics.Trace.WriteLine($"Error: {r.Exception.Message}");
                            if (r.Exception.InnerException is Hl7.Fhir.Rest.FhirOperationException fe)
                            {
                                base.StatusCode = (int)fe.Status;
                                if (fe.Outcome != null)
                                {
                                    base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(fe.Outcome));
                                    base.MimeType = "application/fhir+json";
                                }
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
                            extension.AddExtension("token", new FhirUri($"https://{_launchContext.LaunchContext}.identity.localhost/token"));
                            extension.AddExtension("authorize", new FhirUri($"https://{_launchContext.LaunchContext}.identity.localhost/authorize"));
                        }

                        base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(r.Result));
                        Console.WriteLine($"Success: {base.Stream.Length}");
                        base.MimeType = "application/fhir+json";
                        callback.Continue();
                        return r.Result;
                    });
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

                        _facade.ProcessBatch(requestDetails, b).ContinueWith(r =>
                        {
                            if (r.Exception != null)
                            {
                                System.Diagnostics.Trace.WriteLine($"Error: {r.Exception.Message}");
                                if (r.Exception.InnerException is Hl7.Fhir.Rest.FhirOperationException fe)
                                {
                                    base.StatusCode = (int)fe.Status;
                                    if (fe.Outcome != null)
                                    {
                                        base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(fe.Outcome));
                                        base.MimeType = "application/fhir+json";
                                    }
                                    callback.Continue();
                                    System.Diagnostics.Trace.WriteLine($"Error (inner): {fe.Message}");
                                    return null;
                                }
                            }
                            if (r.Result.HasAnnotation<HttpStatusCode>())
                                base.StatusCode = (int)r.Result.Annotation<HttpStatusCode>();
                            else
                                base.StatusCode = 200;

                            base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(r.Result));
                            Console.WriteLine($"Success: {base.Stream.Length}");
                            base.MimeType = "application/fhir+json";
                            callback.Continue();
                            return r.Result;
                        });
                        return CefReturnValue.ContinueAsync;
                    }

                    System.Net.Http.HttpClient client = new System.Net.Http.HttpClient();
                    client.DefaultRequestHeaders.Add("Accept", request.GetHeaderByName("Accept"));
                    // client.DefaultRequestHeaders.Add("Content-Type", request.GetHeaderByName("Content-Type"));
                    HttpContent content = null;
                    if (request.PostData != null)
                    {
                        var data = request.PostData.Elements.FirstOrDefault();
                        var body = data.GetBody();
                        content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, request.GetHeaderByName("Content-Type"));
                    }
                    else
                    {
                        content = new System.Net.Http.StreamContent(null);
                    }
                    client.PostAsync(redirectedUrl, content).ContinueWith((System.Threading.Tasks.Task<HttpResponseMessage> r) =>
                    {
                        if (r.Exception != null)
                        {
                            Console.WriteLine($"Error: {r.Exception.Message}");
                            //if (r.Exception.InnerException is Hl7.Fhir.Rest.FhirOperationException fe)
                            //{
                            //    base.StatusCode = (int)fe.Status;
                            //    if (fe.Outcome != null)
                            //        base.Stream = new MemoryStream(r.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                            //    callback.Continue();
                            //    System.Diagnostics.Trace.WriteLine($"Error (inner): {fe.Message}");
                            //    return;
                            //}
                        }
                        base.StatusCode = (int)r.Result.StatusCode;

                        base.Stream = r.Result.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                        Console.WriteLine($"Success: {base.Stream.Length}");
                        base.MimeType = r.Result.Content.Headers.ContentType.MediaType;
                        callback.Continue();
                        return;
                    });
                }
                if (request.Method == "PUT")
                {
                    System.Net.Http.HttpClient client = new System.Net.Http.HttpClient();
                    client.DefaultRequestHeaders.Add("Accept", request.GetHeaderByName("Accept"));
                    // client.DefaultRequestHeaders.Add("Content-Type", request.GetHeaderByName("Content-Type"));
                    HttpContent content = null;
                    if (request.PostData != null)
                    {
                        var data = request.PostData.Elements.FirstOrDefault();
                        var body = data.GetBody();
                        content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, request.GetHeaderByName("Content-Type"));
                    }
                    else
                    {
                        content = new System.Net.Http.StreamContent(null);
                    }
                    client.PutAsync(redirectedUrl, content).ContinueWith((System.Threading.Tasks.Task<HttpResponseMessage> r) =>
                    {
                        if (r.Exception != null)
                        {
                            Console.WriteLine($"Error: {r.Exception.Message}");
                            //if (r.Exception.InnerException is Hl7.Fhir.Rest.FhirOperationException fe)
                            //{
                            //    base.StatusCode = (int)fe.Status;
                            //    if (fe.Outcome != null)
                            //        base.Stream = new MemoryStream(r.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                            //    callback.Continue();
                            //    System.Diagnostics.Trace.WriteLine($"Error (inner): {fe.Message}");
                            //    return;
                            //}
                        }
                        base.StatusCode = (int)r.Result.StatusCode;

                        base.Stream = r.Result.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                        Console.WriteLine($"Success: {base.Stream.Length}");
                        base.MimeType = r.Result.Content.Headers.ContentType.MediaType;
                        callback.Continue();
                        return;
                    });
                }
                return CefReturnValue.ContinueAsync;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
                callback.Dispose();
                return CefReturnValue.Cancel;
            }
        }

        /// <summary>
        /// Retrieve all the parameters from the Request
        /// </summary>
        /// <param name="request"></param>
        /// <param name="excludeParameters">Do not include any parameters from the provided collection</param>
        /// <returns></returns>
        public static IEnumerable<KeyValuePair<string, string>> TupledParameters(Uri requestUri, string[] excludeParameters = null)
        {
            var list = new List<KeyValuePair<string, string>>();

            System.Collections.Specialized.NameValueCollection nvp = System.Web.HttpUtility.ParseQueryString(requestUri.OriginalString);

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
            return list;
        }

        public static string GetParameter(Uri requestUri, string keyParam)
        {
            System.Collections.Specialized.NameValueCollection nvp = System.Web.HttpUtility.ParseQueryString(requestUri.OriginalString);

            if (nvp.HasKeys())
            {
                return nvp.GetValues(keyParam).FirstOrDefault();
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
