using Hl7.Fhir.SmartAppLaunch;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EHRApp
{
    public class SmartAppContext : IFhirSmartAppContext
    {
        public string LaunchContext { get; set; }
        public string Code { get; set; }
        public string CodeChallenge { get; set; }
        public string CodeChallengeMethod { get; set; }
        public string Bearer { get; set; }
        public string Scopes { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public System.Security.Principal.IPrincipal Principal { get; set; }

        public List<KeyValuePair<string, string>> ContextProperties { get; } = new List<KeyValuePair<string, string>>();
        IEnumerable<KeyValuePair<string, string>> IFhirSmartAppContext.ContextProperties => ContextProperties;

        public string GetIdToken(SmartApplicationDetails appDetails)
        {
            var token = SMARTForm.GenerateProviderJWTForNcsr(DateTime.Now, appDetails, this);
            Principal = this.ToPrincipal(appDetails, token);
            return token;
        }

        public string PatientNameForDebug { get; set; }
        public string PractitionerName { get; set; }
        public string PractitionerId
        {
            get
            {
                return ContextProperties.FirstOrDefault(cp => cp.Key == "practitioner").Value;
            }
        }
        public string MedicareProviderNumber { get; set; }
    }
}
