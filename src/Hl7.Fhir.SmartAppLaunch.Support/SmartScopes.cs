using Hl7.Fhir.WebApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Hl7.Fhir.SmartAppLaunch
{
    /// <summary>
    /// http://docs.smarthealthit.org/authorization/scopes-and-launch-context/
    /// </summary>
    public class SmartScopes
    {
        /// <summary>
        /// Process if the provided user has access to the requested information
        /// based on SMART on FHIR scopes
        /// (anonymous access is not evaluated by this routine)
        /// http://build.fhir.org/ig/HL7/smart-app-launch/scopes-and-launch-context.html
        /// </summary>
        /// <param name="inputs"></param>
        /// <param name="resourceName"></param>
        /// <param name="httpOperation"></param>
        /// <returns></returns>
        public static ScopeAccess HasSecureAccess_SmartOnFhir(ModelBaseInputs inputs, string resourceName, SmartOperation httpOperation)
        {
            var principal = inputs.User as ClaimsPrincipal;
            if (principal == null)
                return null;

            // SMART on FHIR Scope processing
            var scopes = principal.Claims.Where(c => c.Type == Constants.ClaimTypes.Scope && (c.Value.StartsWith("user/") || c.Value.StartsWith("patient/") || c.Value.StartsWith("system/")))
                .Select(c => new ScopeAccess(c.Value));

            var result = new ScopeAccess()
            {
                ResourceName = resourceName
            };
            // the where is done here and not when extracting the scopes as we need to know 
            // if there are any scopes to know that scopes have been defined.
            foreach (var sa in scopes.Where(sa => sa.ResourceName == resourceName || sa.ResourceName == "*"))
            {
                if (sa.PermitsOperation(httpOperation))
                {
                    // tweak the user setting
                    if (result.SmartUserType < sa.SmartUserType)
                        result.SmartUserType = sa.SmartUserType;
                }
                if (sa.CreateAccess)
                    result.CreateAccess = true;
                if (sa.ReadAccess)
                    result.ReadAccess = true;
                if (sa.UpdateAccess)
                    result.UpdateAccess = true;
                if (sa.DeleteAccess)
                    result.DeleteAccess = true;
                if (sa.SearchAccess)
                    result.SearchAccess = true;
                //if (sa.OperationAccess.Any())
                //    result.OperationAccess.AddRange(sa.OperationAccess);
            }

            // default is no access
            if (scopes.Count() > 0)
                return result;

            // If there were no scopes defined, then can't make any statements about the claim
            return null;
        }
    }

    public class ScopeAccess
    {
        public static ScopeAccess NoAccess(string resourceName)
        {
            // yes the "none" here isn't really a scope in the smart on fhir spec
            // but we are just using it to create a no access scope result
            return new ScopeAccess($"user/{resourceName}.none");
        }

        internal ScopeAccess()
        {
        }

        public bool PermitsOperation(SmartOperation httpOperation)
        {
            switch (httpOperation)
            {
                case SmartOperation.create:
                    return CreateAccess;
                case SmartOperation.read:
                    return ReadAccess;
                case SmartOperation.update:
                    return UpdateAccess;
                case SmartOperation.delete:
                    return DeleteAccess;
                case SmartOperation.search:
                    return SearchAccess;
                default:
                    break;
            }
            return false;
        }

        public ScopeAccess(string scope)
        {
            // skip the user or patient part of the scope
            if (!scope.Contains("/"))
                return; // this isn't a valid scope, so just barf
            switch (scope.Substring(0, scope.IndexOf("/")))
            {
                case "patient":
                    SmartUserType = SmartUserType.patient;
                    break;
                case "user":
                    SmartUserType = SmartUserType.user;
                    break;
                case "system":
                    SmartUserType = SmartUserType.system;
                    break;
                default:
                    return; // this isn't a valid scope, so just barf (again)
            }
            scope = scope.Substring(scope.IndexOf("/") + 1);
            if (scope?.Contains(".") == true)
            {
                ResourceName = scope.Substring(0, scope.IndexOf('.'));
                string access = scope.Substring(scope.IndexOf('.') + 1);
                // SMART v1 scopes processing
                switch (access)
                {
                    case "read":
                        ReadAccess = true;
                        SearchAccess = true;
                        return;
                    case "write":
                        CreateAccess = true;
                        UpdateAccess = true;
                        return;
                    case "*":
                        CreateAccess = true;
                        ReadAccess = true;
                        UpdateAccess = true;
                        DeleteAccess = true;
                        SearchAccess = true;
                        return;
                }

                // SMART v2 scopes processing
                foreach (var a in access)
                {
                    switch (a)
                    {
                        case 'c':
                            CreateAccess = true;
                            break;
                        case 'r':
                            ReadAccess = true;
                            break;
                        case 'u':
                            UpdateAccess = true;
                            break;
                        case 'd':
                            DeleteAccess = true;
                            break;
                        case 's':
                            SearchAccess = true;
                            break;
                        default:
                            // corrupt scope access, so no permissions to come from this rule
                            CreateAccess = false;
                            ReadAccess = false;
                            UpdateAccess = false;
                            DeleteAccess = false;
                            SearchAccess = false;
                            return; // this isn't a valid scope, so just barf - yet again
                    }
                }
            }
        }

        /// <summary>
        /// If this scope provides no access, then return true
        /// (may be an operation with access)
        /// </summary>
        /// <returns></returns>
        public bool IsDenyAllAccessRule()
        {
            if (CreateAccess)
                return false;
            if (ReadAccess)
                return false;
            if (UpdateAccess)
                return false;
            if (DeleteAccess)
                return false;
            if (SearchAccess)
                return false;
            return true;
        }

        public SmartUserType SmartUserType { get; internal set; } = SmartUserType.undefined;
        public string ResourceName { get; internal set; }
        public bool CreateAccess { get; internal set; }
        public bool ReadAccess { get; internal set; }
        public bool UpdateAccess { get; internal set; }
        public bool DeleteAccess { get; internal set; }
        public bool SearchAccess { get; internal set; }

        //public List<string> OperationAccess { get; internal set; } = new List<string>();
    }
    public enum SmartUserType { undefined, patient, user, system }
    public enum SmartOperation { create, read, update, delete, search }
}
