using System;
using System.IO;
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

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MDIParent());
        }

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
            // Cef.GetGlobalRequestContext().RegisterSchemeHandlerFactory("https", fhirServerAddress, new FhirFacadeProtocolSchemeHandlerFactory(_app, _context, () => { return new ComCare.FhirServer.Models.ComCareSystemService(Configuration()); }));
            Cef.GetGlobalRequestContext().RegisterSchemeHandlerFactory("https", fhirServerAddress, new FhirProxyProtocolSchemeHandlerFactory(ActiveSession, fhirServerAddress, identityServerAddress, Globals.ApplicationSettings.FhirBaseUrl));
        }
        public static string fhirServerAddress = "fhir.test.localhost";
        public static SmartSessions ActiveSession = new SmartSessions();
    }
}
