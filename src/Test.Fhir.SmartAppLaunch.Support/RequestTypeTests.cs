using Hl7.Fhir.Model;
using Hl7.Fhir.SmartAppLaunch;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test.Fhir.SmartAppLaunch.Support
{
    [TestClass]
    public class RequestTypeTests
    {
        static IEnumerable<object[]> ReusableTestDataProperty
        {
            get
            {
                return new[]
                {
                    new object[] {FhirRequestTypeParser.FhirRequestType.Unknown, "GET", null, "", null, null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.SystemSearch, "GET", null, "http://example.org", null, null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.SmartConfiguration, "GET", null, "http://example.org/.well-known/smart-configuration", null, null },

                    new object[] {FhirRequestTypeParser.FhirRequestType.CapabilityStatement,"OPTIONS", null, "http://example.org/", null, null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.CapabilityStatement,"GET", null, "http://example.org/metadata", null, null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.SystemHistory,"GET", null, "http://example.org/_history", null, null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.SystemSearch,"GET", null, "http://example.org/", null, null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.SystemSearch,"POST", "application/x-www-form-urlencoded", "http://example.org/", null, null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.SystemBatchOperation,"POST", "application/fhir+json", "http://example.org/", null, null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.SystemOperation,"GET", null, "http://example.org/$sample", null, null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.SystemOperation,"POST", "application/fhir+json", "http://example.org/$sample", null, null },

                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceTypeHistory,"GET", null, "http://example.org/Patient/_history", "Patient", null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceTypeSearch,"GET", null, "http://example.org/Patient", "Patient", null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceTypeSearch,"GET", null, "http://example.org/Patient/", "Patient", null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceTypeSearch,"POST", "application/x-www-form-urlencoded", "http://example.org/Patient", "Patient", null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceTypeSearch,"POST", "application/x-www-form-urlencoded", "http://example.org/Patient/", "Patient", null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceTypeOperation,"GET", null, "http://example.org/Patient/$tsample", "Patient", null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceTypeOperation,"POST", "application/fhir+json", "http://example.org/Patient/$tsample", "Patient", null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceTypeCreate,"POST", "application/fhir+json", "http://example.org/Patient", "Patient", null },
                    new object[] {FhirRequestTypeParser.FhirRequestType.UnknownResourceType,"GET", null, "http://example.org/glarb", "glarb", null }, // intentional incorrect resource type

                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceInstanceGet,"GET", null, "http://example.org/Patient/example" },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceInstanceGetVersion,"GET", null, "http://example.org/Patient/example/_history/1", "Patient", "example", "1" },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceInstanceUpdate,"PUT", null, "http://example.org/Patient/example" },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceInstanceDelete,"DELETE", null, "http://example.org/Patient/example" },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceInstancePatch,"PATCH", null, "http://example.org/Patient/example" },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceInstanceHistory,"GET", null, "http://example.org/Patient/example/_history" },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceInstanceOperation,"GET", null, "http://example.org/Patient/example/$testme" },
                    new object[] {FhirRequestTypeParser.FhirRequestType.ResourceInstanceOperation,"POST", "application/fhir+json", "http://example.org/Patient/example/$testme" }
                };
            }
        }

        [TestMethod]
        [DynamicData("ReusableTestDataProperty")]
        public void TestRoutes(FhirRequestTypeParser.FhirRequestType expected, string method, string contentType, string Url, string resourceType = "Patient", string resourceId = "example", string versionId = null)
        {
            FhirRequestTypeParser parser = new FhirRequestTypeParser();
            var result = parser.ParseRequestType(method, Url, contentType);
            Assert.AreEqual(expected, result);
            Assert.AreEqual(resourceType, parser.ResourceType);
            Assert.AreEqual(resourceId, parser.ResourceId);
            Assert.AreEqual(versionId, parser.Version);
            if (result == FhirRequestTypeParser.FhirRequestType.ResourceInstanceOperation)
                Assert.AreEqual("testme", parser.OperationName);
            if (result == FhirRequestTypeParser.FhirRequestType.ResourceTypeOperation)
                Assert.AreEqual("tsample", parser.OperationName);
            if (result == FhirRequestTypeParser.FhirRequestType.SystemOperation)
                Assert.AreEqual("sample", parser.OperationName);
            System.Diagnostics.Trace.WriteLine(parser.OperationName);
        }
    }
}
