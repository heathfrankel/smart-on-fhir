using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using CefSharp;
using Hl7.Fhir.Model;
using Hl7.Fhir.WebApi;

namespace Hl7.Fhir.SmartAppLaunch
{
    /// <summary>
    /// This is a Demonstration Facade Handler that pretends that the server at the provided address is a local FHIR Facade running in-process
    /// It does not do any authentication with the remote service.
    /// </summary>
    public class FhirProxyProtocolSchemeHandlerFactory : FhirBaseProtocolSchemeHandlerFactory, ISchemeHandlerFactory
    {
        public FhirProxyProtocolSchemeHandlerFactory(SmartSessions sessionManager, string fhirServerBaseUrl, string identityServerBaseUrl, string externalFhirServerBaseUrl)
            : base(sessionManager, fhirServerBaseUrl, identityServerBaseUrl)
        {
            _externalFhirServerBaseUrl = externalFhirServerBaseUrl;
        }
        private string _externalFhirServerBaseUrl;

        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
        {
            var session = _sessionManager.GetSession(browser.MainFrame.Identifier);
            System.Diagnostics.Trace.WriteLine($"{session.app.Name}: {session.context.Bearer}");
            foreach (var p in session.context.ContextProperties)
            {
                System.Diagnostics.Trace.WriteLine($"  {p.Key}: {p.Value}");
            }

            System.Diagnostics.Trace.WriteLine($"{request.Method}: {request.Url}");
            System.Diagnostics.Trace.WriteLine($"  Content-type: {request.Headers["Content-Type"]}");
            string HeaderAuth = request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(HeaderAuth))
            {
                System.Diagnostics.Trace.WriteLine($"  Authorization: {HeaderAuth}");
            }
            return new FhirProxyProtocolSchemeHandler(session.app, session.context, _fhirServerBaseUrl, _identityServerBaseUrl, _externalFhirServerBaseUrl);
        }
    }

    public class FhirProxyProtocolSchemeHandler : FhirBaseProtocolSchemeHandler
    {
        public FhirProxyProtocolSchemeHandler(SmartApplicationDetails app, IFhirSmartAppContext launchContext, string fhirServerBaseUrl, string identityServerBaseUrl, string externalFhirServerBaseUrl)
            : base(app, launchContext, fhirServerBaseUrl, identityServerBaseUrl)
        {
            _externalFhirServerBaseUrl = externalFhirServerBaseUrl;
        }
        private string _externalFhirServerBaseUrl;

        // Process request and craft response.
        public override CefReturnValue ProcessRequestAsync(IRequest request, ICallback callback)
        {
            if (request.Method == "OPTIONS")
            {
                return WriteOptionsOutput(callback);
            }
            var uri = new Uri(request.Url);
            Console.WriteLine($"-----------------\r\n{request.Url}");
            try
            {
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

                // This is a regular request
                Hl7.Fhir.Rest.FhirClient server = new Hl7.Fhir.Rest.FhirClient(_externalFhirServerBaseUrl);
                server.OnAfterResponse += (sender, args) =>
                {
                    base.Charset = args.RawResponse.CharacterSet;
                    foreach (string header in args.RawResponse.Headers.AllKeys)
                    {
                        if (!header.StartsWith("Access-Control"))
                            base.Headers.Add(header, args.RawResponse.Headers[header]);
                    }
                };
                server.PreferredFormat = Hl7.Fhir.Rest.ResourceFormat.Json;
                string redirectedUrl = server.Endpoint.OriginalString.TrimEnd('/') + uri.PathAndQuery;
                System.Diagnostics.Trace.WriteLine($"{redirectedUrl}");
                if (request.Method == "GET")
                {
                    if (uri.LocalPath == "/.well-known/smart-configuration")
                    {
                        base.StatusCode = 200;

                        FhirSmartAppLaunchConfiguration smart_config = new FhirSmartAppLaunchConfiguration();
                        // populate the context based on the data we know
                        smart_config.token_endpoint = $"https://{_identityServerBaseUrl}/token";
                        smart_config.authorization_endpoint = $"https://{_identityServerBaseUrl}/authorize";
                        smart_config.issuer = _app.Issuer;
                        smart_config.scopes_supported = _app.AllowedScopes;

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
                                if (!callback.IsDisposed)
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
                            extension.AddExtension("token", new FhirUri($"https://{_identityServerBaseUrl}/token"));
                            extension.AddExtension("authorize", new FhirUri($"https://{_identityServerBaseUrl}/authorize"));
                        }

                        base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(r.Result));
                        Console.WriteLine($"Success: {base.Stream.Length}");
                        base.MimeType = "application/fhir+json";
                        if (!callback.IsDisposed)
                            callback.Continue();
                        return r.Result;
                    });
                }
                if (request.Method == "POST")
                {
                    if (string.IsNullOrEmpty(bearer))
                    {
                        SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.Security, "No Bearer token provided for this request");
                        return CefReturnValue.Continue;
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
                        if (!callback.IsDisposed)
                            callback.Continue();
                        return;
                    });
                }
                if (request.Method == "PUT")
                {
                    if (string.IsNullOrEmpty(bearer))
                    {
                        SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.Security, "No Bearer token provided for this request");
                        return CefReturnValue.Continue;
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
                        if (!callback.IsDisposed)
                            callback.Continue();
                        return;
                    });
                }
                return CefReturnValue.ContinueAsync;
            }
            catch (Exception ex)
            {
                // Totally unknown request encountered
                SetErrorResponse(callback, ex);
                return CefReturnValue.Continue;
            }
        }
    }
}
