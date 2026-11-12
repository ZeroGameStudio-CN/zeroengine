using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZeroEngine.World.Editor.WorldGraph;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Tests.Editor
{
    [Category("Boundary")]
    public sealed class WorldSceneContractValidatorTests
    {
        private const string SourceCellId = "cell.outdoor";
        private const string TargetCellId = "cell.interior";
        private const string SourceAnchorId = "anchor.outdoor.entry";
        private const string TargetAnchorId = "anchor.interior.exit";
        private const string BoundaryId = "boundary.interior";
        private const string LinkId = "link.interior";

        private string _testAssetDirectory;

        [SetUp]
        public void SetUp()
        {
            _testAssetDirectory = "Assets/ZeroEngineWorldSceneContractValidatorTests_"
                                  + Guid.NewGuid().ToString("N");
            var folderGuid = AssetDatabase.CreateFolder(
                "Assets",
                _testAssetDirectory.Substring("Assets/".Length));
            Assert.That(folderGuid, Is.Not.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(_testAssetDirectory)
                && AssetDatabase.IsValidFolder(_testAssetDirectory))
            {
                Assert.That(
                    AssetDatabase.DeleteAsset(_testAssetDirectory),
                    Is.True,
                    $"Failed to delete synthetic test assets at {_testAssetDirectory}.");
            }
        }

        [Test]
        public void Validate_SeamlessInteriorWithoutGateDelegate_RequiresLegacyTravelPortal()
        {
            var issues = ValidateFixture(
                WorldTravelMode.SeamlessInterior,
                includeBoundary: true,
                useGateContract: false,
                includeGate: false,
                includeTravelPortal: false);

            AssertIssueCodes(issues, "WORLD_SCENE_TRAVEL_PORTAL_MISSING");
        }

        [Test]
        public void Validate_SeamlessInteriorWithMatchingGate_HasNoPortalOrGateIssue()
        {
            var issues = ValidateFixture(
                WorldTravelMode.SeamlessInterior,
                includeBoundary: true,
                useGateContract: true,
                includeGate: true,
                includeTravelPortal: false);

            Assert.That(issues, Is.Empty);
        }

        [Test]
        public void Validate_SeamlessInteriorWithMissingGate_ReportsGateMissing()
        {
            var issues = ValidateFixture(
                WorldTravelMode.SeamlessInterior,
                includeBoundary: true,
                useGateContract: true,
                includeGate: false,
                includeTravelPortal: false);

            AssertIssueCodes(issues, "WORLD_SCENE_SEAMLESS_INTERIOR_GATE_MISSING");
        }

        [Test]
        public void Validate_SeamlessInteriorWithoutMatchingBoundary_ReportsBoundaryMissing()
        {
            var issues = ValidateFixture(
                WorldTravelMode.SeamlessInterior,
                includeBoundary: false,
                useGateContract: true,
                includeGate: false,
                includeTravelPortal: false);

            AssertIssueCodes(issues, "WORLD_SCENE_SEAMLESS_INTERIOR_BOUNDARY_MISSING");
        }

        [Test]
        public void Validate_PortalTransitionWithGateDelegate_StillRequiresTravelPortal()
        {
            var issues = ValidateFixture(
                WorldTravelMode.PortalTransition,
                includeBoundary: true,
                useGateContract: true,
                includeGate: true,
                includeTravelPortal: false);

            AssertIssueCodes(issues, "WORLD_SCENE_TRAVEL_PORTAL_MISSING");
        }

        private IReadOnlyList<ZeroEngine.World.Authoring.AreaAuthoringIssue> ValidateFixture(
            WorldTravelMode travelMode,
            bool includeBoundary,
            bool useGateContract,
            bool includeGate,
            bool includeTravelPortal)
        {
            var sourceObjectNames = new List<string> { "Anchor_" + SourceAnchorId };
            if (includeGate)
            {
                sourceObjectNames.Add(GetSeamlessInteriorGateName(BoundaryId, SourceAnchorId));
            }

            if (includeTravelPortal)
            {
                sourceObjectNames.Add(GetTravelPortalName(LinkId, SourceAnchorId));
            }

            var sourceScenePath = CreateScene("Outdoor.unity", sourceObjectNames);
            var targetScenePath = CreateScene(
                "Interior.unity",
                new[] { "Anchor_" + TargetAnchorId });
            var graph = CreateGraph(travelMode, includeBoundary);
            var profile = CreateProfile(
                graph,
                sourceScenePath,
                targetScenePath,
                useGateContract
                    ? new Func<string, string, string>(GetSeamlessInteriorGateName)
                    : null);

            return WorldSceneContractValidator.Validate(profile);
        }

        private WorldGraphSO CreateGraph(WorldTravelMode travelMode, bool includeBoundary)
        {
            var sourceAnchor = new WorldAnchorDefinition(
                SourceAnchorId,
                "Outdoor Entry",
                WorldAnchorKind.InteriorEntry,
                Vector3.zero,
                Vector3.forward);
            var targetAnchor = new WorldAnchorDefinition(
                TargetAnchorId,
                "Interior Exit",
                WorldAnchorKind.InteriorExit,
                Vector3.zero,
                Vector3.back);
            var sourceCell = new WorldCellDefinition(
                SourceCellId,
                "Outdoor",
                WorldCellKind.Outdoor,
                "scene.outdoor",
                WorldCellLayer.None,
                1,
                new[] { sourceAnchor },
                includeBoundary
                    ? new[]
                    {
                        new WorldStreamingBoundaryDefinition(
                            BoundaryId,
                            new[] { TargetCellId })
                    }
                    : Array.Empty<WorldStreamingBoundaryDefinition>());
            var targetCell = new WorldCellDefinition(
                TargetCellId,
                "Interior",
                WorldCellKind.Interior,
                "scene.interior",
                WorldCellLayer.None,
                1,
                new[] { targetAnchor },
                Array.Empty<WorldStreamingBoundaryDefinition>());
            var graph = ScriptableObject.CreateInstance<WorldGraphSO>();
            graph.ConfigureForTests(
                "world.test",
                new[]
                {
                    new WorldRegionDefinition(
                        "region.test",
                        "Test Region",
                        new[] { sourceCell, targetCell })
                },
                new[]
                {
                    new WorldTravelLinkDefinition(
                        LinkId,
                        SourceAnchorId,
                        TargetAnchorId,
                        travelMode,
                        bidirectional: false)
                },
                Array.Empty<WorldFastTravelNodeDefinition>());
            AssetDatabase.CreateAsset(graph, _testAssetDirectory + "/WorldGraph.asset");
            AssetDatabase.SaveAssetIfDirty(graph);
            return graph;
        }

        private WorldGraphGraduationProfile CreateProfile(
            WorldGraphSO graph,
            string sourceScenePath,
            string targetScenePath,
            Func<string, string, string> getSeamlessInteriorGateName)
        {
            return new WorldGraphGraduationProfile(
                graph,
                _testAssetDirectory + "/WorldGraph.asset",
                "world.test",
                SourceCellId,
                SourceAnchorId,
                Array.Empty<WorldTravelMode>(),
                Array.Empty<WorldAddressablesGroupContract>(),
                Array.Empty<WorldAddressableAssetContract>(),
                cell => cell.CellId == SourceCellId ? sourceScenePath : targetScenePath,
                getNavigationAssetPath: null,
                getWorldCellRootName: null,
                getLayerRootName: null,
                getLayerReadinessMarkerName: null,
                getTravelPortalName: GetTravelPortalName,
                getStreamingBoundaryName: null,
                geometryContentObjectName: null,
                navigationReadinessSourceScriptGuid: null,
                navigationSourceId: null,
                requireStrictNavigationSceneBinding: false,
                getSeamlessInteriorGateName: getSeamlessInteriorGateName);
        }

        private string CreateScene(string fileName, IEnumerable<string> objectNames)
        {
            var scenePath = _testAssetDirectory + "/" + fileName;
            var names = objectNames.ToArray();
            var sceneText = new StringBuilder();
            sceneText.AppendLine("%YAML 1.1");
            sceneText.AppendLine("%TAG !u! tag:unity3d.com,2011:");
            for (var index = 0; index < names.Length; index++)
            {
                var gameObjectFileId = 1000 + index * 2;
                var transformFileId = gameObjectFileId + 1;
                AppendGameObject(sceneText, gameObjectFileId, transformFileId, names[index]);
                AppendTransform(sceneText, transformFileId, gameObjectFileId);
            }

            sceneText.AppendLine("--- !u!1660057539 &9223372036854775807");
            sceneText.AppendLine("SceneRoots:");
            sceneText.AppendLine("  m_ObjectHideFlags: 0");
            sceneText.AppendLine("  m_Roots:");
            for (var index = 0; index < names.Length; index++)
            {
                sceneText.AppendLine($"  - {{fileID: {1001 + index * 2}}}");
            }

            File.WriteAllText(scenePath, sceneText.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                scenePath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Assert.That(AssetDatabase.AssetPathToGUID(scenePath), Is.Not.Empty);
            return scenePath;
        }

        private static void AppendGameObject(
            StringBuilder sceneText,
            int gameObjectFileId,
            int transformFileId,
            string objectName)
        {
            sceneText.AppendLine($"--- !u!1 &{gameObjectFileId}");
            sceneText.AppendLine("GameObject:");
            sceneText.AppendLine("  m_ObjectHideFlags: 0");
            sceneText.AppendLine("  m_CorrespondingSourceObject: {fileID: 0}");
            sceneText.AppendLine("  m_PrefabInstance: {fileID: 0}");
            sceneText.AppendLine("  m_PrefabAsset: {fileID: 0}");
            sceneText.AppendLine("  serializedVersion: 6");
            sceneText.AppendLine("  m_Component:");
            sceneText.AppendLine($"  - component: {{fileID: {transformFileId}}}");
            sceneText.AppendLine("  m_Layer: 0");
            sceneText.AppendLine($"  m_Name: '{objectName.Replace("'", "''")}'");
            sceneText.AppendLine("  m_TagString: Untagged");
            sceneText.AppendLine("  m_Icon: {fileID: 0}");
            sceneText.AppendLine("  m_NavMeshLayer: 0");
            sceneText.AppendLine("  m_StaticEditorFlags: 0");
            sceneText.AppendLine("  m_IsActive: 1");
        }

        private static void AppendTransform(
            StringBuilder sceneText,
            int transformFileId,
            int gameObjectFileId)
        {
            sceneText.AppendLine($"--- !u!4 &{transformFileId}");
            sceneText.AppendLine("Transform:");
            sceneText.AppendLine("  m_ObjectHideFlags: 0");
            sceneText.AppendLine("  m_CorrespondingSourceObject: {fileID: 0}");
            sceneText.AppendLine("  m_PrefabInstance: {fileID: 0}");
            sceneText.AppendLine("  m_PrefabAsset: {fileID: 0}");
            sceneText.AppendLine($"  m_GameObject: {{fileID: {gameObjectFileId}}}");
            sceneText.AppendLine("  serializedVersion: 2");
            sceneText.AppendLine("  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}");
            sceneText.AppendLine("  m_LocalPosition: {x: 0, y: 0, z: 0}");
            sceneText.AppendLine("  m_LocalScale: {x: 1, y: 1, z: 1}");
            sceneText.AppendLine("  m_ConstrainProportionsScale: 0");
            sceneText.AppendLine("  m_Children: []");
            sceneText.AppendLine("  m_Father: {fileID: 0}");
            sceneText.AppendLine("  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}");
        }

        private static string GetTravelPortalName(string linkId, string anchorId)
        {
            return $"TravelPortal_{linkId}_{anchorId}";
        }

        private static string GetSeamlessInteriorGateName(string boundaryId, string anchorId)
        {
            return $"SeamlessCellGate_{boundaryId}_{anchorId}";
        }

        private static void AssertIssueCodes(
            IEnumerable<ZeroEngine.World.Authoring.AreaAuthoringIssue> issues,
            params string[] expectedCodes)
        {
            Assert.That(
                issues.Select(issue => issue.Code),
                Is.EquivalentTo(expectedCodes));
        }
    }
}
