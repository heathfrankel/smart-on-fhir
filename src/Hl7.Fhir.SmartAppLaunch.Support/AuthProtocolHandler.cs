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
                    return ProcessAuthorizeRequest(callback, uri);
                }

                if (uri.LocalPath == "/token")
                {
                    return ProcessTokenRequest(request, callback);
                }

                // This is a regular request
                // TODO: Process any Auth pages if required (where there are no user interactions, we can ignore this)
                // 

                // --------------------------------------------------------------------
                // This was not handled as any currently supported Auth operation, so its a big no, nothing found.
                base.StatusCode = (int)System.Net.HttpStatusCode.NotFound;
                callback.Continue();
                return CefReturnValue.Continue;
            }
            catch (Exception ex)
            {
                base.StatusCode = (int)System.Net.HttpStatusCode.InternalServerError;
                callback.Cancel();
                return CefReturnValue.Cancel;
            }
        }

        private CefReturnValue ProcessAuthorizeRequest(ICallback callback, Uri uri)
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

            // Validate the Client ID and Client Secret
            if (clientId != _app.ClientID || clientSecret != _app.ClientSecret)
            {
                base.StatusCode = (int)System.Net.HttpStatusCode.BadRequest;
                // replace this with some Razor content
                base.Stream = new System.IO.MemoryStream(System.Text.UTF8Encoding.UTF8.GetBytes("<HTML><body>Client ID or Secret invalid</body></HTML>"));
                base.Headers.Add("Cache-Control", "no-store");
                base.Headers.Add("Pragma", "no-cache");
                base.MimeType = "text/html;charset=UTF-8";

                callback.Continue();
                return CefReturnValue.Continue;
            }

            // Validate the redirect URL against the app's configured redirect URIs
            if (_app.redirect_uri?.Any() == true && !_app.redirect_uri.Contains(redirectUri))
            {
                base.StatusCode = (int)System.Net.HttpStatusCode.BadRequest;
                // replace this with some Razor content
                base.Stream = new System.IO.MemoryStream(System.Text.UTF8Encoding.UTF8.GetBytes("<HTML><body>Invalid Redirect URL provided</body></HTML>"));
                base.Headers.Add("Cache-Control", "no-store");
                base.Headers.Add("Pragma", "no-cache");
                base.MimeType = "text/html;charset=UTF-8";

                callback.Continue();
                return CefReturnValue.Continue;
            }

            // Check the scopes are supported
            _context.Scopes = FilterScopes(requestedScopes, _app.AllowedScopes);

            // TODO: (and possibly the referrer value against the _apps.AllowedHosts)

            // This client (application) is authorized to connect to the system, and we have a logged in user in the system (as you've come in from our browser)
            _context.Code = Guid.NewGuid().ToFhirId();
            _context.ExpiresAt = DateTimeOffset.Now.AddMinutes(2); // This only lives for a very short time (not that it really matters in process where you can't get in anyway)
            base.StatusCode = (int)System.Net.HttpStatusCode.Redirect;
            base.Headers.Remove("Location");
            base.Headers.Add("Location", $"{redirectUri}?code={_context.Code}&state={state}");

            callback.Continue();
            return CefReturnValue.Continue;
        }

        private CefReturnValue ProcessTokenRequest(IRequest request, ICallback callback)
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
            responseToken.nash_pub_cert = _context.ContextProperties.FirstOrDefault(p => p.Key == "X-NASH-Public-Cert").Value;

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

        public string FilterScopes(string requestedScopes, string[] supportedScopes)
        {
            if (supportedScopes == null)
                return requestedScopes;
            List<string> resultScopes = new List<string>();
            foreach (var scope in requestedScopes.Split(' '))
            {
                foreach (var supportedScopeFormat in supportedScopes)
                {
                    if (MatchesScope(scope, supportedScopeFormat))
                    {
                        resultScopes.Add(scope);
                        // move to the next scope (templates are permissive)
                        break;
                    }
                }
            }
            return string.Join(" ", resultScopes);
        }

        /// <summary>
        /// Check the scope against the FHIR App Launch scope permission grammar
        /// and also convert any v1 scopes into v2 scopes if required
        /// (as well as explicit matches)
        /// http://build.fhir.org/ig/HL7/smart-app-launch/scopes-and-launch-context.html#clinical-scope-syntax
        /// </summary>
        /// <param name="scope"></param>
        /// <param name="supportedScopeFormat"></param>
        /// <returns></returns>
        /// <remarks>
        /// The old v1 format is defined here (till the CI build/v2 is published)
        /// http://hl7.org/fhir/smart-app-launch/scopes-and-launch-context/index.html#clinical-scope-syntax
        /// </remarks>
        public bool MatchesScope(string scope, string supportedScopeFormat)
        {
            // exact match of scope format
            if (scope == supportedScopeFormat)
                return true;

            // check for user wildcard formats
            if (supportedScopeFormat == "user/*.*" && (scope.StartsWith("user/") || scope.StartsWith("patient/")))
                return true;

            // check for patient wildcard formats
            if (supportedScopeFormat == "patient/*.*" && scope.StartsWith("patient/"))
                return true;

            return false;
        }
    }
}
