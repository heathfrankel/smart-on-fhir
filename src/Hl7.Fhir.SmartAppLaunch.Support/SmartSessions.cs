using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hl7.Fhir.SmartAppLaunch
{
    /// <summary>
    /// The SMART Session management class
    /// </summary>
    /// <remarks>
    /// The browser sessions are indexed by the browser's MainFrame Identifier
    /// (as there is only 1 request handler for the entire server, and the context of each window needs to be managed)
    /// </remarks>
    public class SmartSessions
    {
        Dictionary<long, SmartSession> SessionByFrameIdentifier = new Dictionary<long, SmartSession>();

        /// <summary>
        /// Retrieve the session data for a browser instance
        /// </summary>
        /// <param name="browserFrameIdentifier"></param>
        /// <returns></returns>
        public SmartSession GetSession(long browserFrameIdentifier)
        {
            return SessionByFrameIdentifier[browserFrameIdentifier];
        }

        /// <summary>
        /// Register a new browser session
        /// </summary>
        /// <param name="browserFrameIdentifier"></param>
        /// <param name="app"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public SmartSession RegisterSession(long browserFrameIdentifier, SmartApplicationDetails app, IFhirSmartAppContext context)
        {
            var session = new SmartSession(app, context);
            SessionByFrameIdentifier.Add(browserFrameIdentifier, session);
            return session;
        }

        /// <summary>
        /// Remove the session for a specific browser instance
        /// </summary>
        /// <param name="browserFrameIdentifier"></param>
        public void RemoveSession(long browserFrameIdentifier)
        {
            if (SessionByFrameIdentifier.ContainsKey(browserFrameIdentifier))
            {
                SessionByFrameIdentifier.Remove(browserFrameIdentifier);
            }
        }
    }

    public class SmartSession
    {
        public SmartSession(SmartApplicationDetails app, IFhirSmartAppContext context)
        {
            this.app = app;
            this.context = context;
        }
        public SmartApplicationDetails app { get; private set; }
        public IFhirSmartAppContext context { get; private set; }
    }
}
