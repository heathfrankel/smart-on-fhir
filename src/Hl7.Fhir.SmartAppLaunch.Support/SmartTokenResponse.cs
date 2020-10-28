/* 
 * Copyright (c) 2017+ brianpos, Firely and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://github.com/ewoutkramer/fhir-net-api/blob/master/LICENSE
 */

using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.WebApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Hl7.Fhir.SmartAppLaunch
{
    /// <summary>
    /// Models a response from an OpenID Connect/OAuth 2 token endpoint
    /// </summary>
    public class TokenResponse
    {
        public string access_token { get; set; }
        public string token_type { get; set; }
        public int expires_in { get; set; }
        public string id_token { get; set; }
        public string scope { get; set; }
        public string refresh_token { get; set; }
        public string error_description { get; set; }

        // Clinical Context
        public string patient { get; set; }
        public string encounter { get; set; }
        public string episodeofcare { get; set; }

        // Practitioner Context
        public string practitioner { get; set; }
        public string practitionerrole { get; set; }
        public string organization { get; set; }
    }
}
