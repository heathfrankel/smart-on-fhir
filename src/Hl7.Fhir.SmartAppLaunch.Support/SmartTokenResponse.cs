/* 
 * Copyright (c) 2017+ brianpos, Firely and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://github.com/ewoutkramer/fhir-net-api/blob/master/LICENSE
 */
#pragma warning disable IDE1006 // Ignoring the Case naming recommendation as this matches to a json representation

namespace Hl7.Fhir.SmartAppLaunch
{
    /// <summary>
    /// Models a response from an OpenID Connect/OAuth 2 token endpoint
    /// It has the HL7 Australia extended context properties in it
    /// </summary>
    /// <remarks>
    /// Details of the <a href="http://build.fhir.org/ig/HL7/smart-app-launch/scopes-and-launch-context.html#launch-context-arrives-with-your-access_token">launch contexts</a> from HL7 International
    /// and also from HL7 Australia (<a href="https://confluence.hl7australia.com/display/PA/FHIR+SMART+App+Launch+Australian+Profile">in progress</a>)
    /// </remarks>
    public class TokenResponse
    {
        public string access_token { get; set; }
        public string token_type { get; set; }
        public int expires_in { get; set; }
        public string id_token { get; set; }
        public string scope { get; set; }
        public string refresh_token { get; set; }
        public string error_description { get; set; }

        #region << Clinical Context >>
        /// <summary>
        /// The Patient currently in focus (and all data may also be restricted to)
        /// </summary>
        /// <remarks>
        /// e.g. 43 (this should be accessible at the related fhir server /Patient/43)
        /// </remarks>
        public string patient { get; set; }
        /// <summary>
        /// The Encounter currently in focus
        /// </summary>
        public string encounter { get; set; }
        /// <summary>
        /// The EpisodeOfCare currently in focus
        /// </summary>
        public string episodeofcare { get; set; }
        #endregion

        #region << Practitioner/Clinical User Context >>
        // Note: These properties are being considered if they should move into the id_token, and access_token (or both)
        /// <summary>
        /// The Practitioner currently using the system - MUST be the same as in the id_token (when included)
        /// </summary>
        public string practitioner { get; set; }
        /// <summary>
        /// The PractitionerRole describing the organization/location of the practitioner currently using the system
        /// </summary>
        public string practitionerrole { get; set; }
        /// <summary>
        /// The Organization of the practitioner currently using the system (must be in the practitionerrole resource referenced)
        /// </summary>
        public string organization { get; set; }
        #endregion

        /// <summary>
        /// NASH Custom Property for Australia
        /// </summary>
        /// <remarks>
        /// Note: This will be replaced by the standard x5c property
        /// https://self-issued.info/docs/draft-ietf-jose-json-web-signature.html#x5cExample
        /// </remarks>
        [Newtonsoft.Json.JsonProperty("x_nash_public_cert")]
        public string nash_pub_cert { get; set; }
    }
}
