using System.Linq;
using NUnit.Framework;
using ZeroEngine.World.Authoring;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class AreaAuthoringValidationKernelTests
    {
        [Test]
        public void AreaAuthoringIssue_ErrorSeverity_IsErrorAndFormatsContext()
        {
            var issue = new AreaAuthoringIssue(
                AreaAuthoringIssueSeverity.Error,
                "AREA_ID_EMPTY",
                "Area id is empty.",
                "Assets/World/Areas/Test.asset",
                "Area_Test");

            Assert.IsTrue(issue.IsError);
            Assert.AreEqual("AREA_ID_EMPTY", issue.Code);
            Assert.AreEqual("Assets/World/Areas/Test.asset", issue.AssetPath);
            Assert.AreEqual("AREA_ID_EMPTY: Area id is empty. [Assets/World/Areas/Test.asset] (Area_Test)", issue.ToString());
        }

        [Test]
        public void YamlScanner_ExtractsScalarsOnlyFromSelectedComponentBlocks()
        {
            var yaml = string.Join("\n",
                "--- !u!114",
                "m_Script: {fileID: 11500000, guid: marker-guid, type: 3}",
                "  _portalId: marker_should_not_count",
                "--- !u!114",
                "m_Script: {fileID: 11500000, guid: portal-guid, type: 3}",
                "  _portalId: portal_a");

            var portalIds = AreaAuthoringYamlScanner.ExtractComponentBlocks(yaml, "portal-guid")
                .Select(block => block.GetScalar("_portalId"))
                .ToArray();

            CollectionAssert.AreEqual(new[] { "portal_a" }, portalIds);
        }

        [Test]
        public void StableIdValidation_ReportsDuplicateStableIds()
        {
            var yaml = string.Join("\n",
                "--- !u!114",
                "m_Script: {fileID: 11500000, guid: marker-guid, type: 3}",
                "  _markerId: marker_a",
                "--- !u!114",
                "m_Script: {fileID: 11500000, guid: marker-guid, type: 3}",
                "  _markerId: marker_a");

            var issues = AreaAuthoringComponentValidator.ValidateStableIds(
                "Assets/Scenes/Areas/Test.unity",
                yaml,
                "marker-guid",
                "_markerId",
                stableIdsAreRequired: true,
                emptyCode: "MARKER_ID_EMPTY",
                duplicateCode: "MARKER_ID_DUPLICATE",
                displayName: "RuntimePrefabMarker");

            Assert.That(issues.Any(issue => issue.Code == "MARKER_ID_DUPLICATE"), Is.True);
        }

        [Test]
        public void ReferenceValidation_ReportsMissingReference()
        {
            var yaml = string.Join("\n",
                "--- !u!114",
                "m_Script: {fileID: 11500000, guid: marker-guid, type: 3}",
                "  _markerId: marker_a",
                "  _runtimePrefab: {fileID: 0}");

            var issues = AreaAuthoringComponentValidator.ValidateRequiredReferences(
                "Assets/Scenes/Areas/Test.unity",
                yaml,
                "marker-guid",
                "_runtimePrefab",
                "_markerId",
                missingCode: "MARKER_PREFAB_MISSING",
                message: "RuntimePrefabMarker must reference a runtime prefab.");

            Assert.That(issues.Any(issue => issue.Code == "MARKER_PREFAB_MISSING"), Is.True);
        }

        [Test]
        public void PortalGraphValidation_ReportsDuplicateAndMissingEndpoints()
        {
            var locations = new[]
            {
                new AreaAuthoringPortalLocation("portal_a", "area_a"),
                new AreaAuthoringPortalLocation("portal_a", "area_a")
            };
            var connections = new[]
            {
                new AreaAuthoringPortalConnection("portal_a", "portal_missing")
            };

            var issues = AreaAuthoringGraphValidator.ValidatePortalGraph(
                new[] { "area_a" },
                locations,
                connections,
                "Assets/World/PortalGraph.asset");

            Assert.That(issues.Any(issue => issue.Code == "PORTAL_ID_DUPLICATE"), Is.True);
            Assert.That(issues.Any(issue => issue.Code == "PORTAL_CONNECTION_ENDPOINT_MISSING"), Is.True);
        }
    }
}
