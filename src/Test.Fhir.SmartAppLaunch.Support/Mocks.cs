using CefSharp;
using Hl7.Fhir.SmartAppLaunch;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace Test.Fhir.SmartAppLaunch.Support
{
    internal class MockBrowser : CefSharp.IBrowser
    {
        public MockBrowser(IFrame frame)
        {
            _frame = frame;
        }
        IFrame _frame;
        public bool CanGoBack => throw new NotImplementedException();

        public bool CanGoForward => throw new NotImplementedException();

        public bool IsLoading => throw new NotImplementedException();

        public int Identifier => throw new NotImplementedException();

        public bool IsPopup => throw new NotImplementedException();

        public bool HasDocument => throw new NotImplementedException();

        public IFrame MainFrame => _frame;

        public IFrame FocusedFrame => throw new NotImplementedException();

        public bool IsDisposed => throw new NotImplementedException();

        public void CloseBrowser(bool forceClose)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public IFrame GetFrame(long identifier)
        {
            throw new NotImplementedException();
        }

        public IFrame GetFrame(string name)
        {
            throw new NotImplementedException();
        }

        public int GetFrameCount()
        {
            throw new NotImplementedException();
        }

        public List<long> GetFrameIdentifiers()
        {
            throw new NotImplementedException();
        }

        public List<string> GetFrameNames()
        {
            throw new NotImplementedException();
        }

        public IBrowserHost GetHost()
        {
            throw new NotImplementedException();
        }

        public void GoBack()
        {
            throw new NotImplementedException();
        }

        public void GoForward()
        {
            throw new NotImplementedException();
        }

        public bool IsSame(IBrowser that)
        {
            throw new NotImplementedException();
        }

        public void Reload(bool ignoreCache = false)
        {
            throw new NotImplementedException();
        }

        public void StopLoad()
        {
            throw new NotImplementedException();
        }
    }

    internal class MockFrame : CefSharp.IFrame
    {
        public MockFrame(long identifier)
        {
            _identifier = identifier;
        }
        long _identifier;
        public bool IsValid => throw new NotImplementedException();

        public bool IsMain => throw new NotImplementedException();

        public bool IsFocused => throw new NotImplementedException();

        public string Name => throw new NotImplementedException();

        public long Identifier => _identifier;

        public IFrame Parent => throw new NotImplementedException();

        public string Url => throw new NotImplementedException();

        public IBrowser Browser => throw new NotImplementedException();

        public bool IsDisposed => throw new NotImplementedException();

        public void Copy()
        {
            throw new NotImplementedException();
        }

        public IRequest CreateRequest(bool initializePostData = true)
        {
            throw new NotImplementedException();
        }

        public IUrlRequest CreateUrlRequest(IRequest request, IUrlRequestClient client)
        {
            throw new NotImplementedException();
        }

        public void Cut()
        {
            throw new NotImplementedException();
        }

        public void Delete()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public Task<JavascriptResponse> EvaluateScriptAsync(string script, string scriptUrl = "about:blank", int startLine = 1, TimeSpan? timeout = null, bool useImmediatelyInvokedFuncExpression = false)
        {
            throw new NotImplementedException();
        }

        public void ExecuteJavaScriptAsync(string code, string scriptUrl = "about:blank", int startLine = 1)
        {
            throw new NotImplementedException();
        }

        public void GetSource(IStringVisitor visitor)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetSourceAsync()
        {
            throw new NotImplementedException();
        }

        public void GetText(IStringVisitor visitor)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetTextAsync()
        {
            throw new NotImplementedException();
        }

        public void LoadRequest(IRequest request)
        {
            throw new NotImplementedException();
        }

        public void LoadUrl(string url)
        {
            throw new NotImplementedException();
        }

        public void Paste()
        {
            throw new NotImplementedException();
        }

        public void Redo()
        {
            throw new NotImplementedException();
        }

        public void SelectAll()
        {
            throw new NotImplementedException();
        }

        public void Undo()
        {
            throw new NotImplementedException();
        }

        public void ViewSource()
        {
            throw new NotImplementedException();
        }
    }

    internal class MockCallback : CefSharp.ICallback, CefSharp.Callback.IResourceReadCallback
    {
        public bool IsDisposed { get; private set; }

        public bool IsCancelled { get; private set; }
        public bool IsContinue { get; private set; }

        public void Cancel()
        {
            IsCancelled = true;
        }

        public void Continue()
        {
            IsContinue = true;
        }

        public int _bytesRead;
        public void Continue(int bytesRead)
        {
            _bytesRead = bytesRead;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    public class MockFhirSmartAppContext : IFhirSmartAppContext
    {
        public string LaunchContext { get; set; }
        public string Code { get; set; }
        public string Bearer { get; set; }
        public string Scopes { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }

        public IEnumerable<KeyValuePair<string, string>> ContextProperties { get; set; } = new List<KeyValuePair<string, string>>();

        public IPrincipal Principal { get; set; }
        public string MedicareProviderNumber { get; internal set; }
        public string PractitionerName { get; internal set; }
        public string PractitionerId { get; internal set; }

        public string GetIdToken(SmartApplicationDetails appDetails)
        {
            throw new NotImplementedException();
        }
    }

    public class MockResponse : CefSharp.IResponse
    {
        public string Charset { get; set; }
        public string MimeType { get; set; }
        public NameValueCollection Headers { get; set; } = new NameValueCollection();

        public bool IsReadOnly { get; set; }

        public CefErrorCode ErrorCode { get; set; }
        public int StatusCode { get; set; }
        public string StatusText { get; set; }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public string GetHeaderByName(string name)
        {
            return Headers[name];
        }

        public void SetHeaderByName(string name, string value, bool overwrite)
        {
            Headers[name] = value;
        }
    }
}
