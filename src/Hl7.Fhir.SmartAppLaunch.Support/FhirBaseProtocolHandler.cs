using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using CefSharp;
using Hl7.Fhir.Model;
using Hl7.Fhir.WebApi;

namespace Hl7.Fhir.SmartAppLaunch
{
    /// <summary>
    /// This is a Demonstration Facade Handler that pretends that the server at the provided address is a local FHIR Facade running in-process
    /// It does not do any authentication with the remote service.
    /// </summary>
    public abstract class FhirBaseProtocolSchemeHandlerFactory
    {
        public FhirBaseProtocolSchemeHandlerFactory(SmartSessions sessionManager, string fhirServerBaseUrl, string identityServerBaseUrl)
        {
            _fhirServerBaseUrl = fhirServerBaseUrl;
            _identityServerBaseUrl = identityServerBaseUrl;
            _sessionManager = sessionManager;
        }
        protected string _fhirServerBaseUrl;
        protected string _identityServerBaseUrl;
        protected SmartSessions _sessionManager;
    }

    public abstract class FhirBaseProtocolSchemeHandler : ResourceHandler
    {
        public FhirBaseProtocolSchemeHandler(SmartApplicationDetails app, IFhirSmartAppContext launchContext, string fhirServerBaseUrl, string identityServerBaseUrl)
        {
            _app = app;
            _launchContext = launchContext;
            _fhirServerBaseUrl = fhirServerBaseUrl;
            _identityServerBaseUrl = identityServerBaseUrl;
        }
        protected SmartApplicationDetails _app;
        protected IFhirSmartAppContext _launchContext;
        protected string _fhirServerBaseUrl;
        protected string _identityServerBaseUrl;

        protected CefReturnValue WriteOptionsOutput(ICallback callback)
        {
            // This is the CORS request, and that's good
            base.StatusCode = 200;
            // base.Headers.Add("Access-Control-Allow-Origin", "*");
            base.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,OPTIONS");
            base.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept, authorization");
            callback.Continue();
            return CefReturnValue.Continue;
        }

        protected void SetErrorResponse(ICallback callback, Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error: {ex.Message}");
            base.Headers.Add("Cache-Control", "no-store");
            base.Headers.Add("Pragma", "no-cache");

            OperationOutcome result = new OperationOutcome();
            HttpStatusCode? status = null;
            // process the exception (and all it's inner exceptions
            while (ex != null)
            {
                if (ex is Hl7.Fhir.Rest.FhirOperationException fe)
                {
                    if (!status.HasValue)
                        status = fe.Status;
                    if (fe.Outcome != null)
                    {
                        result.Issue.AddRange(fe.Outcome.Issue);
                    }
                }
                else
                {
                    result.Issue.Add(new OperationOutcome.IssueComponent()
                    {
                        Severity = OperationOutcome.IssueSeverity.Fatal,
                        Code = OperationOutcome.IssueType.Exception,
                        Details = new CodeableConcept() { Text = ex.Message }
                    });
                }
                ex = ex.InnerException;
            }

            base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(result));
            base.MimeType = "application/fhir+json";
            if (status.HasValue)
                base.StatusCode = (int)status.Value;
            else
                base.StatusCode = (int)HttpStatusCode.InternalServerError;

            if (!callback.IsDisposed)
                callback.Continue();
        }

        /// <summary>
        /// Send a custom error message back
        /// </summary>
        /// <param name="callback"></param>
        /// <param name="status"></param>
        /// <param name="severity"></param>
        /// <param name="issueType"></param>
        /// <param name="message"></param>
        /// <param name="coding"></param>
        protected void SetErrorResponse(ICallback callback, HttpStatusCode status, OperationOutcome.IssueSeverity severity, OperationOutcome.IssueType issueType, string message, Coding coding = null)
        {
            System.Diagnostics.Trace.WriteLine($"Error: {message}");
            OperationOutcome result = new OperationOutcome();
            var issue = new OperationOutcome.IssueComponent()
            {
                Severity = severity,
                Code = issueType,
                Details = new CodeableConcept() { Text = message }
            };
            if (coding != null)
                issue.Details.Coding.Add(coding);
            result.Issue.Add(issue);

            SetResponse(callback, status, result);
        }

        /// <summary>
        /// Set this FHIR Resource as the response to the request
        /// </summary>
        /// <param name="callback"></param>
        /// <param name="status"></param>
        /// <param name="resource"></param>
        protected void SetResponse(ICallback callback, HttpStatusCode status, Resource resource)
        {
            base.Headers.Add("Cache-Control", "no-store");
            base.Headers.Add("Pragma", "no-cache");
            base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(resource));
            base.MimeType = "application/fhir+json";
            base.StatusCode = (int)status;

            if (!callback.IsDisposed)
                callback.Continue();
        }
    }
}
