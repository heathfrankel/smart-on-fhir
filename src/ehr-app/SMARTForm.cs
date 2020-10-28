using CefSharp;
using CefSharp.WinForms;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;

namespace EHRApp
{
    public partial class SMARTForm : Form
    {
        private ChromiumWebBrowser _browser;
        private string _launchId;

        public SMARTForm()
        {
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            SimulatedFhirServer.LaunchContexts.Remove(_launchId);
            base.OnClosed(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            RequestContext rc = new RequestContext();
            rc.RegisterSchemeHandlerFactory("https", "sqlonfhir-r4.azurewebsites.net", new CustomProtocolSchemeHandlerFactory(_launchId));
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
        internal void LoadSmartApp(SmartApplication application, string fhirBaseUrl, string launchId, IPatientData patientData)
        {
            _launchId = launchId;
            SimulatedFhirServer.LaunchContexts.Add(launchId, patientData);
            _url = $"{application.Url}?iss={fhirBaseUrl}&launch={launchId}";
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
    }
}
