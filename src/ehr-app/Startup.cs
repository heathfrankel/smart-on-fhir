using Hl7.Fhir.DemoFileSystemFhirServer;
using Hl7.Fhir.WebApi;
using Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Dependencies;

namespace EHRApp
{
    public class Startup
    {
        // This test stuff was based off the code found here:
        // http://www.asp.net/web-api/overview/hosting-aspnet-web-api/use-owin-to-self-host-web-api
        // This code configures Web API. The Startup class is specified as a type
        // parameter in the WebApp.Start method.
        public void Configuration(IAppBuilder appBuilder)
        {
            // Configure Web API for self-host.
            HttpConfiguration config = new HttpConfiguration();

            systemService = new DirectorySystemService<IDependencyScope>();
            DirectorySystemService<IDependencyScope>.Directory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)+"\\.fhir\\ehrapp-testdata";
            if (!System.IO.Directory.Exists(DirectorySystemService<IDependencyScope>.Directory))
                System.IO.Directory.CreateDirectory(DirectorySystemService<IDependencyScope>.Directory);
            (systemService as DirectorySystemService<IDependencyScope>).InitializeIndexes();
            // systemService = new ComCare.FhirServer.Models.ComCareSystemService(Program.Configuration()); // this is from the actual WebAPI Project

            WebApiConfig.Register(config, systemService);

            config.Formatters.Add(new SimpleHtmlFhirOutputFormatter());
            appBuilder.UseWebApi(config);
        }

        public static IFhirSystemServiceR4<IDependencyScope> systemService;
    }
}
