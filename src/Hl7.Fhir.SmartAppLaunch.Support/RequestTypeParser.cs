using CefSharp;
using Hl7.Fhir.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hl7.Fhir.SmartAppLaunch
{
    public class FhirRequestTypeParser
    {
        public enum FhirRequestType
        {
            Unknown, // anything not processed correctly
            UnknownResourceType, // anything not processed correctly

            SmartConfiguration,

            CapabilityStatement,
            SystemHistory,
            SystemSearch,
            SystemBatchOperation,
            SystemOperation,

            ResourceTypeHistory,
            ResourceTypeSearch,
            ResourceTypeOperation,
            ResourceTypeCreate,

            ResourceInstanceGet,
            ResourceInstanceGetVersion,
            ResourceInstanceUpdate,
            ResourceInstanceDelete,
            ResourceInstancePatch,
            ResourceInstanceHistory,
            ResourceInstanceOperation,
        }

        public string ResourceType { get; private set; }
        public string ResourceId { get; private set; }
        public string Version { get; private set; }

        public FhirRequestType ParseRequestType(string method, string requestUrl, string contentType)
        {
            if (String.IsNullOrEmpty(requestUrl))
                return FhirRequestType.Unknown;
            var uri = new Uri(requestUrl);
            Console.WriteLine($"-----------------\r\n{requestUrl}");

            if (method == "OPTIONS" && uri.LocalPath == "/")
                return FhirRequestType.CapabilityStatement;

            if (String.IsNullOrEmpty(uri.LocalPath))
                return FhirRequestType.Unknown;

            // ----------------------------------------------------------------------
            // System level routes
            if (method == "GET")
            {
                if (uri.LocalPath == "/.well-known/smart-configuration")
                    return FhirRequestType.SmartConfiguration;
                if (uri.LocalPath == "/metadata")
                    return FhirRequestType.CapabilityStatement;
                if (uri.LocalPath == "/")
                    return FhirRequestType.SystemSearch;
                if (uri.LocalPath == "/_history")
                    return FhirRequestType.SystemHistory;
                if (uri.LocalPath.StartsWith("/$"))
                    return FhirRequestType.SystemOperation;
            }

            if (method == "POST")
            {
                if (uri.LocalPath == "/")
                {
                    if (contentType == "application/x-www-form-urlencoded")
                        return FhirRequestType.SystemSearch;
                    return FhirRequestType.SystemBatchOperation;
                }
                if (uri.LocalPath.StartsWith("/$"))
                    return FhirRequestType.SystemOperation;
            }

            // ----------------------------------------------------------------------
            // Resource Type level interactions
            string resourceType = uri.LocalPath.Substring(1);
            string resourceSubPath = null;
            if (resourceType.Contains("/"))
            {
                resourceSubPath = resourceType.Substring(resourceType.IndexOf("/"));
                resourceType = resourceType.Substring(0, resourceType.IndexOf("/"));
            }
            ResourceType = resourceType; // yes grab it before verifying, so that can be used in error messages
            if (!ModelInfo.IsKnownResource(resourceType))
            {
                // unknown resource type
                return FhirRequestType.UnknownResourceType;
            }

            if (method == "GET")
            {
                if (resourceSubPath == "/" || string.IsNullOrEmpty(resourceSubPath))
                    return FhirRequestType.ResourceTypeSearch;
                if (resourceSubPath == "/_history")
                    return FhirRequestType.ResourceTypeHistory;
                if (resourceSubPath.StartsWith("/$"))
                    return FhirRequestType.ResourceTypeOperation;
            }

            if (method == "POST")
            {
                if (resourceSubPath == "/" || string.IsNullOrEmpty(resourceSubPath))
                {
                    if (contentType == "application/x-www-form-urlencoded")
                        return FhirRequestType.ResourceTypeSearch;
                    return FhirRequestType.ResourceTypeCreate;
                }
                if (resourceSubPath.StartsWith("/$"))
                    return FhirRequestType.ResourceTypeOperation;
            }

            // ----------------------------------------------------------------------
            // Resource Instance level interactions
            string resourceId = resourceSubPath.Substring(1);
            string resourceIdSubPath = null;
            if (resourceId.Contains("/"))
            {
                resourceIdSubPath = resourceId.Substring(resourceId.IndexOf("/"));
                resourceId = resourceId.Substring(0, resourceId.IndexOf("/"));
            }
            ResourceId = resourceId;

            if (method == "GET")
            {
                if (resourceIdSubPath == "/" || string.IsNullOrEmpty(resourceIdSubPath))
                    return FhirRequestType.ResourceInstanceGet;
                if (resourceIdSubPath == "/_history")
                    return FhirRequestType.ResourceInstanceHistory;
                if (resourceIdSubPath.StartsWith("/$"))
                    return FhirRequestType.ResourceInstanceOperation;
                if (resourceIdSubPath.StartsWith("/_history/"))
                {
                    Version = resourceIdSubPath.Substring("/_history/".Length);
                    return FhirRequestType.ResourceInstanceGetVersion;
                }
            }

            if (method == "PUT")
            {
                if (resourceIdSubPath == "/" || string.IsNullOrEmpty(resourceIdSubPath))
                {
                    if (contentType == "application/x-www-form-urlencoded")
                        return FhirRequestType.Unknown;
                    return FhirRequestType.ResourceInstanceUpdate;
                }
                return FhirRequestType.Unknown;
            }

            if (method == "POST")
            {
                if (resourceIdSubPath == "/" || string.IsNullOrEmpty(resourceIdSubPath))
                {
                    if (contentType == "application/x-www-form-urlencoded")
                        return FhirRequestType.Unknown;
                    return FhirRequestType.ResourceInstanceUpdate;
                }
                if (resourceIdSubPath.StartsWith("/$"))
                    return FhirRequestType.ResourceInstanceOperation;
                return FhirRequestType.Unknown;
            }

            if (method == "DELETE")
            {
                if (!string.IsNullOrEmpty(resourceId))
                    return FhirRequestType.ResourceInstanceDelete;
            }
            if (method == "PATCH")
            {
                if (!string.IsNullOrEmpty(resourceId))
                    return FhirRequestType.ResourceInstancePatch;
            }

            return FhirRequestType.Unknown;
        }
    }
}
