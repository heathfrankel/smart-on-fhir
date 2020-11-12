using Hl7.Fhir.SmartAppLaunch;

namespace EHRApp
{
    // This will normally come from a database, or app settings, or somewhere else,
    // and is assumed approved by the Practitioner's organization
    public class SmartAppSettings
    {
        public SmartApplicationDetails[] SmartApplications { get; set; }
    }
}
