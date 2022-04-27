using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EHRApp
{
    public class ApplicationSettings
    {
        /// <summary>
        /// This specific configuration will run the FileSystemServer, not the ProxyServer
        /// </summary>
        public string LocalFileSystemFolder { get; set; }

        /// <summary>
        /// If the LocalFileSystemFolder is no specified, use this location
        /// </summary>
        public string FhirBaseUrl { get; set; }

        public string organization { get; set; }
        public string practitioner { get; set; }
        public string practitionerrole { get; set; }
        public string OrganizationsDigitalCertificateThumbprint { get; set; }
    }
}
