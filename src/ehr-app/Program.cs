using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Web.Http.Dependencies;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using Hl7.Fhir.SmartAppLaunch;
using Microsoft.Extensions.Configuration;

namespace EHRApp
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            InitializeConfiguration();
            InitializeCef();

            StartupWebServer();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MDIParent());
            _fhirServerController.Dispose();
        }

        static void StartupWebServer()
        {
            // Ensure that we grab an available IP port on the local workstation
            // http://stackoverflow.com/questions/9895129/how-do-i-find-an-available-port-before-bind-the-socket-with-the-endpoint
            string port = "9000";

            using (Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                sock.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 0)); // Pass 0 here, it means to go looking for a free port
                port = ((IPEndPoint)sock.LocalEndPoint).Port.ToString();
                sock.Close();
            }

            // Now use that randomly located port to start up a local FHIR server
            _baseAddress = "http://localhost:" + port + "/";
            _fhirServerController = Microsoft.Owin.Hosting.WebApp.Start<Startup>(_baseAddress);
        }

        static private IDisposable _fhirServerController;
        public static string _baseAddress;

        static void InitializeConfiguration(string profile = null)
        {
            IConfigurationBuilder builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("AppSettings.json");
            if (profile != null)
                builder.AddJsonFile($"{profile}.json");

            IConfigurationRoot configurationRoot = builder.Build();

            Globals.ApplicationSettings = new ApplicationSettings();
            configurationRoot.GetSection("ApplicationSettings").Bind(Globals.ApplicationSettings);

            Globals.SmartAppSettings = new SmartAppSettings();
            configurationRoot.GetSection("SmartAppSettings").Bind(Globals.SmartAppSettings);
        }

        static void InitializeCef()
        {
            Cef.EnableHighDPISupport();
            var settings = new CefSettings
            {
                // This is the path in the users non roaming application folder
                // (You may consider if this should be removed after use, or even remove the cache path, which is essentially incognito mode)
                // optionally you can do this in the specific form/browser instance too
                CachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"CefSharp\Cache"),
                RemoteDebuggingPort = 8080
            };

            Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);

            string identityServerAddress = "identity.test.localhost";

            Cef.GetGlobalRequestContext().RegisterSchemeHandlerFactory("https", identityServerAddress, new AuthProtocolSchemeHandlerFactory(ActiveSession));
            Cef.GetGlobalRequestContext().RegisterSchemeHandlerFactory("https", fhirServerAddress, new FhirFacadeProtocolSchemeHandlerFactory<IDependencyScope>(ActiveSession, fhirServerAddress, identityServerAddress, () =>
            {
                // return new ComCare.FhirServer.Models.ComCareSystemService(Configuration());
                return Startup.systemService;
            }, true));
            // Cef.GetGlobalRequestContext().RegisterSchemeHandlerFactory("https", fhirServerAddress, new FhirProxyProtocolSchemeHandlerFactory(ActiveSession, fhirServerAddress, identityServerAddress, Globals.ApplicationSettings.FhirBaseUrl));
        }

        public static IConfigurationRoot Configuration()
        {
            return new ConfigurationBuilder()
                           .SetBasePath(Directory.GetCurrentDirectory())
                           .AddJsonFile("appsettings.json", optional: false).Build();
        }

        public static string fhirServerAddress = "fhir.test.localhost";
        public static SmartSessions ActiveSession = new SmartSessions();
    }
}
