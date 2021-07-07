using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hl7.Fhir.SmartAppLaunch
{
    public class SmartApplicationDetails
    {
        /// <summary>
        /// Internal Key used to uniquely identify this App Launch context
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The Name that is shown for this Smart App
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Launch URL for the Smart Application
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// List of redirect URIs that are supported for this SMART application
        /// </summary>
        public string[] redirect_uri { get; set; }

        /// <summary>
        /// The OAuth ClientID for this SMART application
        /// </summary>
        public string ClientID { get; set; }

        /// <summary>
        /// The Secret for the Smart Application
        /// Empty implies no secret as the client can't keep it
        /// (This should be in a database or somewhere more secure than an appsetting.json file unless secured appropriately)
        /// </summary>
        public string ClientSecret { get; set; }

        /// <summary>
        /// Which Scopes (space separated) are permitted by the data server to this application
        /// No values implies returning whatever they asked for
        /// </summary>
        public string[] AllowedScopes { get; set; }

        /// <summary>
        /// This is the equivalent of the AllowedHosts for CORS processing for the internal FHIR Facade
        /// https://docs.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-3.1#host-filtering
        /// This will serve to prevent hosts outside the domain of the smart app that somehow inject themselves into the site from having access to the data server
        /// </summary>
        public string AllowedHosts { get; set; }

        /// <summary>
        /// When creating the id_token, the Audience that the client is expected to provide (for this smart application)
        /// </summary>
        public string Audience { get; set; }

        /// <summary>
        /// When creating the id_token, the Issuer that is configured for the smart App
        /// </summary>
        public string Issuer { get; set; }
    }
}
