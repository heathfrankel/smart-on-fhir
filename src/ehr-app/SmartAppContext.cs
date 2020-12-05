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
        public string Bearer { get; set; }
        public string Scopes { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }

        public List<KeyValuePair<string, string>> ContextProperties { get; } = new List<KeyValuePair<string, string>>();
        IEnumerable<KeyValuePair<string, string>> IFhirSmartAppContext.ContextProperties => ContextProperties;

        public string GetIdToken()
        {
            return null;
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
