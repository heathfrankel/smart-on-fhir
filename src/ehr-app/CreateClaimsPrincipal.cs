using Hl7.Fhir.SmartAppLaunch;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace EHRApp
{
    public static class CreateClaimsPrincipal
    {
        public static ClaimsPrincipal ToPrincipal(this IFhirSmartAppContext context, SmartApplicationDetails appDetails, string jwt)
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwt);
            var claims = new[]
            {
                CreateClaimIfHasValue(Constants.ClaimTypes.IdentityProvider, "FHIR.SMARTAppLaunch"),
                CreateClaimIfHasValue(Constants.ClaimTypes.AuthenticationTime, DateTimeOffset.UtcNow.ToEpochTime().ToString()),
            }.Where(t => t != null).ToList();

            // put in all the claims that are in the identity token
            claims.AddRange(token.Claims);

            // Add in the context properties
            claims.AddRange(context.ContextProperties.Select(cp => new Claim(cp.Key, cp.Value)));

            // Add in the smart on FHIR scope (limited)
            if (context.Scopes != null)
            claims.AddRange(context.Scopes.Split(' ').Select(scope => new Claim(Constants.ClaimTypes.Scope, scope)));

            // return the ClaimsPrincipal object itself
            var identity = new ClaimsIdentity(claims, "FHIR.SMARTAppLaunch", "name", null);
            return new ClaimsPrincipal(identity);
        }

        private static System.Security.Claims.Claim CreateClaimIfHasValue(string claimType, string value)
        {
            return value != null ? new System.Security.Claims.Claim(claimType, value) : null;
        }
    }

    public class AustralianJwt
    {
        public string sub { get; set; }
        public string name { get; set; }
        public string profile { get; set; }
        public string iss { get; set; }
        public string aud { get; set; }
        public long exp { get; set; }
        public string jti { get; set; }
        public long iat { get; set; }
        public long nbf { get; set; }
        public string providerNo { get; set; }
        public string practitioner { get; set; }
        public string practitionerrole { get; set; }
        public string organization { get; set; }
    }

    internal static class DateTimeExtensions
    {
        // http://www.vortech.net/2013/07/converting-a-datetime-to-a-unix-time-value-in-c/
        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        public static int ToEpochTime(this DateTimeOffset me)
        {
            if (me == DateTimeOffset.MinValue)
            {
                return -1;
            }
            TimeSpan span = (me - UnixEpoch);
            return (int)Math.Floor(span.TotalSeconds);
        }
    }

}
