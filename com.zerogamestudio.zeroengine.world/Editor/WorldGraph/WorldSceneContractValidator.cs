using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using ZeroEngine.World.Authoring;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public static class WorldSceneContractValidator
    {
        private static readonly WorldCellLayer[] SceneLayerRoots =
        {
            WorldCellLayer.Geometry,
            WorldCellLayer.Collision,
            WorldCellLayer.Navigation,
            WorldCellLayer.GameplayMarkers,
            WorldCellLayer.LightingAndVolumes,
            WorldCellLayer.Audio
        };

        public static IReadOnlyList<AreaAuthoringIssue> Validate(WorldGraphGraduationProfile profile)
        {
            if (profile?.Graph == null || profile.GetCellScenePath == null)
            {
                return Array.Empty<AreaAuthoringIssue>();
            }

            var issues = new List<AreaAuthoringIssue>();
            foreach (var cell in WorldGraphGraduationRunner.EnumerateCells(profile.Graph))
            {
                ValidateCellScene(profile, cell, issues);
            }

            return issues;
        }

        private static void ValidateCellScene(
            WorldGraphGraduationProfile profile,
            WorldCellDefinition cell,
            ICollection<AreaAuthoringIssue> issues)
        {
            var scenePath = profile.GetCellScenePath(cell);
            if (string.IsNullOrWhiteSpace(scenePath) || !File.Exists(scenePath))
            {
                issues.Add(WorldGraphGraduationRunner.Error(
                    "WORLD_SCENE_MISSING",
                    $"World cell scene is missing for {cell.CellId}: {scenePath}.",
                    scenePath,
                    cell.CellId));
                return;
            }

            if (string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(scenePath)))
            {
                issues.Add(WorldGraphGraduationRunner.Error(
                    "WORLD_SCENE_GUID_MISSING",
                    $"World cell scene is not imported: {scenePath}.",
                    scenePath,
                    cell.CellId));
            }

            var sceneText = File.ReadAllText(scenePath).Replace("\r\n", "\n");
            ValidateNamedObject(
                sceneText,
                profile.GetWorldCellRootName?.Invoke(cell.CellId),
                "WORLD_SCENE_ROOT_MISSING",
                "World cell root is missing.",
                scenePath,
                cell.CellId,
                issues);
            ValidateLayerRoots(profile, cell, sceneText, scenePath, issues);
            ValidateAnchors(cell, sceneText, scenePath, issues);
            ValidateTravelPortals(profile, cell, sceneText, scenePath, issues);
            ValidateStreamingBoundaries(profile, cell, sceneText, scenePath, issues);
            ValidateGeometryBinding(profile, sceneText, scenePath, cell.CellId, issues);
        }

        private static void ValidateLayerRoots(
            WorldGraphGraduationProfile profile,
            WorldCellDefinition cell,
            string sceneText,
            string scenePath,
            ICollection<AreaAuthoringIssue> issues)
        {
            foreach (var layer in SceneLayerRoots)
            {
                if ((cell.Layers & layer) == WorldCellLayer.None)
                {
                    continue;
                }

                ValidateNamedObject(
                    sceneText,
                    profile.GetLayerRootName?.Invoke(layer),
                    "WORLD_SCENE_LAYER_ROOT_MISSING",
                    $"World cell layer root is missing for {layer}.",
                    scenePath,
                    cell.CellId,
                    issues);
                ValidateNamedObject(
                    sceneText,
                    profile.GetLayerReadinessMarkerName?.Invoke(layer),
                    "WORLD_SCENE_LAYER_READINESS_MARKER_MISSING",
                    $"World cell layer readiness marker is missing for {layer}.",
                    scenePath,
                    cell.CellId,
                    issues);
            }
        }

        private static void ValidateAnchors(
            WorldCellDefinition cell,
            string sceneText,
            string scenePath,
            ICollection<AreaAuthoringIssue> issues)
        {
            foreach (var anchor in cell.Anchors.Where(anchor => anchor != null))
            {
                ValidateNamedObject(
                    sceneText,
                    "Anchor_" + anchor.AnchorId,
                    "WORLD_SCENE_ANCHOR_MISSING",
                    $"World cell scene anchor is missing: {anchor.AnchorId}.",
                    scenePath,
                    anchor.AnchorId,
                    issues);
            }
        }

        private static void ValidateTravelPortals(
            WorldGraphGraduationProfile profile,
            WorldCellDefinition cell,
            string sceneText,
            string scenePath,
            ICollection<AreaAuthoringIssue> issues)
        {
            if (profile.GetTravelPortalName == null)
            {
                return;
            }

            var anchors = new HashSet<string>(cell.Anchors.Where(anchor => anchor != null).Select(anchor => anchor.AnchorId));
            foreach (var link in profile.Graph.TravelLinks.Where(link => link != null && ShouldCreateSceneTravelPortal(link.TravelMode)))
            {
                if (anchors.Contains(link.FromAnchorId))
                {
                    ValidateNamedObject(
                        sceneText,
                        profile.GetTravelPortalName(link.LinkId, link.FromAnchorId),
                        "WORLD_SCENE_TRAVEL_PORTAL_MISSING",
                        $"World cell scene travel portal is missing for link {link.LinkId}.",
                        scenePath,
                        link.LinkId,
                        issues);
                }

                if (link.Bidirectional && anchors.Contains(link.ToAnchorId))
                {
                    ValidateNamedObject(
                        sceneText,
                        profile.GetTravelPortalName(link.LinkId, link.ToAnchorId),
                        "WORLD_SCENE_TRAVEL_PORTAL_MISSING",
                        $"World cell scene travel portal is missing for bidirectional link {link.LinkId}.",
                        scenePath,
                        link.LinkId,
                        issues);
                }
            }
        }

        private static void ValidateStreamingBoundaries(
            WorldGraphGraduationProfile profile,
            WorldCellDefinition cell,
            string sceneText,
            string scenePath,
            ICollection<AreaAuthoringIssue> issues)
        {
            if (profile.GetStreamingBoundaryName == null)
            {
                return;
            }

            var anchors = new HashSet<string>(cell.Anchors.Where(anchor => anchor != null).Select(anchor => anchor.AnchorId));
            foreach (var link in profile.Graph.TravelLinks.Where(link => link != null && link.TravelMode == WorldTravelMode.SeamlessWalk))
            {
                if (anchors.Contains(link.FromAnchorId)
                    && WorldGraphGraduationRunner.TryFindCellContainingAnchor(profile.Graph, link.ToAnchorId, out var targetCell)
                    && TryFindStreamingBoundaryId(cell, targetCell.CellId, out var boundaryId))
                {
                    ValidateNamedObject(
                        sceneText,
                        profile.GetStreamingBoundaryName(boundaryId, link.FromAnchorId),
                        "WORLD_SCENE_STREAMING_BOUNDARY_MISSING",
                        $"World cell scene streaming boundary is missing for {boundaryId}.",
                        scenePath,
                        boundaryId,
                        issues);
                }

                if (link.Bidirectional
                    && anchors.Contains(link.ToAnchorId)
                    && WorldGraphGraduationRunner.TryFindCellContainingAnchor(profile.Graph, link.FromAnchorId, out var reverseTargetCell)
                    && TryFindStreamingBoundaryId(cell, reverseTargetCell.CellId, out var reverseBoundaryId))
                {
                    ValidateNamedObject(
                        sceneText,
                        profile.GetStreamingBoundaryName(reverseBoundaryId, link.ToAnchorId),
                        "WORLD_SCENE_STREAMING_BOUNDARY_MISSING",
                        $"World cell scene streaming boundary is missing for {reverseBoundaryId}.",
                        scenePath,
                        reverseBoundaryId,
                        issues);
                }
            }
        }

        private static void ValidateGeometryBinding(
            WorldGraphGraduationProfile profile,
            string sceneText,
            string scenePath,
            string cellId,
            ICollection<AreaAuthoringIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(profile.GeometryContentObjectName))
            {
                return;
            }

            if (TryFindNamedGameObjectFileId(sceneText, profile.GeometryContentObjectName, out var gameObjectFileId)
                && ComponentReferencesGameObject(sceneText, "MeshFilter:", gameObjectFileId)
                && ComponentReferencesGameObject(sceneText, "MeshRenderer:", gameObjectFileId, requireEnabled: true))
            {
                return;
            }

            issues.Add(WorldGraphGraduationRunner.Error(
                "WORLD_SCENE_GEOMETRY_CONTENT_INVALID",
                $"World cell scene must include {profile.GeometryContentObjectName} with MeshFilter and enabled MeshRenderer.",
                scenePath,
                cellId));
        }

        private static void ValidateNamedObject(
            string sceneText,
            string objectName,
            string code,
            string message,
            string scenePath,
            string contextId,
            ICollection<AreaAuthoringIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(objectName) || SceneContainsNamedObject(sceneText, objectName))
            {
                return;
            }

            issues.Add(WorldGraphGraduationRunner.Error(code, $"{message} Expected object name: {objectName}.", scenePath, contextId));
        }

        private static bool SceneContainsNamedObject(string sceneText, string objectName)
        {
            return !string.IsNullOrWhiteSpace(sceneText)
                   && !string.IsNullOrWhiteSpace(objectName)
                   && sceneText.Contains("m_Name: " + objectName + "\n");
        }

        private static bool ShouldCreateSceneTravelPortal(WorldTravelMode travelMode)
        {
            return travelMode == WorldTravelMode.SeamlessInterior
                   || travelMode == WorldTravelMode.PortalTransition;
        }

        private static bool TryFindStreamingBoundaryId(
            WorldCellDefinition sourceCell,
            string targetCellId,
            out string boundaryId)
        {
            boundaryId = null;
            foreach (var boundary in sourceCell.StreamingBoundaries.Where(boundary => boundary != null))
            {
                if (boundary.TargetCellIds.Contains(targetCellId))
                {
                    boundaryId = boundary.BoundaryId;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindNamedGameObjectFileId(
            string sceneText,
            string objectName,
            out string fileId)
        {
            fileId = string.Empty;
            var nameMarker = "m_Name: " + objectName + "\n";
            var nameIndex = sceneText.IndexOf(nameMarker, StringComparison.Ordinal);
            while (nameIndex >= 0)
            {
                var blockStart = FindYamlBlockStart(sceneText, nameIndex);
                var blockEnd = FindYamlBlockEnd(sceneText, nameIndex + nameMarker.Length);
                var block = sceneText.Substring(blockStart, blockEnd - blockStart);
                if (block.Contains("GameObject:") && TryReadYamlFileId(block, out fileId))
                {
                    return true;
                }

                nameIndex = sceneText.IndexOf(nameMarker, nameIndex + nameMarker.Length, StringComparison.Ordinal);
            }

            return false;
        }

        private static bool ComponentReferencesGameObject(
            string sceneText,
            string componentHeader,
            string gameObjectFileId,
            bool requireEnabled = false)
        {
            var componentIndex = sceneText.IndexOf(componentHeader, StringComparison.Ordinal);
            while (componentIndex >= 0)
            {
                var blockStart = FindYamlBlockStart(sceneText, componentIndex);
                var blockEnd = FindYamlBlockEnd(sceneText, componentIndex + componentHeader.Length);
                var componentBlock = sceneText.Substring(blockStart, blockEnd - blockStart);
                if (componentBlock.Contains("m_GameObject: {fileID: " + gameObjectFileId)
                    && (!requireEnabled || componentBlock.Contains("m_Enabled: 1")))
                {
                    return true;
                }

                componentIndex = sceneText.IndexOf(componentHeader, componentIndex + componentHeader.Length, StringComparison.Ordinal);
            }

            return false;
        }

        private static bool TryReadYamlFileId(string block, out string fileId)
        {
            fileId = string.Empty;
            var firstLineEnd = block.IndexOf('\n');
            var headerLine = firstLineEnd < 0 ? block : block.Substring(0, firstLineEnd);
            var fileIdIndex = headerLine.LastIndexOf("&", StringComparison.Ordinal);
            if (fileIdIndex < 0 || fileIdIndex == headerLine.Length - 1)
            {
                return false;
            }

            fileId = headerLine.Substring(fileIdIndex + 1).Trim();
            return !string.IsNullOrWhiteSpace(fileId);
        }

        private static int FindYamlBlockStart(string sceneText, int markerIndex)
        {
            var blockStart = sceneText.LastIndexOf("\n--- !u!", markerIndex, StringComparison.Ordinal);
            return blockStart < 0 ? 0 : blockStart + 1;
        }

        private static int FindYamlBlockEnd(string sceneText, int searchStart)
        {
            var blockEnd = sceneText.IndexOf("\n--- !u!", searchStart, StringComparison.Ordinal);
            return blockEnd < 0 ? sceneText.Length : blockEnd;
        }
    }
}
