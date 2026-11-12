using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZeroEngine.World.Authoring;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public sealed class WorldGraphWorkbenchSession
    {
        private readonly List<WorldGraphWorkbenchAction> _customActions;
        private readonly List<WorldGraphWorkbenchRunRecord> _history = new List<WorldGraphWorkbenchRunRecord>();
        private IReadOnlyList<AreaAuthoringIssue> _currentIssues = Array.Empty<AreaAuthoringIssue>();

        public WorldGraphWorkbenchSession(
            string title,
            WorldGraphGraduationProfile profile,
            IEnumerable<WorldGraphWorkbenchAction> customActions = null)
        {
            Title = string.IsNullOrWhiteSpace(title) ? "WorldGraph Workbench" : title.Trim();
            Profile = profile;
            _customActions = customActions?.ToList() ?? new List<WorldGraphWorkbenchAction>();
            CurrentSnapshot = WorldGraphWorkbenchModel.CreateSnapshot(
                Title,
                Profile,
                _currentIssues,
                _customActions,
                _history);
        }

        public string Title { get; }
        public WorldGraphGraduationProfile Profile { get; }
        public WorldGraphWorkbenchSnapshot CurrentSnapshot { get; private set; }

        public WorldGraphWorkbenchSnapshot Refresh()
        {
            CurrentSnapshot = WorldGraphWorkbenchModel.CreateSnapshot(
                Title,
                Profile,
                _currentIssues,
                _customActions,
                _history);
            return CurrentSnapshot;
        }

        public WorldGraphWorkbenchSnapshot RunValidation()
        {
            return Run(WorldGraphWorkbenchRunMode.Validation);
        }

        public WorldGraphWorkbenchSnapshot RunGraduation()
        {
            return Run(WorldGraphWorkbenchRunMode.Graduation);
        }

        public bool TryExecuteAction(string actionId, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(actionId))
            {
                error = "Action id is missing.";
                return false;
            }

            if (actionId == WorldGraphWorkbenchModel.RunValidationActionId)
            {
                RunValidation();
                return true;
            }

            if (actionId == WorldGraphWorkbenchModel.RunGraduationActionId)
            {
                RunGraduation();
                return true;
            }

            if (actionId == WorldGraphWorkbenchModel.OpenGraphActionId)
            {
                return TryOpenAsset(Profile?.GraphAssetPath, out error);
            }

            if (actionId.StartsWith(WorldGraphWorkbenchModel.OpenSceneActionPrefix, StringComparison.Ordinal))
            {
                return TryOpenScene(FindActionTargetPath(actionId), out error);
            }

            if (actionId.StartsWith(WorldGraphWorkbenchModel.OpenNavigationActionPrefix, StringComparison.Ordinal))
            {
                return TryOpenAsset(FindActionTargetPath(actionId), out error);
            }

            var customAction = _customActions.FirstOrDefault(action => action.ActionId == actionId);
            if (customAction == null)
            {
                error = "Unknown Workbench action: " + actionId;
                return false;
            }

            var executed = customAction.Execute(out error);
            Refresh();
            return executed;
        }

        private WorldGraphWorkbenchSnapshot Run(WorldGraphWorkbenchRunMode mode)
        {
            _currentIssues = WorldGraphGraduationRunner.Validate(Profile);
            _history.Insert(
                0,
                new WorldGraphWorkbenchRunRecord(
                    mode,
                    DateTime.UtcNow,
                    _currentIssues.Count,
                    _currentIssues.Count(issue => issue.IsError)));
            Refresh();
            return CurrentSnapshot;
        }

        private string FindActionTargetPath(string actionId)
        {
            var action = CurrentSnapshot.Actions.FirstOrDefault(descriptor => descriptor.ActionId == actionId);
            return action.TargetPath;
        }

        private static bool TryOpenAsset(string assetPath, out string error)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                error = "Asset path is missing.";
                return false;
            }

            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
            {
                error = "Asset not found: " + assetPath;
                return false;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            error = string.Empty;
            return true;
        }

        private static bool TryOpenScene(string scenePath, out string error)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                error = "Scene path is missing.";
                return false;
            }

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (sceneAsset == null)
            {
                error = "Scene not found: " + scenePath;
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                error = "Open scene was cancelled.";
                return false;
            }

            EditorSceneManager.OpenScene(scenePath);
            error = string.Empty;
            return true;
        }
    }
}
