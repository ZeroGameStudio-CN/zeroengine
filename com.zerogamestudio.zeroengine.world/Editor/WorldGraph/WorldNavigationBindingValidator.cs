using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine.AI;
using ZeroEngine.World.Authoring;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public static class WorldNavigationBindingValidator
    {
        public static IReadOnlyList<AreaAuthoringIssue> Validate(WorldGraphGraduationProfile profile)
        {
            if (profile?.Graph == null || profile.GetNavigationAssetPath == null)
            {
                return Array.Empty<AreaAuthoringIssue>();
            }

            var issues = new List<AreaAuthoringIssue>();
            foreach (var cell in WorldGraphGraduationRunner.EnumerateCells(profile.Graph))
            {
                var navMeshPath = profile.GetNavigationAssetPath(cell);
                var navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(navMeshPath);
                if (navMeshData == null)
                {
                    issues.Add(WorldGraphGraduationRunner.Error(
                        "WORLD_NAVIGATION_ASSET_MISSING",
                        $"Baked NavMeshData is missing for {cell.CellId}: {navMeshPath}.",
                        navMeshPath,
                        cell.CellId));
                    continue;
                }

                if (profile.RequireStrictNavigationSceneBinding)
                {
                    ValidateSceneBinding(profile, cell.CellId, navMeshPath, issues);
                }
            }

            return issues;
        }

        private static void ValidateSceneBinding(
            WorldGraphGraduationProfile profile,
            string cellId,
            string navMeshPath,
            ICollection<AreaAuthoringIssue> issues)
        {
            if (profile.GetCellScenePath == null)
            {
                return;
            }

            var scenePath = profile.GetCellScenePath(profile.Graph.FindCell(cellId));
            if (string.IsNullOrWhiteSpace(scenePath) || !File.Exists(scenePath))
            {
                issues.Add(WorldGraphGraduationRunner.Error(
                    "WORLD_NAVIGATION_SCENE_MISSING",
                    $"World cell scene is missing for navigation binding validation: {scenePath}.",
                    scenePath,
                    cellId));
                return;
            }

            var navMeshGuid = AssetDatabase.AssetPathToGUID(navMeshPath);
            if (string.IsNullOrWhiteSpace(navMeshGuid))
            {
                issues.Add(WorldGraphGraduationRunner.Error(
                    "WORLD_NAVIGATION_ASSET_GUID_MISSING",
                    $"Baked NavMeshData guid is missing for {cellId}: {navMeshPath}.",
                    navMeshPath,
                    cellId));
                return;
            }

            var sceneText = File.ReadAllText(scenePath);
            var blocks = AreaAuthoringYamlScanner.ExtractComponentBlocks(sceneText, profile.NavigationReadinessSourceScriptGuid);
            foreach (var block in blocks)
            {
                if (block.Contains("m_Enabled: 1")
                    && block.Contains("_sourceId: " + profile.NavigationSourceId)
                    && block.Contains("_requiresBakedNavMeshData: 1")
                    && block.Contains("guid: " + navMeshGuid))
                {
                    return;
                }
            }

            issues.Add(WorldGraphGraduationRunner.Error(
                "WORLD_NAVIGATION_SCENE_BINDING_MISSING",
                $"World cell scene must bind baked NavMeshData {navMeshPath}.",
                scenePath,
                cellId));
        }
    }
}
