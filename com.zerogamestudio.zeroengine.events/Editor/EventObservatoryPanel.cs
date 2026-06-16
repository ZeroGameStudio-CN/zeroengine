using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Events.Editor
{
    public sealed class EventObservatoryPanelState
    {
        public string Filter = string.Empty;
        public int RecentEventCount = 16;
        public int SubscriptionCount = 24;
        public bool ExceptionScopesOnly;
        public int SelectedScopeIndex;
        public EventBusDiagnosticsSnapshot Snapshot;
        public string LastExportPath = string.Empty;
    }

    public static class EventObservatoryPanel
    {
        private static readonly GUIContent FilterLabel = new("Filter", "Filters event type, owner type, source id, correlation id, and causation id.");
        private static readonly GUIContent ExceptionOnlyLabel = new("Exception scopes only", "Only show scopes that have subscriber exceptions.");
        private static readonly GUIContent RecentCountLabel = new("Recent", "Number of recent event records to read per scope.");
        private static readonly GUIContent SubscriptionCountLabel = new("Subscriptions", "Number of subscription records to read per scope.");

        public static void Draw(
            EventObservatoryPanelState state,
            Func<EventBusDiagnosticsQuery, EventBusDiagnosticsSnapshot> capture)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (capture == null)
            {
                throw new ArgumentNullException(nameof(capture));
            }

            DrawToolbar(state, capture);
            state.Snapshot ??= Capture(state, capture);
            DrawSnapshot(state);
        }

        private static void DrawToolbar(
            EventObservatoryPanelState state,
            Func<EventBusDiagnosticsQuery, EventBusDiagnosticsSnapshot> capture)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            state.Filter = EditorGUILayout.TextField(FilterLabel, state.Filter ?? string.Empty);
            state.ExceptionScopesOnly = EditorGUILayout.ToggleLeft(ExceptionOnlyLabel, state.ExceptionScopesOnly);
            state.RecentEventCount = Mathf.Clamp(EditorGUILayout.IntField(RecentCountLabel, state.RecentEventCount), 0, 128);
            state.SubscriptionCount = Mathf.Clamp(EditorGUILayout.IntField(SubscriptionCountLabel, state.SubscriptionCount), 0, 256);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Height(24)))
            {
                state.Snapshot = Capture(state, capture);
            }

            using (new EditorGUI.DisabledScope(state.Snapshot == null))
            {
                if (GUILayout.Button("Copy Text", GUILayout.Height(24)))
                {
                    EditorGUIUtility.systemCopyBuffer = EventBusDiagnosticsTextFormatter.Format(state.Snapshot);
                }

                if (GUILayout.Button("Export JSON", GUILayout.Height(24)))
                {
                    ExportJson(state);
                }
            }

            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(state.LastExportPath))
            {
                EditorGUILayout.LabelField("Last export", state.LastExportPath, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private static EventBusDiagnosticsSnapshot Capture(
            EventObservatoryPanelState state,
            Func<EventBusDiagnosticsQuery, EventBusDiagnosticsSnapshot> capture)
        {
            var query = new EventBusDiagnosticsQuery(
                state.RecentEventCount,
                state.SubscriptionCount,
                state.Filter,
                state.ExceptionScopesOnly);
            return capture.Invoke(query);
        }

        private static void DrawSnapshot(EventObservatoryPanelState state)
        {
            var snapshot = state.Snapshot;
            if (snapshot == null)
            {
                EditorGUILayout.HelpBox("No event diagnostics snapshot.", MessageType.Info);
                return;
            }

            if (snapshot.Scopes.Count == 0)
            {
                EditorGUILayout.HelpBox("No scopes matched the current filters.", MessageType.Info);
                return;
            }

            var names = new[] { "All" }.Concat(snapshot.Scopes.Select(scope => scope.Name)).ToArray();
            state.SelectedScopeIndex = Mathf.Clamp(state.SelectedScopeIndex, 0, names.Length - 1);
            state.SelectedScopeIndex = EditorGUILayout.Popup("Scope", state.SelectedScopeIndex, names);

            if (state.SelectedScopeIndex == 0)
            {
                for (var i = 0; i < snapshot.Scopes.Count; i++)
                {
                    DrawScope(snapshot.Scopes[i]);
                }
            }
            else
            {
                DrawScope(snapshot.Scopes[state.SelectedScopeIndex - 1]);
            }
        }

        private static void DrawScope(EventBusScopeDiagnosticsSnapshot scope)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var title = scope.IsCreated
                ? $"{scope.Name}  published={scope.TotalPublished}  exceptions={scope.TotalSubscriberExceptions}  subscriptions={scope.TotalSubscriptions}"
                : $"{scope.Name}  not-created";
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            if (!scope.IsCreated)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            if (scope.TotalSubscriberExceptions > 0)
            {
                EditorGUILayout.HelpBox($"Subscriber exceptions: {scope.TotalSubscriberExceptions}", MessageType.Warning);
            }

            DrawSubscriptions(scope);
            DrawRecentEvents(scope);
            EditorGUILayout.EndVertical();
        }

        private static void DrawSubscriptions(EventBusScopeDiagnosticsSnapshot scope)
        {
            EditorGUILayout.LabelField("Subscriptions", EditorStyles.miniBoldLabel);
            if (scope.Subscriptions.Count == 0)
            {
                EditorGUILayout.LabelField("None", EditorStyles.miniLabel);
                return;
            }

            for (var i = 0; i < scope.Subscriptions.Count; i++)
            {
                var subscription = scope.Subscriptions[i];
                var owner = string.IsNullOrEmpty(subscription.OwnerTypeName) ? "none" : subscription.OwnerTypeName;
                EditorGUILayout.LabelField(
                    subscription.EventTypeName,
                    $"owner={owner}, priority={subscription.Priority}",
                    EditorStyles.miniLabel);
            }
        }

        private static void DrawRecentEvents(EventBusScopeDiagnosticsSnapshot scope)
        {
            EditorGUILayout.LabelField("Recent Events", EditorStyles.miniBoldLabel);
            if (scope.RecentEvents.Count == 0)
            {
                EditorGUILayout.LabelField("None", EditorStyles.miniLabel);
                return;
            }

            for (var i = 0; i < scope.RecentEvents.Count; i++)
            {
                var recent = scope.RecentEvents[i];
                var source = string.IsNullOrEmpty(recent.SourceId) ? "none" : recent.SourceId;
                EditorGUILayout.LabelField(
                    $"{recent.Sequence}: {recent.EventTypeName}",
                    $"source={source}, correlation={recent.CorrelationId}",
                    EditorStyles.miniLabel);
            }
        }

        private static void ExportJson(EventObservatoryPanelState state)
        {
            var path = EditorUtility.SaveFilePanel(
                "Export Event Diagnostics",
                string.Empty,
                "event-diagnostics.json",
                "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            File.WriteAllText(path, EventBusDiagnosticsJsonExporter.ToJson(state.Snapshot));
            state.LastExportPath = path;
        }
    }
}
