using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataToolkitWindow : EditorWindow
    {
        private const float DefaultTypeColumnWidth = 190f;
        private const float DefaultAssetColumnWidth = 240f;
        private const float MinColumnWidth = 160f;
        private const float MaxColumnWidth = 520f;
        private const float SplitterWidth = 5f;
        private const float RowHeight = 24f;

        private readonly CompositeAssetInspector inspector = new();
        private readonly Dictionary<Type, int> assetCountCache = new();
        private readonly Queue<Type> pendingCountTypes = new();

        private DataToolkitContext context;
        private IReadOnlyList<IDataToolkitToolbarProvider> toolbarProviders = Array.Empty<IDataToolkitToolbarProvider>();
        private Type[] typesToDisplay = Array.Empty<Type>();
        private Type selectedType;
        private string selectedAssetPath;
        private UnityEngine.Object selectedAsset;
        private string typeSearch = string.Empty;
        private string assetSearch = string.Empty;
        private Vector2 typeColumnScroll;
        private Vector2 assetColumnScroll;
        private Vector2 inspectorScroll;
        private float typeColumnWidth = DefaultTypeColumnWidth;
        private float assetColumnWidth = DefaultAssetColumnWidth;
        private string activeResizeKey;
        private bool isWarmingAssetCounts;

        public static DataToolkitWindow Open(DataToolkitProjectSettings settings)
        {
            return Open(new DataToolkitProjectProfile(settings));
        }

        public static DataToolkitWindow Open(
            DataToolkitProjectSettings settings,
            params IDataToolkitToolbarProvider[] toolbarProviders)
        {
            return Open(new DataToolkitProjectProfile(settings, toolbarProviders));
        }

        public static DataToolkitWindow Open(DataToolkitProjectProfile profile)
        {
            var window = GetWindow<DataToolkitWindow>();
            window.Initialize(profile);
            window.Show();
            return window;
        }

        private void Initialize(DataToolkitProjectProfile profile)
        {
            profile ??= DataToolkitProjectRegistry.CreateDefaultProfile();
            var settings = profile.Settings;
            context = new DataToolkitContext(settings);
            toolbarProviders = profile.ToolbarProviders;
            titleContent = new GUIContent(settings.WindowTitle);
            minSize = new Vector2(980f, 560f);
            typeColumnWidth = Mathf.Clamp(EditorPrefs.GetFloat(settings.PrefKey("TypeColumnWidth"), DefaultTypeColumnWidth), MinColumnWidth, MaxColumnWidth);
            assetColumnWidth = Mathf.Clamp(EditorPrefs.GetFloat(settings.PrefKey("AssetColumnWidth"), DefaultAssetColumnWidth), MinColumnWidth, MaxColumnWidth);
            typesToDisplay = ManageableDataTypeDiscovery.GetManageableScriptableObjectTypes().ToArray();
            EnsureSelectedType();
            StartAssetCountWarmup();
        }

        private void OnEnable()
        {
            DataToolkitProjectRegistry.DefaultProfileRegistered += RestoreDefaultProfileIfUsingGenericFallback;

            if (context == null)
            {
                Initialize(DataToolkitProjectRegistry.CreateDefaultProfile());
            }
        }

        private void OnDisable()
        {
            if (context != null)
            {
                EditorPrefs.SetFloat(context.Settings.PrefKey("TypeColumnWidth"), Mathf.Clamp(typeColumnWidth, MinColumnWidth, MaxColumnWidth));
                EditorPrefs.SetFloat(context.Settings.PrefKey("AssetColumnWidth"), Mathf.Clamp(assetColumnWidth, MinColumnWidth, MaxColumnWidth));
            }

            inspector.Dispose();
            StopAssetCountWarmup();
            DataToolkitProjectRegistry.DefaultProfileRegistered -= RestoreDefaultProfileIfUsingGenericFallback;
        }

        private void OnProjectChange()
        {
            RefreshCaches();
        }

        private void OnGUI()
        {
            EnsureContext();
            DrawHeaderToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawTypeColumn();
            DrawColumnResizeHandle(ref typeColumnWidth, context.Settings.PrefKey("TypeColumnWidth"), position.width - assetColumnWidth - 360f);
            DrawAssetColumn();
            DrawColumnResizeHandle(ref assetColumnWidth, context.Settings.PrefKey("AssetColumnWidth"), position.width - typeColumnWidth - 360f);
            DrawSelectedAssetInspector();
            EditorGUILayout.EndHorizontal();
        }

        private void EnsureContext()
        {
            if (context == null)
            {
                Initialize(DataToolkitProjectRegistry.CreateDefaultProfile());
            }
        }

        private void RestoreDefaultProfileIfUsingGenericFallback()
        {
            if (context != null && context.Settings.ProjectId != "ZGS")
            {
                return;
            }

            Initialize(DataToolkitProjectRegistry.CreateDefaultProfile());
            Repaint();
        }

        private void DrawHeaderToolbar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(context.Settings.WindowTitle, EditorStyles.boldLabel, GUILayout.MinWidth(160f));
                GUILayout.Label(BuildAssetSummaryText(), EditorStyles.miniLabel, GUILayout.Width(220f));
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Refresh", GUILayout.Width(82f), GUILayout.Height(24f)))
                {
                    RefreshCaches();
                }

                EditorGUILayout.EndHorizontal();
                DrawProjectToolbars();
            }
        }

        private void DrawProjectToolbars()
        {
            foreach (var toolbarProvider in toolbarProviders)
            {
                if (toolbarProvider == null)
                {
                    continue;
                }

                try
                {
                    if (!toolbarProvider.IsVisible(context))
                    {
                        continue;
                    }

                    toolbarProvider.DrawToolbar(context);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void DrawTypeColumn()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(typeColumnWidth), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField("Data Types", EditorStyles.boldLabel);
                typeSearch = EditorGUILayout.TextField(typeSearch, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarSearchField);

                typeColumnScroll = EditorGUILayout.BeginScrollView(typeColumnScroll);
                foreach (var type in typesToDisplay.Where(IsTypeVisible))
                {
                    if (DrawSelectableRow(type.Name, GetAssetCountLabel(type), type == selectedType))
                    {
                        SelectType(type);
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawAssetColumn()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(assetColumnWidth), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField(selectedType == null ? "Assets" : selectedType.Name, EditorStyles.boldLabel);
                assetSearch = EditorGUILayout.TextField(assetSearch, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarSearchField);

                assetColumnScroll = EditorGUILayout.BeginScrollView(assetColumnScroll);
                foreach (var assetPath in AssetDiscoveryService.GetAssetPathsForType(selectedType, context.Settings).Where(IsAssetVisible))
                {
                    if (DrawSelectableRow(Path.GetFileNameWithoutExtension(assetPath), null, assetPath == selectedAssetPath))
                    {
                        SelectAssetByPath(assetPath);
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSelectedAssetInspector()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                if (selectedAsset == null)
                {
                    EditorGUILayout.HelpBox("Select a data asset from the middle column.", MessageType.Info);
                    return;
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(selectedAsset.name, EditorStyles.boldLabel);
                if (GUILayout.Button("Ping", GUILayout.Width(64f), GUILayout.Height(22f)))
                {
                    EditorGUIUtility.PingObject(selectedAsset);
                }

                EditorGUILayout.EndHorizontal();

                inspector.SetTarget(selectedAsset);
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                EditorGUI.BeginChangeCheck();
                inspector.Draw();
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(selectedAsset);
                    Repaint();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private bool DrawSelectableRow(string title, string countText, bool selected)
        {
            var rect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
            var currentEvent = Event.current;
            var hovered = rect.Contains(currentEvent.mousePosition);

            if (currentEvent.type == EventType.Repaint)
            {
                if (selected)
                {
                    EditorGUI.DrawRect(rect, new Color(0.24f, 0.42f, 0.72f, 0.35f));
                }
                else if (hovered)
                {
                    EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.06f));
                }

                var titleRect = new Rect(rect.x + 6f, rect.y + 2f, rect.width - 56f, rect.height - 4f);
                var titleStyle = new GUIStyle(EditorStyles.label)
                {
                    clipping = TextClipping.Clip,
                    alignment = TextAnchor.MiddleLeft
                };
                titleStyle.normal.textColor = selected ? Color.white : EditorStyles.label.normal.textColor;
                GUI.Label(titleRect, title, titleStyle);

                if (!string.IsNullOrEmpty(countText))
                {
                    var countRect = new Rect(rect.xMax - 46f, rect.y + 2f, 40f, rect.height - 4f);
                    var countStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight
                    };
                    countStyle.normal.textColor = selected ? new Color(0.9f, 0.95f, 1f, 1f) : EditorStyles.miniLabel.normal.textColor;
                    GUI.Label(countRect, countText, countStyle);
                }
            }

            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private void DrawColumnResizeHandle(ref float width, string prefsKey, float maxWidthByLayout)
        {
            var rect = GUILayoutUtility.GetRect(SplitterWidth, SplitterWidth, GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(new Rect(rect.x + 2f, rect.y, 1f, rect.height), new Color(0.28f, 0.28f, 0.28f, 1f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            var currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown && rect.Contains(currentEvent.mousePosition))
            {
                activeResizeKey = prefsKey;
                currentEvent.Use();
            }

            if (activeResizeKey == prefsKey && currentEvent.type == EventType.MouseDrag)
            {
                var maxWidth = Mathf.Max(MinColumnWidth, Mathf.Min(MaxColumnWidth, maxWidthByLayout));
                width = Mathf.Clamp(width + currentEvent.delta.x, MinColumnWidth, maxWidth);
                Repaint();
                currentEvent.Use();
            }

            if (activeResizeKey == prefsKey && currentEvent.rawType == EventType.MouseUp)
            {
                activeResizeKey = null;
                EditorPrefs.SetFloat(prefsKey, Mathf.Clamp(width, MinColumnWidth, MaxColumnWidth));
                currentEvent.Use();
            }
        }

        private void EnsureSelectedType()
        {
            if (selectedType != null || typesToDisplay.Length == 0)
            {
                return;
            }

            SelectType(typesToDisplay[0]);
        }

        private void SelectType(Type type)
        {
            selectedType = type;
            assetColumnScroll = Vector2.zero;
            assetSearch = string.Empty;
            selectedAssetPath = null;
            SelectAsset(null);
        }

        private void SelectAssetByPath(string assetPath)
        {
            if (selectedAssetPath == assetPath)
            {
                return;
            }

            selectedAssetPath = assetPath;
            SelectAsset(string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath));
        }

        private void SelectAsset(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                selectedAssetPath = null;
            }

            if (selectedAsset == asset)
            {
                return;
            }

            selectedAsset = asset;
            Selection.activeObject = asset;
            inspectorScroll = Vector2.zero;
            inspector.SetTarget(asset);
        }

        private bool IsTypeVisible(Type type)
        {
            return string.IsNullOrWhiteSpace(typeSearch) ||
                   type.Name.IndexOf(typeSearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsAssetVisible(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) &&
                   (string.IsNullOrWhiteSpace(assetSearch) ||
                    Path.GetFileNameWithoutExtension(assetPath).IndexOf(assetSearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private string BuildAssetSummaryText()
        {
            var countedAssets = assetCountCache.Values.Sum();
            var suffix = assetCountCache.Count < typesToDisplay.Length ? "+" : string.Empty;
            return $"{typesToDisplay.Length} types / {countedAssets}{suffix} assets";
        }

        private string GetAssetCountLabel(Type type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            if (type == selectedType)
            {
                return CountAssetsForType(type).ToString();
            }

            return assetCountCache.TryGetValue(type, out var count) ? count.ToString() : "...";
        }

        private void RefreshCaches()
        {
            ManageableDataTypeDiscovery.ClearCache();
            AssetDiscoveryService.ClearCaches();
            assetCountCache.Clear();
            pendingCountTypes.Clear();
            typesToDisplay = ManageableDataTypeDiscovery.GetManageableScriptableObjectTypes().ToArray();
            if (selectedType != null && !typesToDisplay.Contains(selectedType))
            {
                SelectType(null);
            }

            EnsureSelectedType();
            StartAssetCountWarmup();
            Repaint();
        }

        private int CountAssetsForType(Type type)
        {
            if (type == null)
            {
                return 0;
            }

            if (assetCountCache.TryGetValue(type, out var count))
            {
                return count;
            }

            count = AssetDiscoveryService.GetAssetPathsForType(type, context.Settings).Length;
            assetCountCache[type] = count;
            return count;
        }

        private void StartAssetCountWarmup()
        {
            pendingCountTypes.Clear();
            foreach (var type in typesToDisplay)
            {
                if (type != null && !assetCountCache.ContainsKey(type))
                {
                    pendingCountTypes.Enqueue(type);
                }
            }

            if (pendingCountTypes.Count == 0)
            {
                StopAssetCountWarmup();
                return;
            }

            if (isWarmingAssetCounts)
            {
                return;
            }

            EditorApplication.update += WarmAssetCountsStep;
            isWarmingAssetCounts = true;
        }

        private void StopAssetCountWarmup()
        {
            if (!isWarmingAssetCounts)
            {
                return;
            }

            EditorApplication.update -= WarmAssetCountsStep;
            isWarmingAssetCounts = false;
        }

        private void WarmAssetCountsStep()
        {
            if (pendingCountTypes.Count == 0)
            {
                StopAssetCountWarmup();
                Repaint();
                return;
            }

            var type = pendingCountTypes.Dequeue();
            if (type != null && !assetCountCache.ContainsKey(type))
            {
                CountAssetsForType(type);
            }

            Repaint();
            if (pendingCountTypes.Count == 0)
            {
                StopAssetCountWarmup();
            }
        }

    }
}
