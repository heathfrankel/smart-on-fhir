using System;
using System.Collections.Generic;
using System.Linq;
using CefSharp;
using Hl7.Fhir.Support;
using Newtonsoft.Json;

namespace Hl7.Fhir.SmartAppLaunch
{
    public class AuthProtocolSchemeHandlerFactory : ISchemeHandlerFactory
    {
        public static string AuthAddress(IFhirSmartAppContext context) => $"{context.LaunchContext}.identity.localhost";
        public static string FhirFacadeAddress(IFhirSmartAppContext context) => $"{context.LaunchContext}.fhir-facade.localhost";

        public AuthProtocolSchemeHandlerFactory(SmartApplicationDetails app, IFhirSmartAppContext context, Func<SmartApplicationDetails, IFhirSmartAppContext, string> getIdToken = null)
        {
            _app = app;
            _context = context;
            _getIdToken = getIdToken;
        }
        private SmartApplicationDetails _app;
        private IFhirSmartAppContext _context;
        private Func<SmartApplicationDetails, IFhirSmartAppContext, string> _getIdToken;

        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
        {
            return new AuthProtocolSchemeHandler(_app, _context, _getIdToken);
        }
    }

    public class AuthProtocolSchemeHandler : ResourceHandler
    {
        public AuthProtocolSchemeHandler(SmartApplicationDetails app, IFhirSmartAppContext context, Func<SmartApplicationDetails, IFhirSmartAppContext, string> getIdToken)
        {
            _app = app;
            _context = context;
            _getIdToken = getIdToken;
        }
        private SmartApplicationDetails _app;
        private IFhirSmartAppContext _context;
        private Func<SmartApplicationDetails, IFhirSmartAppContext, string> _getIdToken;

        // Process request and craft response.
        public override CefReturnValue ProcessRequestAsync(IRequest request, ICallback callback)
        {
            if (request.Method == "OPTIONS")
            {
                // This is the CORS request, and that's good
                base.StatusCode = 200;
                if (!string.IsNullOrEmpty(_app.AllowedHosts))
                    base.Headers.Add("Access-Control-Allow-Origin", _app.AllowedHosts);
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
                    string requestedScopes = keyValuePairs.FirstOrDefault(k => k.Key == "scope").Value;

                    // TODO: Validate the redirect URL, client ID, secret (and possibly the referrer value)
                    if (clientId != _app.ClientID || clientSecret != _app.ClientSecret)
                    {
                        base.StatusCode = (int)System.Net.HttpStatusCode.BadRequest;
                        // replace this with some Razor content
                        base.Stream = new System.IO.MemoryStream(System.Text.UTF8Encoding.UTF8.GetBytes("<HTML><body>Client ID or Secret bad</body></HTML>"));
                        base.Headers.Add("Cache-Control", "no-store");
                        base.Headers.Add("Pragma", "no-cache");
                        base.MimeType = "text/html;charset=UTF-8";

                        callback.Continue();
                        return CefReturnValue.Continue;
                    }

                    if (_app.redirect_uri?.Any() == true && !_app.redirect_uri.Contains(redirectUri))
                    {
                        base.StatusCode = (int)System.Net.HttpStatusCode.BadRequest;
                        // replace this with some Razor content
                        base.Stream = new System.IO.MemoryStream(System.Text.UTF8Encoding.UTF8.GetBytes("<HTML><body>Bad Redirect URL provided</body></HTML>"));
                        base.Headers.Add("Cache-Control", "no-store");
                        base.Headers.Add("Pragma", "no-cache");
                        base.MimeType = "text/html;charset=UTF-8";

                        callback.Continue();
                        return CefReturnValue.Continue;
                    }

                    // Check the scopes
                    _context.Scopes = requestedScopes;

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
                        string grant_type = keyValuePairs.FirstOrDefault(k => k.Key == "grant_type").Value;
                        if (code != _context.Code || grant_type != "authorization_code")
                        {
                            base.StatusCode = (int)System.Net.HttpStatusCode.Unauthorized;
                            TokenResponse responseTokenError = new TokenResponse()
                            {
                                error_description = "Invalid Code or unsupported grant_type requested"
                            };
                            string jsonInvalidCode = JsonConvert.SerializeObject(responseTokenError);
                            Console.WriteLine($"Token: {jsonInvalidCode}");
                            base.Stream = new System.IO.MemoryStream(System.Text.UTF8Encoding.UTF8.GetBytes(jsonInvalidCode));
                            base.Headers.Add("Cache-Control", "no-store");
                            base.Headers.Add("Pragma", "no-cache");
                            base.MimeType = "application/json;charset=UTF-8";

                            callback.Continue();
                            return CefReturnValue.Continue;
                        }

                        string redirect_uri = keyValuePairs.FirstOrDefault(k => k.Key == "redirect_uri").Value;
                        if (_app.redirect_uri?.Any() == true && !_app.redirect_uri.Contains(redirect_uri))
                        {
                            base.StatusCode = (int)System.Net.HttpStatusCode.Unauthorized;
                            TokenResponse responseTokenError = new TokenResponse()
                            {
                                error_description = "Invalid redirect_uri provided"
                            };
                            string jsonInvalidCode = JsonConvert.SerializeObject(responseTokenError);
                            Console.WriteLine($"Token: {jsonInvalidCode}");
                            base.Stream = new System.IO.MemoryStream(System.Text.UTF8Encoding.UTF8.GetBytes(jsonInvalidCode));
                            base.Headers.Add("Cache-Control", "no-store");
                            base.Headers.Add("Pragma", "no-cache");
                            base.MimeType = "application/json;charset=UTF-8";

                            callback.Continue();
                            return CefReturnValue.Continue;
                        }
                    }

                    // TODO: additional validation
                    // ...

                    // Grab the id_token if it's required
                    string id_token = null;
                    if ((_context.Scopes.Contains("fhirUser") || _context.Scopes.Contains("profile")) && _context.Scopes.Contains("openid") && _getIdToken != null)
                    {
                        // Need to also include the id_token
                        id_token = _getIdToken(_app, _context);
                    }

                    // All has been validated correctly, so we can return the token response
                    _context.ExpiresAt = DateTimeOffset.Now.AddSeconds(3600);
                    _context.Bearer = Guid.NewGuid().ToFhirId();
                    TokenResponse responseToken = new TokenResponse()
                    {
                        access_token = _context.Bearer,
                        id_token = id_token,
                        token_type = "Bearer",
                        expires_in = 3600,
                        scope = _context.Scopes,
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
