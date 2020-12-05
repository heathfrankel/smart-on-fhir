using CefSharp;
using CefSharp.WinForms;
using Hl7.Fhir.SmartAppLaunch;
using Hl7.Fhir.Support;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows.Forms;

namespace EHRApp
{
    public partial class SMARTForm : Form
    {
        private ChromiumWebBrowser _browser;
        private SmartApplicationDetails _app;
        private IFhirSmartAppContext _context;


        public SMARTForm()
        {
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // dispose of the request context too
            _browser.RequestContext.RegisterSchemeHandlerFactory("https", AuthProtocolSchemeHandlerFactory.AuthAddress(_context), null);
            _browser.RequestContext.RegisterSchemeHandlerFactory("https", AuthProtocolSchemeHandlerFactory.FhirFacadeAddress(_context), null);
        }

        public static IConfigurationRoot Configuration()
        {
            return new ConfigurationBuilder()
                           .SetBasePath(Directory.GetCurrentDirectory())
                           .AddJsonFile("appsettings.json", optional: false).Build();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // This is the path in the users non roaming application folder
            // (You may consider if this should be removed after use, or even remove the cache path, which is essentially incognito mode)
            RequestContext rc = new RequestContext(new RequestContextSettings()
            {
                CachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"CefSharp\Cache")
            });

            // Register the handlers for this 
            rc.RegisterSchemeHandlerFactory("https", AuthProtocolSchemeHandlerFactory.AuthAddress(_context), new AuthProtocolSchemeHandlerFactory(_app, _context, GetIdToken));
            // rc.RegisterSchemeHandlerFactory("https", AuthProtocolSchemeHandlerFactory.FhirFacadeAddress(_context), new FhirFacadeProtocolSchemeHandlerFactory(_app, _context, () => { return new ComCare.FhirServer.Models.ComCareSystemService(Configuration()); }));
            rc.RegisterSchemeHandlerFactory("https", AuthProtocolSchemeHandlerFactory.FhirFacadeAddress(_context), new FhirProxyProtocolSchemeHandlerFactory(_app, _context, Globals.ApplicationSettings.FhirBaseUrl));

            _browser = new ChromiumWebBrowser("about:blank", rc)
            {
                Dock = DockStyle.Fill
            };
            panel2.Controls.Add(_browser);

            _browser.ConsoleMessage += OnBrowserConsoleMessage;
            _browser.StatusMessage += OnBrowserStatusMessage;
            _browser.TitleChanged += OnBrowserTitleChanged;
            _browser.AddressChanged += OnBrowserAddressChanged;
            _browser.LoadingStateChanged += OnBrowserLoadingStateChanged;
            _browser.IsBrowserInitializedChanged += _browser_IsBrowserInitializedChanged;
            _browser.LoadError += _browser_LoadError;

            string bitness = Environment.Is64BitProcess ? "x64" : "x86";
            string version = $"Chromium: {Cef.ChromiumVersion}, CEF: {Cef.CefVersion}, Environment: {Cef.CefSharpVersion}";
            GetMdiParent().DisplayOutput(version);
        }

        public string GetIdToken(SmartApplicationDetails app, IFhirSmartAppContext context)
        {
            return GenerateProviderJWTForNcsr(DateTime.Now);
        }

        public string GenerateProviderJWTForNcsr(DateTime creationTime)
        {
            var payload = new
            {
                sub = (_context as SmartAppContext).PractitionerId,
                name = (_context as SmartAppContext).PractitionerName,
                profile = (_context as SmartAppContext).PractitionerId,
                iss = _app.Issuer,
                aud = _app.Audience,
                exp = creationTime.AddHours(1).ToUnixTime(),
                jti = Guid.NewGuid().ToFhirId(),
                iat = creationTime.AddMinutes(-1).ToUnixTime(),
                nbf = creationTime.AddMinutes(-1).ToUnixTime(),
                providerNo = (_context as SmartAppContext).MedicareProviderNumber,
                isDelegate = "N",
                roles = new[] { "Practitioner", "PracticeManager" },
                pmsver = "Example EHR v1.34"
            };

            // Provide the NASH Digital Certificate (with Private Key)
            X509Certificate2 cert = GetNashCertificate();

            // Now generate the Identity Token
            string token = Jose.JWT.Encode(payload, cert.GetRSAPrivateKey(), Jose.JwsAlgorithm.RS256);
            return token;
        }

