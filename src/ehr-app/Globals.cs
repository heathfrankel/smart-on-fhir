using Hl7.Fhir.SmartAppLaunch;

namespace EHRApp
{
    public class Globals
    {
        public static ApplicationSettings ApplicationSettings { get; internal set; }
        public static SmartAppSettings SmartAppSettings { get; internal set; }
        
        public static SmartApplicationDetails GetSmartApplicationSettings(string key)
        {
            foreach(SmartApplicationDetails settings in SmartAppSettings.SmartApplications)
            {
                if(settings.Key == key)
                {
                    return settings;
                }
            }

            return null;
        }
    }
}
