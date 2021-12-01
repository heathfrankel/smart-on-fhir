using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using CefSharp;
using CefSharp.DevTools.Network;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Utility;
using Hl7.Fhir.WebApi;

namespace Hl7.Fhir.SmartAppLaunch
{
    public class FhirFacadeProtocolSchemeHandlerFactory<TSP> : FhirBaseProtocolSchemeHandlerFactory, ISchemeHandlerFactory
        where TSP : class
    {
        public FhirFacadeProtocolSchemeHandlerFactory(SmartSessions sessionManager, string fhirServerBaseUrl, string identityServerBaseUrl, Func<IFhirSystemServiceR4<TSP>> facadeFactory, bool applySmartScopes = false)
            : base(sessionManager, fhirServerBaseUrl, identityServerBaseUrl)
        {
            _facadeFactory = facadeFactory;
            _applySmartScopes = applySmartScopes;
        }
        private Func<IFhirSystemServiceR4<TSP>> _facadeFactory;
        private bool _applySmartScopes;

        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
        {
            var session = _sessionManager.GetSession(browser.MainFrame.Identifier);
            System.Diagnostics.Trace.WriteLine($"{request.Method}: {request.Url}");
            System.Diagnostics.Trace.WriteLine($"  Content-type: {request.Headers["Content-Type"]}");
            string HeaderAuth = request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(HeaderAuth))
            {
                System.Diagnostics.Trace.WriteLine($"  Authorization: {HeaderAuth}");
                System.Diagnostics.Trace.WriteLine($"{session.app.Name}: {session.context.Bearer}");
                foreach (var p in session.context.ContextProperties)
                {
                    System.Diagnostics.Trace.WriteLine($"  {p.Key}: {p.Value}");
                }
            }
            if (session == null)
                return null;
            return new FhirFacadeProtocolSchemeHandler<TSP>(session.app, session.context, _fhirServerBaseUrl, _identityServerBaseUrl, _facadeFactory(), _applySmartScopes);
        }
    }

    public class FhirFacadeProtocolSchemeHandler<TSP> : FhirBaseProtocolSchemeHandler
        where TSP : class
    {
        readonly string[] SearchQueryParameterNames = { "_summary", "_sort", "_count", "_format" };
        readonly string[] OperationQueryParameterNames = { "_summary", "_format" };


        public FhirFacadeProtocolSchemeHandler(SmartApplicationDetails app, IFhirSmartAppContext launchContext, string fhirServerBaseUrl, string identityServerBaseUrl, IFhirSystemServiceR4<TSP> facade, bool applySmartScopes)
            : base(app, launchContext, fhirServerBaseUrl, identityServerBaseUrl)
        {
            _facade = facade;
            _applySmartScopes = applySmartScopes;
        }
        private IFhirSystemServiceR4<TSP> _facade;
        private bool _applySmartScopes;


        /// <summary>
        /// Process request and craft response.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="callback"></param>
        /// <returns></returns>
        /// <remarks>
        /// Sample of processing the background execution using Task.Run from the cefsharp documentation
        /// https://github.com/cefsharp/CefSharp/blob/20dad8aced51e077780ce3fe3b9a3766c15a7102/CefSharp.Example/FlashResourceHandler.cs#L13
        /// </remarks>
        public override CefReturnValue ProcessRequestAsync(IRequest request, ICallback callback)
        {
            var uri = new Uri(request.Url);
            Console.WriteLine($"-----------------\r\n{request.Url}");

            // Raw Options request
            if (request.Method == "OPTIONS" && uri.LocalPath != "/")
                return WriteOptionsOutput(callback);

            // TODO: Check that the session hasn't expired (not critical as the window is within the control of the PMS system)
            //if (_launchContext.ExpiresAt < DateTimeOffset.Now) 
            //{
            //    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.Security, "Authorization has expired, please re-authorize");
            //    return CefReturnValue.Continue;
            //}

            // This is a regular FHIR request
            var headers = new List<KeyValuePair<string, IEnumerable<string>>>();
            foreach (var key in request.Headers?.AllKeys)
            {
                headers.Add(new KeyValuePair<string, IEnumerable<string>>(key, request.Headers.GetValues(key)));
            }
            ModelBaseInputs<TSP> requestDetails = new ModelBaseInputs<TSP>(_launchContext.Principal, null, request.Method, uri, new Uri($"https://{_fhirServerBaseUrl}"), null, headers, null);

            var rtParser = new FhirRequestTypeParser();
            var rt = rtParser.ParseRequestType(request.Method, request.Url, request.GetHeaderByName("Content-Type"));

            // Check for security required
            if (rt != FhirRequestTypeParser.FhirRequestType.Unknown
                && rt != FhirRequestTypeParser.FhirRequestType.UnknownResourceType
                && rt != FhirRequestTypeParser.FhirRequestType.SmartConfiguration
                && rt != FhirRequestTypeParser.FhirRequestType.CapabilityStatement)
            {
                // Check the bearer header (as all calls to this API MUST be provided the bearer token, otherwise are straight rejected)
                string bearer = request.GetHeaderByName("authorization");
                if (string.IsNullOrEmpty(bearer))
                {
                    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.Security, "No Bearer token provided for this request");
                    return CefReturnValue.Continue;
                }
                if (bearer != "Bearer " + _launchContext.Bearer)
                {
                    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.Security, "Invalid Bearer token provided for this request");
                    return CefReturnValue.Continue;
                }
            }
            IFhirResourceServiceR4<TSP> rs = null;
            if (!string.IsNullOrEmpty(rtParser.ResourceType))
            {
                rs = _facade.GetResourceService(requestDetails, rtParser.ResourceType);
                if (rs == null)
                {
                    SetErrorResponse(callback, HttpStatusCode.NotFound, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotSupported, $"Resource type {rtParser.ResourceType} is not supported");
                    return CefReturnValue.Continue;
                }
            }
            try
            {

                // For queries that leverage the summary parameter (that's on the URL)
                var summary = GetSummaryParameter(uri);
                var parameters = TupledParameters(uri, SearchQueryParameterNames).ToList(); // convert to a list so that we can append the patient ID when required
                int? pagesize = GetIntParameter(uri, FhirParameter.COUNT);
                string sortby = GetParameter(uri, FhirParameter.SORT);
                Resource postedResource = null;
                if (request.PostData != null)
                {
                    var data = request.PostData.Elements.FirstOrDefault();
                    var body = data.GetBody();
                    if (request.GetHeaderByName("Content-Type").Contains("application/x-www-form-urlencoded"))
                    {
                        // This is the search post style data
                        System.Collections.Specialized.NameValueCollection nvp = System.Web.HttpUtility.ParseQueryString(body);
                        if (nvp.HasKeys())
                        {
                            foreach (string key in nvp.Keys)
                            {
                                foreach (string val in nvp.GetValues(key))
                                {
                                    if (key == FhirParameter.COUNT)
                                    {
                                        if (!pagesize.HasValue)
                                        {
                                            int n;
                                            if (int.TryParse(key, out n))
                                                pagesize = n;
                                        }
                                    }
                                    else if (key == FhirParameter.SUMMARY)
                                    {
                                        switch (val.ToLower())
                                        {
                                            case "true": summary = SummaryType.True; break;
                                            case "false": summary = Hl7.Fhir.Rest.SummaryType.False; break;
                                            case "text": summary = SummaryType.Text; break;
                                            case "data": summary = SummaryType.Data; break;
                                            case "count": summary = SummaryType.Count; break;
                                        }
                                    }
                                    else if (key == FhirParameter.SORT)
                                    {
                                        sortby = val;
                                    }
                                    else
                                    {
                                        parameters.Add(new KeyValuePair<string, string>(key, val));
                                    }
                                }
                            }
                        }
                    }
                    else if (request.GetHeaderByName("Content-Type").Contains("xml"))
                        postedResource = new Hl7.Fhir.Serialization.FhirXmlParser().Parse<Resource>(body);
                    else
                        postedResource = new Hl7.Fhir.Serialization.FhirJsonParser().Parse<Resource>(body);
                }

                switch (rt)
                {
                    case FhirRequestTypeParser.FhirRequestType.Unknown:
                        SetErrorResponse(callback, HttpStatusCode.BadRequest, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotSupported, $"Unsupported request {request.Method} {uri.LocalPath}");
                        return CefReturnValue.Continue;

                    case FhirRequestTypeParser.FhirRequestType.UnknownResourceType:
                        SetErrorResponse(callback, HttpStatusCode.BadRequest, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotSupported, $"Unsupported Resource Type in request: {request.Method} {uri.LocalPath}");
                        return CefReturnValue.Continue;

                    case FhirRequestTypeParser.FhirRequestType.SystemHistory:
                    case FhirRequestTypeParser.FhirRequestType.SystemSearch:
                    case FhirRequestTypeParser.FhirRequestType.ResourceTypeHistory:
                    case FhirRequestTypeParser.FhirRequestType.ResourceInstancePatch:
                    case FhirRequestTypeParser.FhirRequestType.ResourceInstanceHistory:
                        SetErrorResponse(callback, HttpStatusCode.BadRequest, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotSupported, $"Unsupported request {request.Method} {uri.LocalPath}");
                        return CefReturnValue.Continue;

                    // ----------------------------------------------------------------------------------------------
                    case FhirRequestTypeParser.FhirRequestType.SmartConfiguration:
                        return ProcessWellKnownSmartConfigurationRequest(callback);

                    case FhirRequestTypeParser.FhirRequestType.CapabilityStatement:
                        if (request.Method == "OPTIONS")
                        {
                            // base.Headers.Add("Access-Control-Allow-Origin", "*");
                            base.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,OPTIONS");
                            base.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept, authorization");
                        }
                        return ProcessFhirMetadataRequest(callback, requestDetails);

                    case FhirRequestTypeParser.FhirRequestType.SystemBatchOperation:
                        return ProcessTransactionBundle(callback, requestDetails, postedResource);

                    case FhirRequestTypeParser.FhirRequestType.SystemOperation:
                        return ProcessSystemOperation(callback, requestDetails, postedResource, rtParser.OperationName, summary);

                    // ----------------------------------------------------------------------------------------------
                    case FhirRequestTypeParser.FhirRequestType.ResourceTypeSearch:
                        return ProcessSearchRequest(callback, requestDetails, rs, summary, parameters, pagesize, rtParser.ResourceType, sortby);

                    case FhirRequestTypeParser.FhirRequestType.ResourceTypeOperation:
                        return ProcessResourceTypeOperation(callback, requestDetails, rs, postedResource, rtParser.ResourceType, rtParser.OperationName, summary);

                    case FhirRequestTypeParser.FhirRequestType.ResourceTypeCreate:
                        return ProcessResourceCreate(request, callback, uri, requestDetails, rs, postedResource, rtParser.ResourceType, rtParser.ResourceId);

                    // ----------------------------------------------------------------------------------------------
                    case FhirRequestTypeParser.FhirRequestType.ResourceInstanceGet:
                        return ProcessGetResourceInstance(callback, requestDetails, rs, summary, rtParser.ResourceType, rtParser.ResourceId, rtParser.Version);

                    case FhirRequestTypeParser.FhirRequestType.ResourceInstanceGetVersion:
                        return ProcessGetResourceInstance(callback, requestDetails, rs, summary, rtParser.ResourceType, rtParser.ResourceId, rtParser.Version);

                    case FhirRequestTypeParser.FhirRequestType.ResourceInstanceUpdate:
                        return ProcessResourceCreate(request, callback, uri, requestDetails, rs, postedResource, rtParser.ResourceType, rtParser.ResourceId);

                    case FhirRequestTypeParser.FhirRequestType.ResourceInstanceDelete:
                        return ProcessGetResourceInstanceDelete(callback, requestDetails, rs, summary, rtParser.ResourceType, rtParser.ResourceId);

                    case FhirRequestTypeParser.FhirRequestType.ResourceInstanceOperation:
                        return ProcessGetResourceInstanceOperation(callback, requestDetails, rs, summary, rtParser.ResourceType, rtParser.ResourceId, rtParser.OperationName, postedResource);
                }

                // This was an unknown request type
                SetErrorResponse(callback, HttpStatusCode.BadRequest, OperationOutcome.IssueSeverity.Fatal, OperationOutcome.IssueType.NotSupported, $"Unsupported request: {request.Method}: {uri.LocalPath}");
                return CefReturnValue.Continue;
            }
            catch (Exception ex)
            {
                // Totally unknown request encountered
                SetErrorResponse(callback, ex);
                return CefReturnValue.Continue;
            }
        }

        private CefReturnValue ProcessTransactionBundle(ICallback callback, ModelBaseInputs<TSP> requestDetails, Resource postedResource)
        {
            Bundle b = postedResource as Bundle;

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var result = await _facade.ProcessBatch(requestDetails, b);
                    var statusCode = result.HasAnnotation<HttpStatusCode>() ? result.Annotation<HttpStatusCode>() : HttpStatusCode.OK;
                    SetResponse(callback, statusCode, result);
                }
                catch (Exception ex)
                {
                    SetErrorResponse(callback, ex);
                }
            });
            return CefReturnValue.ContinueAsync;
        }

        private CefReturnValue ProcessSystemOperation(ICallback callback, ModelBaseInputs<TSP> requestDetails, Resource postedResource, string operationName, SummaryType summary)
        {
            // Check that this is within the smart context
            if (_applySmartScopes)
            {
                var scopeRead = SmartScopes.HasSecureAccess_SmartOnFhir(requestDetails, "system", SmartOperation.search);
                if (scopeRead?.SearchAccess == false)
                {
                    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User.Identity.Name}/App {_app.Name} does not have system search access");
                    return CefReturnValue.Continue;
                }
            }
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var parameters = postedResource as Parameters;
                    if (parameters == null && postedResource != null)
                    {
                        parameters = new Parameters();
                        parameters.Parameter.Add(new Parameters.ParameterComponent() { Name = "resource", Resource = postedResource });
                    }
                    var result = await _facade.PerformOperation(requestDetails, operationName, parameters, summary);
                    if (result == null)
                    {
                        SetErrorResponse(callback, HttpStatusCode.InternalServerError, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Unknown, $"System operation {operationName} had no result");
                        return;
                    }
                    // All good return the resource
                    var statusCode = result?.HasAnnotation<HttpStatusCode>() == true ? result.Annotation<HttpStatusCode>() : HttpStatusCode.OK;
                    SetResponse(callback, statusCode, result);
                }
                catch (Exception ex)
                {
                    SetErrorResponse(callback, ex);
                }
            });
            return CefReturnValue.ContinueAsync;
        }

        private CefReturnValue ProcessResourceTypeOperation(ICallback callback, ModelBaseInputs<TSP> requestDetails, IFhirResourceServiceR4<TSP> rs, Resource postedResource, string resourceType, string operationName, SummaryType summary)
        {
            // Check that this is within the smart context
            if (_applySmartScopes)
            {
                var scopeRead = SmartScopes.HasSecureAccess_SmartOnFhir(requestDetails, resourceType, SmartOperation.search);
                if (scopeRead?.SearchAccess == false)
                {
                    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User.Identity.Name}/App {_app.Name} does not have search access on {resourceType}");
                    return CefReturnValue.Continue;
                }
            }
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var parameters = postedResource as Parameters;
                    if (parameters == null && postedResource != null)
                    {
                        parameters = new Parameters();
                        parameters.Parameter.Add(new Parameters.ParameterComponent() { Name = "resource", Resource = postedResource });
                    }
                    var result = await rs.PerformOperation(operationName, parameters, summary);
                    if (result == null)
                    {
                        SetErrorResponse(callback, HttpStatusCode.InternalServerError, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Unknown, $"Resource operation {resourceType}/{operationName} had no result");
                        return;
                    }
                    // All good return the resource
                    var statusCode = result?.HasAnnotation<HttpStatusCode>() == true ? result.Annotation<HttpStatusCode>() : HttpStatusCode.OK;
                    SetResponse(callback, statusCode, result);
                }
                catch (Exception ex)
                {
                    SetErrorResponse(callback, ex);
                }
            });
            return CefReturnValue.ContinueAsync;
        }

        private CefReturnValue ProcessResourceCreate(IRequest request, ICallback callback, Uri uri, ModelBaseInputs<TSP> requestDetails, IFhirResourceServiceR4<TSP> rs, Resource postedResource, string resourceType, string resourceId)
        {
            // Check that this is within the smart context
            if (_applySmartScopes)
            {
                var scopeRead = SmartScopes.HasSecureAccess_SmartOnFhir(requestDetails, resourceType, SmartOperation.create);
                if (scopeRead?.CreateAccess == false)
                {
                    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User.Identity.Name}/App {_app.Name} does not have create access on {resourceType}");
                    return CefReturnValue.Continue;
                }
                if (resourceType == "Patient")
                {
                    if (requestDetails.User.PatientInContext() != resourceId && scopeRead.SmartUserType != SmartUserType.user)
                    {
                        SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User.Identity.Name}/App {_app.Name} does not have access to {resourceType}/{resourceId}");
                        return CefReturnValue.Continue;
                    }
                }
            }
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var result = await rs.Create(postedResource, uri.Query, request.GetHeaderByName("If-None-Exist"), null);
                    if (result == null)
                    {
                        SetErrorResponse(callback, HttpStatusCode.InternalServerError, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Unknown, $"Resource create had no result");
                        return;
                    }
                    // All good return the resource
                    var statusCode = result?.HasAnnotation<HttpStatusCode>() == true ? result.Annotation<HttpStatusCode>() : HttpStatusCode.OK;
                    SetResponse(callback, statusCode, result);
                }
                catch (Exception ex)
                {
                    SetErrorResponse(callback, ex);
                }
            });
            return CefReturnValue.ContinueAsync;
        }

        private CefReturnValue ProcessSearchRequest(ICallback callback, ModelBaseInputs<TSP> requestDetails, IFhirResourceServiceR4<TSP> rs, SummaryType summary, List<KeyValuePair<string, string>> parameters, int? pagesize, string resourceType, string sortby)
        {
            if (_applySmartScopes)
            {
                var scopeSearch = SmartScopes.HasSecureAccess_SmartOnFhir(requestDetails, resourceType, SmartOperation.search);
                if (scopeSearch?.SearchAccess == false)
                {
                    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User?.Identity.Name}/App {_app.Name} does not have search access on {resourceType}");
                    return CefReturnValue.Continue;
                }
                if (scopeSearch?.SmartUserType == SmartUserType.patient)
                {
                    if (!string.IsNullOrEmpty(requestDetails.User.PatientInContext()))
                    {
                        if (resourceType == "Patient")
                        {
                            parameters.Add(new KeyValuePair<string, string>("_id", requestDetails.User.PatientInContext()));
                        }
                        else if (Hl7.Fhir.Model.ModelInfo.SearchParameters.Any(sp => sp.Resource == resourceType && sp.Name == "patient"))
                        {
                            parameters.Add(new KeyValuePair<string, string>("patient", requestDetails.User.PatientInContext()));
                        }
                        else if (Hl7.Fhir.Model.ModelInfo.SearchParameters.Any(sp => sp.Resource == resourceType && sp.Name == "subject"))
                        {
                            parameters.Add(new KeyValuePair<string, string>("subject", requestDetails.User.PatientInContext()));
                        }
                    }
                }
            }
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var result = await rs.Search(parameters, pagesize, summary, sortby);
                    var statusCode = result.HasAnnotation<HttpStatusCode>() ? result.Annotation<HttpStatusCode>() : HttpStatusCode.OK;
                    SetResponse(callback, statusCode, result);
                }
                catch (Exception ex)
                {
                    SetErrorResponse(callback, ex);
                }
            });
            return CefReturnValue.ContinueAsync;
        }

        private CefReturnValue ProcessGetResourceInstance(ICallback callback, ModelBaseInputs<TSP> requestDetails, IFhirResourceServiceR4<TSP> rs, SummaryType summary, string resourceType, string resourceId, string version)
        {
            // Check that this is within the smart context
            if (_applySmartScopes)
            {
                var scopeRead = SmartScopes.HasSecureAccess_SmartOnFhir(requestDetails, resourceType, SmartOperation.read);
                if (scopeRead?.ReadAccess == false)
                {
                    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User?.Identity.Name}/App {_app.Name} does not have read access on {resourceType}");
                    return CefReturnValue.Continue;
                }
                if (resourceType == "Patient")
                {
                    if (requestDetails.User.PatientInContext() != resourceId
                        && !(scopeRead.SmartUserType == SmartUserType.user
                        || scopeRead.SmartUserType == SmartUserType.system))
                    {
                        SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User?.Identity.Name}/App {_app.Name} does not have access to {resourceType}/{resourceId}");
                        return CefReturnValue.Continue;
                    }
                }
            }
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var result = await rs.Get(resourceId, version, summary);
                    if (result == null)
                    {
                        if (!string.IsNullOrEmpty(version))
                            SetErrorResponse(callback, HttpStatusCode.NotFound, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotFound, $"Resource {resourceType}/{resourceId}/_history/{version} was not found");
                        else
                            SetErrorResponse(callback, HttpStatusCode.NotFound, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.NotFound, $"Resource {resourceType}/{resourceId} was not found");
                        return;
                    }
                    // Check for security access to this resource before returning it
                    if (resourceType != "Patient")
                    {
                        // This will need to be done by the Model classes, not the facade
                    }

                    // All good return the resource
                    var statusCode = result?.HasAnnotation<HttpStatusCode>() == true ? result.Annotation<HttpStatusCode>() : HttpStatusCode.OK;
                    SetResponse(callback, statusCode, result);
                }
                catch (Exception ex)
                {
                    SetErrorResponse(callback, ex);
                }
            });
            return CefReturnValue.ContinueAsync;
        }

        private CefReturnValue ProcessGetResourceInstanceOperation(ICallback callback, ModelBaseInputs<TSP> requestDetails, IFhirResourceServiceR4<TSP> rs, SummaryType summary, string resourceType, string resourceId, string OperationName, Resource postedResource)
        {
            // Check that this is within the smart context
            if (_applySmartScopes)
            {
                var scopeRead = SmartScopes.HasSecureAccess_SmartOnFhir(requestDetails, resourceType, SmartOperation.read);
                if (scopeRead?.ReadAccess == false)
                {
                    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User?.Identity.Name}/App {_app.Name} does not have read access on {resourceType}");
                    return CefReturnValue.Continue;
                }
                if (resourceType == "Patient")
                {
                    if (requestDetails.User.PatientInContext() != resourceId
                        && !(scopeRead.SmartUserType == SmartUserType.user
                        || scopeRead.SmartUserType == SmartUserType.system))
                    {
                        SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User?.Identity.Name}/App {_app.Name} does not have access to {resourceType}/{resourceId}");
                        return CefReturnValue.Continue;
                    }
                }
            }
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var parameters = postedResource as Parameters;
                    if (parameters == null && postedResource != null)
                    {
                        parameters = new Parameters();
                        parameters.Parameter.Add(new Parameters.ParameterComponent() { Name = "resource", Resource = postedResource });
                    }
                    var result = await rs.PerformOperation(resourceId, OperationName, parameters, summary);
                    if (result == null)
                    {
                        SetErrorResponse(callback, HttpStatusCode.InternalServerError, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Unknown, $"Resource operation {resourceType}/{resourceId}/{OperationName} had no result");
                        return;
                    }
                    // Check for security access to this resource before returning it
                    if (resourceType != "Patient")
                    {
                        // This will need to be done by the Model classes, not the facade
                    }

                    // All good return the resource
                    var statusCode = result?.HasAnnotation<HttpStatusCode>() == true ? result.Annotation<HttpStatusCode>() : HttpStatusCode.OK;
                    SetResponse(callback, statusCode, result);
                }
                catch (Exception ex)
                {
                    SetErrorResponse(callback, ex);
                }
            });
            return CefReturnValue.ContinueAsync;
        }

        private CefReturnValue ProcessGetResourceInstanceDelete(ICallback callback, ModelBaseInputs<TSP> requestDetails, IFhirResourceServiceR4<TSP> rs, SummaryType summary, string resourceType, string resourceId)
        {
            // Check that this is within the smart context
            if (_applySmartScopes)
            {
                var scopeDelete = SmartScopes.HasSecureAccess_SmartOnFhir(requestDetails, resourceType, SmartOperation.delete);
                if (scopeDelete?.ReadAccess == false)
                {
                    SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User?.Identity.Name}/App {_app.Name} does not have delete access on {resourceType}");
                    return CefReturnValue.Continue;
                }
                if (resourceType == "Patient")
                {
                    if (requestDetails.User.PatientInContext() != resourceId
                        && !(scopeDelete.SmartUserType == SmartUserType.user
                        || scopeDelete.SmartUserType == SmartUserType.system))
                    {
                        SetErrorResponse(callback, HttpStatusCode.Unauthorized, OperationOutcome.IssueSeverity.Error, OperationOutcome.IssueType.Security, $"User {requestDetails.User?.Identity.Name}/App {_app.Name} does not have access to {resourceType}/{resourceId}");
                        return CefReturnValue.Continue;
                    }
                }
            }
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var result = await rs.Delete(resourceId, null);
                    if (result == null)
                    {
                        SetResponse(callback, HttpStatusCode.OK, null);
                        return;
                    }

                    // Resource deleted
                    SetResponse(callback, HttpStatusCode.NoContent, null);
                }
                catch (Exception ex)
                {
                    SetErrorResponse(callback, ex);
                }
            });
            return CefReturnValue.ContinueAsync;
        }

        private CefReturnValue ProcessFhirMetadataRequest(ICallback callback, ModelBaseInputs<TSP> requestDetails)
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                // As per the documentation http://hl7.org/fhir/smart-app-launch/conformance/index.html
                CapabilityStatement cs;
                try
                {
                    cs = await _facade.GetConformance(requestDetails, Rest.SummaryType.False);
                }
                catch (Exception ex)
                {
                    SetErrorResponse(callback, ex);
                    return;
                }
                base.StatusCode = (int)HttpStatusCode.OK;

                // Update the security node with our internal security node
                if (cs.Rest[0].Security == null)
                    cs.Rest[0].Security = new Hl7.Fhir.Model.CapabilityStatement.SecurityComponent();
                Hl7.Fhir.Model.CapabilityStatement.SecurityComponent security = cs.Rest[0].Security;
                if (!security.Service.Any(cc => cc.Coding.Any(c => c.System == "http://hl7.org/fhir/restful-security-service" && c.Code == "SMART-on-FHIR")))
                    security.Service.Add(new Hl7.Fhir.Model.CodeableConcept("http://hl7.org/fhir/restful-security-service", "SMART-on-FHIR"));
                var extension = security.GetExtension("http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris");
                if (extension == null)
                {
                    extension = new Extension() { Url = "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris" };
                    security.Extension.Add(extension);
                }
                // remove the existing authentications, and put in our own
                extension.Extension.Clear();
                extension.AddExtension("token", new FhirUri($"https://{_identityServerBaseUrl}/token"));
                extension.AddExtension("authorize", new FhirUri($"https://{_identityServerBaseUrl}/authorize"));

                base.Stream = new MemoryStream(new Hl7.Fhir.Serialization.FhirJsonSerializer(new Hl7.Fhir.Serialization.SerializerSettings() { Pretty = true }).SerializeToBytes(cs));
                Console.WriteLine($"Success: {base.Stream.Length}");
                base.MimeType = "application/fhir+json";

                if (!callback.IsDisposed)
                    callback.Continue();
            });
            return CefReturnValue.ContinueAsync;
        }

        private CefReturnValue ProcessWellKnownSmartConfigurationRequest(ICallback callback)
        {
            base.StatusCode = (int)HttpStatusCode.OK;

            // http://build.fhir.org/ig/HL7/smart-app-launch/conformance.html
            FhirSmartAppLaunchConfiguration smart_config = new FhirSmartAppLaunchConfiguration();
            // populate the context based on the data we know
            smart_config.issuer = $"https://{_identityServerBaseUrl}";
            smart_config.authorization_endpoint = $"https://{_identityServerBaseUrl}/authorize";
            smart_config.token_endpoint = $"https://{_identityServerBaseUrl}/token";
            smart_config.scopes_supported = _app.AllowedScopes;
            smart_config.response_types_supported = new[] { "code", "code id_token" };
            smart_config.code_challenge_methods_supported = new[] { "S256" };
            var capabilities = new List<string> { "launch-ehr", "permission-v2", "context-ehr-patient", "authorize-post" };
            if (String.IsNullOrEmpty(_app.ClientSecret))
                capabilities.Add("client-public");
            else
                capabilities.Add("client-confidential-symmetric");
            smart_config.capabilities = capabilities;

            var jsonSettings = new Newtonsoft.Json.JsonSerializerSettings() { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore };
            base.Stream = new MemoryStream();
            StreamWriter sw = new StreamWriter(base.Stream, System.Text.Encoding.UTF8, 4096, true);
            sw.Write(Newtonsoft.Json.JsonConvert.SerializeObject(smart_config, settings: jsonSettings));
            sw.Flush();
            base.Headers.Add("Cache-Control", "no-store");
            base.Headers.Add("Pragma", "no-cache");
            base.MimeType = "application/json;charset=UTF-8";

            Console.WriteLine($"Success: {base.Stream.Length}");
            if (!callback.IsDisposed)
                callback.Continue();
            return CefReturnValue.Continue;
        }

        /// <summary>
        /// Retrieve all the parameters from a Request URL
        /// </summary>
        /// <param name="requestUri"></param>
        /// <param name="excludeParameters">Do not include any parameters from the provided collection</param>
        /// <returns></returns>
        public static IEnumerable<KeyValuePair<string, string>> TupledParameters(Uri requestUri, string[] excludeParameters = null)
        {
            var list = new List<KeyValuePair<string, string>>();

            string query = requestUri.OriginalString;
            if (query.Contains("?"))
            {
                query = query.Substring(query.IndexOf("?") + 1);
                System.Collections.Specialized.NameValueCollection nvp = System.Web.HttpUtility.ParseQueryString(query);

                if (nvp.HasKeys())
                {
                    foreach (string key in nvp.Keys)
                    {
                        if (excludeParameters == null || !excludeParameters.Contains(key))
                        {
                            foreach (string val in nvp.GetValues(key))
                                list.Add(new KeyValuePair<string, string>(key, val));
                        }
                    }
                }
            }
            return list;
        }

        public static string GetParameter(Uri requestUri, string keyParam)
        {
            System.Collections.Specialized.NameValueCollection nvp = System.Web.HttpUtility.ParseQueryString(requestUri.OriginalString);

            if (nvp.HasKeys())
            {
                return nvp.GetValues(keyParam)?.FirstOrDefault();
            }
            return null;
        }

        public static Hl7.Fhir.Rest.SummaryType GetSummaryParameter(Uri request)
        {
            string s = GetParameter(request, FhirParameter.SUMMARY);
            if (s == null)
                return Hl7.Fhir.Rest.SummaryType.False;

            switch (s.ToLower())
            {
                case "true": return Hl7.Fhir.Rest.SummaryType.True;
                case "false": return Hl7.Fhir.Rest.SummaryType.False;
                case "text": return Hl7.Fhir.Rest.SummaryType.Text;
                case "data": return Hl7.Fhir.Rest.SummaryType.Data;
                case "count": return Hl7.Fhir.Rest.SummaryType.Count;
                default: return Hl7.Fhir.Rest.SummaryType.False;
            }
        }
        public static int? GetIntParameter(Uri request, string name)
        {
            string s = GetParameter(request, name);
            int n;
            return (int.TryParse(s, out n)) ? n : (int?)null;
        }
    }

    public static class FhirParameter
    {
        public const string SUMMARY = "_summary";
        public const string COUNT = "_count";
        public const string SINCE = "_since";
        public const string SORT = "_sort";
    }
}
