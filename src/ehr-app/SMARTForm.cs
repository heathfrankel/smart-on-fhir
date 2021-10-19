using CefSharp;
using CefSharp.WinForms;
using Hl7.Fhir.SmartAppLaunch;
using Hl7.Fhir.Support;
using Microsoft.Extensions.Configuration;
using System;
using System.IdentityModel.Tokens.Jwt;
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
            Program.ActiveSession.RemoveSession(_browser.GetMainFrame().Identifier);
            base.OnClosed(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            _browser = new ChromiumWebBrowser("about:blank", null)
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
            // _browser.DownloadHandler = new IDownloadHandler

            string bitness = Environment.Is64BitProcess ? "x64" : "x86";
            string version = $"Chromium: {Cef.ChromiumVersion}, CEF: {Cef.CefVersion}, Environment: {Cef.CefSharpVersion}";
            GetMdiParent().DisplayOutput(version);
        }

        public string GetIdToken(SmartApplicationDetails app, IFhirSmartAppContext context)
        {
            return GenerateProviderJWTForNcsr(DateTime.Now, app, context);
        }

        public static string GenerateProviderJWTForNcsr(DateTime creationTime, SmartApplicationDetails app, IFhirSmartAppContext context)
        {
            var payload = new
            {
                sub = (context as SmartAppContext).PractitionerId,
                name = (context as SmartAppContext).PractitionerName,
                profile = "https://" + Program.fhirServerAddress + "/Practitioner/" + (context as SmartAppContext).PractitionerId,
                iss = app.Issuer,
                aud = app.Audience,
                exp = creationTime.AddHours(1).ToUnixTime(),
                jti = Guid.NewGuid().ToFhirId(),
                iat = creationTime.AddMinutes(-1).ToUnixTime(),
                nbf = creationTime.AddMinutes(-1).ToUnixTime(),
                providerNo = (context as SmartAppContext).MedicareProviderNumber,
                isDelegate = "N",
                roles = new[] { "Practitioner", "PracticeManager" },
                pmsver = "Example EHR v1.34",
                practitioner = context.ContextProperties.FirstOrDefault(cp => cp.Key == "practitioner").Value,
                practitionerrole = context.ContextProperties.FirstOrDefault(cp => cp.Key == "practitionerrole").Value,
                organization = context.ContextProperties.FirstOrDefault(cp => cp.Key == "organization").Value
            };

            // Provide the NASH Digital Certificate (with Private Key)
            X509Certificate2 cert = GetNashCertificate();

            var extraHeaders = new System.Collections.Generic.Dictionary<string, object>();
            extraHeaders.Add("typ", "JWT");
            extraHeaders.Add(JwtHeaderParameterNames.X5c, GetNashPublicKeyChain(cert));

            // Now generate the Identity Token
            string token = Jose.JWT.Encode(payload, cert.GetRSAPrivateKey(), Jose.JwsAlgorithm.RS256, extraHeaders);
            return token;
        }

        public static X509Certificate2 GetNashCertificate()
        {
            // This could be replaced with reading from the Certificate Store, or some other mechanism
            // TODO: retrieve your NASH certificate
            return null;
        }

        public static string[] GetNashPublicKeyChain(X509Certificate2 cert)
        {
            var certBytes = cert.Export(X509ContentType.Cert);
            var certPublic = new X509Certificate2(certBytes);
            return new[] { Convert.ToBase64String(certPublic.GetRawCertData()) };

            //X509Chain chain = new X509Chain();
            //chain.Build(certPublic);
            //var keys = new System.Collections.Generic.List<string>();
            //foreach (var element in chain.ChainElements)
            //{
            //    // encoded as per https://datatracker.ietf.org/doc/html/rfc7515#appendix-C
            //    keys.Add(Convert.ToBase64String(element.Certificate.GetRawCertData()));// .TrimEnd('=').Replace('+', '-').Replace('/', '_'));
            //}
            //return keys.ToArray();
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
            // register our session
            Program.ActiveSession.RegisterSession(_browser.GetMainFrame().Identifier, _app, _context);

            // Sneaky cheat control code to automatically popup the dev tools
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                _browser.ShowDevTools();
            }

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
            _url = $"{application.Url}?iss=https://{Program.fhirServerAddress}&launch={context.LaunchContext}";
        }

        private void OnBrowserAddressChanged(object sender, AddressChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Address)) return;

            GetMdiParent().DisplayOutput(e.Address);
        }

        private void OnBrowserTitleChanged(object sender, TitleChangedEventArgs e)
        {
            this.InvokeOnUiThreadIfRequired(() => Text = (_context as SmartAppContext).PatientNameForDebug + ": " + e.Title);
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