        public static X509Certificate2 GetNashCertificate()
        {
            // This could be replaced with reading from the Certificate Store, or some other mechanism
            // TODO: retrieve your NASH certificate
            return null;
        }

        public static string GetNashPublicKey()
        {
            var cert = GetNashCertificate();
            var certBytes = cert.Export(X509ContentType.Cert);
            var certPublic = new X509Certificate2(certBytes);
            var publicKey = Convert.ToBase64String(UTF8Encoding.UTF8.GetBytes("-----BEGIN CERTIFICATE-----\r\n" + Convert.ToBase64String(certPublic.GetRawCertData()) + "\r\n-----END CERTIFICATE-----"));
            return publicKey;
        }

        private void _browser_IsBrowserInitializedChanged(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_url) && _browser.Address != _url)
            {
                _browser.Load(_url);
            }
        }

        private void _browser_LoadError(object sender, LoadErrorEventArgs e)
        {
            System.Diagnostics.Trace.WriteLine($"{e.ErrorCode}: {e.ErrorText} Url: {e.FailedUrl}");
            GetMdiParent().DisplayOutput($"{e.ErrorCode}: {e.ErrorText} Url: {e.FailedUrl}");
        }

        private void OnBrowserLoadingStateChanged(object sender, LoadingStateChangedEventArgs e)
        {
            textBoxAddress.InvokeOnUiThreadIfRequired(() =>
            {
                if (textBoxAddress.Text != _browser.Address)
                    textBoxAddress.Text = _browser.Address;
                progressBar1.Visible = e.IsLoading;
            });

            GetMdiParent().DisplayOutput(e.IsLoading ? "Loading" : "Ready");
            if (_browser.Address == _url)
                _url = null;
            if (!string.IsNullOrEmpty(_url) && !e.IsLoading && _browser.Address != _url)
            {
                // _url = null;
                _browser.Load(_url);
            }
        }

        public MDIParent GetMdiParent()
        {
            return (MDIParent)MdiParent;
        }

        string _url;
        internal void LoadSmartApp(SmartApplicationDetails application, IFhirSmartAppContext context)
        {
            _app = application;
            _context = context;
            _url = $"{application.Url}?iss=https://{AuthProtocolSchemeHandlerFactory.FhirFacadeAddress(_context)}&launch={context.LaunchContext}";
        }

        private void OnBrowserAddressChanged(object sender, AddressChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Address)) return;

            GetMdiParent().DisplayOutput(e.Address);
        }

        private void OnBrowserTitleChanged(object sender, TitleChangedEventArgs e)
        {
            this.InvokeOnUiThreadIfRequired(() => Text = e.Title);
        }

        private void OnBrowserStatusMessage(object sender, StatusMessageEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Value)) return;

            GetMdiParent().DisplayOutput(e.Value);
        }

        private void OnBrowserConsoleMessage(object sender, ConsoleMessageEventArgs e)
        {
            this.InvokeOnUiThreadIfRequired(() =>
            {
                Console.WriteLine($"Line: {e.Line}, Source: {e.Source}, Message: {e.Message}");
            });
        }

        private void button1_Click(object sender, EventArgs e)
        {
            _browser.Reload();
        }

        private void developerToolsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _browser.ShowDevTools();
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _browser.Reload();
        }
    }

    public static class TokenExtensions
    {
        public static long ToUnixTime(this DateTime dateTime)
        {
            return (int)(dateTime.ToUniversalTime().Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
        }
    }
}
