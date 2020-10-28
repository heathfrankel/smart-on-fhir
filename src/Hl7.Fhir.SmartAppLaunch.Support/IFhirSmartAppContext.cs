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
    /// The 
    /// </summary>
    public interface IFhirSmartAppContext
    {
        string LaunchContext { get; set; }
        string Code { get; set; }

        string Bearer { get; set; }
        string Scopes { get; set; }
        DateTimeOffset ExpiresAt { get; set; }

        string GetIdToken();
        IEnumerable<KeyValuePair<String, string>> ContextProperties { get; }
    }
}
