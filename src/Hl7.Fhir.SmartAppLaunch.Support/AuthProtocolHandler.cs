using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CefSharp;
using Hl7.Fhir.Model;
using Hl7.Fhir.Support;
using Newtonsoft.Json;

namespace Hl7.Fhir.SmartAppLaunch
{
    public class AuthProtocolSchemeHandlerFactory : ISchemeHandlerFactory
    {
        public AuthProtocolSchemeHandlerFactory(IFhirSmartAppContext context)
        {
            _context = context;
        }
        IFhirSmartAppContext _context;
        public const string SchemeName = "LocalFhirSmartAuthProtocol";

        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
        {
            return new AuthProtocolSchemeHandler(_context);
        }
    }

    public class AuthProtocolSchemeHandler : ResourceHandler
    {
        public AuthProtocolSchemeHandler(IFhirSmartAppContext context)
        {
            _context = context;
        }
        IFhirSmartAppContext _context;

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
                if (uri.LocalPath == "/authorize")
                {
                    // validate the client request data
                    var keyValuePairs = uri.Query.Split('&').Select(p => { var pair = p.Split('='); return new KeyValuePair<string, string>(pair[0], Uri.UnescapeDataString(pair[1])); }).ToList();
                    foreach (var item in keyValuePairs)
                    {
                        Console.WriteLine($"Authorize: {item.Key} = {item.Value}");
                    }
                    string redirectUri = keyValuePairs.FirstOrDefault(k => k.Key == "redirect_uri").Value;
                    string state = keyValuePairs.FirstOrDefault(k => k.Key == "state").Value;
                    string clientId = keyValuePairs.FirstOrDefault(k => k.Key == "client_id").Value;
                    string clientSecret = keyValuePairs.FirstOrDefault(k => k.Key == "client_secret").Value;

                    // TODO: Validate the redirect URL, client ID, secret (and possibly the referrer value)

                    // This client (application) is authorized to connect to the system, and we have a logged in user in the system
                    _context.Code = Guid.NewGuid().ToFhirId();
                    _context.ExpiresAt = DateTimeOffset.Now.AddMinutes(2); // This only lives for a very short time (not that it really matters in process where you can't get in anyway)
                    base.StatusCode = (int)System.Net.HttpStatusCode.Redirect;
                    base.Headers.Remove("Location");
                    base.Headers.Add("Location", $"{redirectUri}?code={_context.Code}&state={state}");

                    callback.Continue();
                    return CefReturnValue.Continue;
                }

                if (uri.LocalPath == "/token")
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

                        string code = keyValuePairs.FirstOrDefault(k => k.Key == "code").Value;

                    }

                    // TODO: additional validation

                    // All has been validated correctly, so we can return the token response
                    _context.ExpiresAt = DateTimeOffset.Now.AddSeconds(3600);
                    _context.Bearer = Guid.NewGuid().ToFhirId();
                    TokenResponse responseToken = new TokenResponse()
                    {
                        access_token = _context.Bearer,
                        token_type = "Bearer",
                        expires_in = 3600,
                        scope = "patient/Observation.read patient/Patient.read",
                    };
                    responseToken.patient = _context.ContextProperties.FirstOrDefault(p => p.Key == "patient").Value;
                    responseToken.encounter = _context.ContextProperties.FirstOrDefault(p => p.Key == "encounter").Value;
                    responseToken.episodeofcare = _context.ContextProperties.FirstOrDefault(p => p.Key == "episodeofcare").Value;

                    responseToken.organization = _context.ContextProperties.FirstOrDefault(p => p.Key == "organization").Value;
                    responseToken.practitioner = _context.ContextProperties.FirstOrDefault(p => p.Key == "practitioner").Value;
                    responseToken.practitionerrole = _context.ContextProperties.FirstOrDefault(p => p.Key == "practitionerrole").Value;

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
                // TODO: Process any Auth pages

                // Otherwise its a big no.
                callback.Cancel();
                return CefReturnValue.Cancel;
            }
            catch (Exception ex)
            {
                callback.Dispose();
                return CefReturnValue.Cancel;
            }
        }
    }
}
