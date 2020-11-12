using CefSharp;
using CefSharp.WinForms;
using Hl7.Fhir.SmartAppLaunch;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace EHRApp
{
    public partial class SMARTForm : Form
    {
        private ChromiumWebBrowser _browser;
        private SmartApplicationDetails _app;
        private IFhirSmartAppContext _context;

        const string _fhirBaseUrl = "legacy-app-fhir-facade.localhost";
        const string _fhirAuthUrl = "identity.localhost";

        public SMARTForm()
        {
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // dispose of the request context too
            _browser.RequestContext.RegisterSchemeHandlerFactory("https", $"{_context.LaunchContext}.{_fhirAuthUrl}", null);
            _browser.RequestContext.RegisterSchemeHandlerFactory("https", $"{_context.LaunchContext}.{_fhirBaseUrl}", null);
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
            RequestContext rc = new RequestContext(new RequestContextSettings() {
                CachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"CefSharp\Cache")
            });

            // Register the handlers for this 
            rc.RegisterSchemeHandlerFactory("https", $"{_context.LaunchContext}.{_fhirAuthUrl}", new AuthProtocolSchemeHandlerFactory(_app, _context));
            rc.RegisterSchemeHandlerFactory("https", $"{_context.LaunchContext}.{_fhirBaseUrl}", new FhirFacadeProtocolSchemeHandlerFactory(_app, _context, () => { return new ComCare.FhirServer.Models.ComCareSystemService(Configuration()); }));
            // rc.RegisterSchemeHandlerFactory("https", _context.LaunchContext + "." + _fhirBaseUrl, new CustomProtocolSchemeHandlerFactory(_app, _context));

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
            _url = $"{application.Url}?iss=https://{_context.LaunchContext}.{_fhirBaseUrl}&launch={context.LaunchContext}";
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
}
