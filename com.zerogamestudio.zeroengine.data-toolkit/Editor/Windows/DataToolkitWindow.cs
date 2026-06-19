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
        private const float MinInspectorWidth = 320f;
        private const float WindowPadding = 4f;
        private const float HeaderRowHeight = 38f;
        private const float ProjectToolbarRowHeight = 64f;
        private const float ProjectFooterRowHeight = 34f;
        private const float HeaderBodySpacing = 4f;
        private const float BodyFooterSpacing = 4f;
        private const float SplitterWidth = 5f;
        private const float RowHeight = 24f;
        private const long LargeAssetInspectorThresholdBytes = 512 * 1024;

        private readonly CompositeAssetInspector inspector = new();
        private readonly Dictionary<Type, int> assetCountCache = new();
        private readonly Queue<Type> pendingCountTypes = new();

        private DataToolkitContext context;
        private IReadOnlyList<IDataToolkitToolbarProvider> toolbarProviders = Array.Empty<IDataToolkitToolbarProvider>();
        private IReadOnlyList<IDataToolkitFooterProvider> footerProviders = Array.Empty<IDataToolkitFooterProvider>();
        private IReadOnlyList<IDataToolkitValidationProvider> validationProviders = Array.Empty<IDataToolkitValidationProvider>();
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
        private bool allowFullInspectorForSelectedAsset;
        private DataValidationReport validationReport = DataValidationReport.Empty;
        private bool hasValidationReport;
        private DataAssetReferenceGraph referenceGraph = DataAssetReferenceGraph.Empty;
        private bool hasReferenceGraph;
        private bool showReferencePanel = true;
        private bool showBatchEditPanel = true;
        private string batchFieldPath = string.Empty;
        private string batchNewValue = string.Empty;

        private readonly struct SelectionSnapshot
        {
            public SelectionSnapshot(Type selectedType, string selectedTypeId, string assetPath, string assetGuid)
            {
                SelectedType = selectedType;
                SelectedTypeId = selectedTypeId;
                AssetPath = assetPath;
                AssetGuid = assetGuid;
            }

            public Type SelectedType { get; }
            public string SelectedTypeId { get; }
            public string AssetPath { get; }
            public string AssetGuid { get; }
        }

        private readonly struct BodyLayoutRects
        {
            public BodyLayoutRects(Rect typeColumn, Rect typeSplitter, Rect assetColumn, Rect assetSplitter, Rect inspectorColumn)
            {
                TypeColumn = typeColumn;
                TypeSplitter = typeSplitter;
                AssetColumn = assetColumn;
                AssetSplitter = assetSplitter;
                InspectorColumn = inspectorColumn;
            }

            public Rect TypeColumn { get; }
            public Rect TypeSplitter { get; }
            public Rect AssetColumn { get; }
            public Rect AssetSplitter { get; }
            public Rect InspectorColumn { get; }
        }

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
            footerProviders = profile.FooterProviders;
            validationProviders = profile.ValidationProviders;
            inspector.SetCustomInspectors(context, profile.AssetInspectorProviders);
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
            var visibleToolbarProviders = GetVisibleToolbarProviders();
            var visibleFooterProviders = GetVisibleFooterProviders();
            var headerHeight = CalculateHeaderHeight(visibleToolbarProviders.Count);
            var footerHeight = CalculateFooterHeight(visibleFooterProviders.Count);
            var footerSpacing = footerHeight > 0f ? BodyFooterSpacing : 0f;
            var contentWidth = Mathf.Max(0f, position.width - WindowPadding * 2f);
            var headerRect = new Rect(WindowPadding, WindowPadding, contentWidth, headerHeight);
            var bodyRect = new Rect(
                WindowPadding,
                headerRect.yMax + HeaderBodySpacing,
                contentWidth,
                Mathf.Max(0f, position.height - headerRect.yMax - HeaderBodySpacing - footerHeight - footerSpacing - WindowPadding));
            var footerRect = new Rect(
                WindowPadding,
                position.height - WindowPadding - footerHeight,
                contentWidth,
                footerHeight);

            DrawHeaderToolbar(headerRect, visibleToolbarProviders);
            DrawBodyLayout(bodyRect);
            DrawFooterToolbar(footerRect, visibleFooterProviders);
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

        private float CalculateHeaderHeight(int visibleToolbarCount)
        {
            return HeaderRowHeight + visibleToolbarCount * ProjectToolbarRowHeight;
        }

        private float CalculateFooterHeight(int visibleFooterCount)
        {
            return visibleFooterCount <= 0 ? 0f : visibleFooterCount * ProjectFooterRowHeight;
        }

        private IReadOnlyList<IDataToolkitToolbarProvider> GetVisibleToolbarProviders()
        {
            var visibleProviders = new List<IDataToolkitToolbarProvider>();

            foreach (var toolbarProvider in toolbarProviders)
            {
                if (toolbarProvider == null)
                {
                    continue;
                }

                try
                {
                    if (toolbarProvider.IsVisible(context))
                    {
                        visibleProviders.Add(toolbarProvider);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            return visibleProviders;
        }

        private IReadOnlyList<IDataToolkitFooterProvider> GetVisibleFooterProviders()
        {
            var visibleProviders = new List<IDataToolkitFooterProvider>();

            foreach (var footerProvider in footerProviders)
            {
                if (footerProvider == null)
                {
                    continue;
                }

                try
                {
                    if (footerProvider.IsVisible(context))
                    {
                        visibleProviders.Add(footerProvider);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            return visibleProviders;
        }

        private void DrawHeaderToolbar(Rect rect, IReadOnlyList<IDataToolkitToolbarProvider> visibleToolbarProviders)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(context.Settings.WindowTitle, EditorStyles.boldLabel, GUILayout.MinWidth(160f));
                GUILayout.Label(BuildAssetSummaryText(), EditorStyles.miniLabel, GUILayout.Width(220f));
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Refresh", GUILayout.Width(82f), GUILayout.Height(24f)))
                {
                    RefreshCaches();
                }

                if (GUILayout.Button("Validate", GUILayout.Width(82f), GUILayout.Height(24f)))
                {
                    RunValidation();
                }

                if (GUILayout.Button("References", GUILayout.Width(92f), GUILayout.Height(24f)))
                {
                    BuildReferenceGraph();
                }

                if (GUILayout.Button("Import CSV", GUILayout.Width(92f), GUILayout.Height(24f)))
                {
                    ImportFieldCsv();
                }

                if (GUILayout.Button("Export CSV", GUILayout.Width(92f), GUILayout.Height(24f)))
                {
                    ExportValidationCsv();
                }

                EditorGUILayout.EndHorizontal();
                DrawProjectToolbars(visibleToolbarProviders);
            }
            GUILayout.EndArea();
        }

        private void DrawProjectToolbars(IReadOnlyList<IDataToolkitToolbarProvider> visibleToolbarProviders)
        {
            foreach (var toolbarProvider in visibleToolbarProviders)
            {
                try
                {
                    toolbarProvider.DrawToolbar(context);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void DrawFooterToolbar(Rect rect, IReadOnlyList<IDataToolkitFooterProvider> visibleFooterProviders)
        {
            if (visibleFooterProviders.Count == 0)
            {
                return;
            }

            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                DrawProjectFooters(visibleFooterProviders);
            }
            GUILayout.EndArea();
        }

        private void DrawProjectFooters(IReadOnlyList<IDataToolkitFooterProvider> visibleFooterProviders)
        {
            foreach (var footerProvider in visibleFooterProviders)
            {
                try
                {
                    footerProvider.DrawFooter(context);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void DrawBodyLayout(Rect bodyRect)
        {
            var layoutRects = CalculateBodyLayoutRects(bodyRect);

            DrawTypeColumn(layoutRects.TypeColumn);
            DrawColumnResizeHandle(
                layoutRects.TypeSplitter,
                ref typeColumnWidth,
                context.Settings.PrefKey("TypeColumnWidth"),
                bodyRect.width - assetColumnWidth - SplitterWidth * 2f - MinInspectorWidth);
            DrawAssetColumn(layoutRects.AssetColumn);
            DrawColumnResizeHandle(
                layoutRects.AssetSplitter,
                ref assetColumnWidth,
                context.Settings.PrefKey("AssetColumnWidth"),
                bodyRect.width - typeColumnWidth - SplitterWidth * 2f - MinInspectorWidth);
            DrawSelectedAssetInspector(layoutRects.InspectorColumn);
        }

        private BodyLayoutRects CalculateBodyLayoutRects(Rect bodyRect)
        {
            var maxTypeWidth = Mathf.Max(MinColumnWidth, bodyRect.width - assetColumnWidth - SplitterWidth * 2f - MinInspectorWidth);
            var resolvedTypeWidth = Mathf.Clamp(typeColumnWidth, MinColumnWidth, Mathf.Min(MaxColumnWidth, maxTypeWidth));
            var maxAssetWidth = Mathf.Max(MinColumnWidth, bodyRect.width - resolvedTypeWidth - SplitterWidth * 2f - MinInspectorWidth);
            var resolvedAssetWidth = Mathf.Clamp(assetColumnWidth, MinColumnWidth, Mathf.Min(MaxColumnWidth, maxAssetWidth));
            var inspectorWidth = Mathf.Max(0f, bodyRect.width - resolvedTypeWidth - resolvedAssetWidth - SplitterWidth * 2f);

            var typeColumn = new Rect(bodyRect.x, bodyRect.y, resolvedTypeWidth, bodyRect.height);
            var typeSplitter = new Rect(typeColumn.xMax, bodyRect.y, SplitterWidth, bodyRect.height);
            var assetColumn = new Rect(typeSplitter.xMax, bodyRect.y, resolvedAssetWidth, bodyRect.height);
            var assetSplitter = new Rect(assetColumn.xMax, bodyRect.y, SplitterWidth, bodyRect.height);
            var inspectorColumn = new Rect(assetSplitter.xMax, bodyRect.y, inspectorWidth, bodyRect.height);

            return new BodyLayoutRects(typeColumn, typeSplitter, assetColumn, assetSplitter, inspectorColumn);
        }

        private void DrawTypeColumn(Rect rect)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
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
            GUILayout.EndArea();
        }

        private void DrawAssetColumn(Rect rect)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField(selectedType == null ? "Assets" : selectedType.Name, EditorStyles.boldLabel);
                assetSearch = EditorGUILayout.TextField(assetSearch, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarSearchField);

                assetColumnScroll = EditorGUILayout.BeginScrollView(assetColumnScroll);
                foreach (var assetPath in AssetDiscoveryService.GetAssetPathsForType(selectedType, context.Settings).Where(IsAssetVisible))
                {
                    if (DrawSelectableRow(Path.GetFileNameWithoutExtension(assetPath), GetAssetIssueLabel(assetPath), assetPath == selectedAssetPath))
                    {
                        SelectAssetByPath(assetPath);
                    }
                }

                EditorGUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        private void DrawSelectedAssetInspector(Rect rect)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                if (selectedAsset == null)
                {
                    DrawEmptyInspectorState();
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(selectedAsset.name, EditorStyles.boldLabel);
                    if (GUILayout.Button("Ping", GUILayout.Width(64f), GUILayout.Height(22f)))
                    {
                        EditorGUIUtility.PingObject(selectedAsset);
                    }

                    EditorGUILayout.EndHorizontal();

                    DrawSelectedAssetIssues();
                    DrawSelectedAssetReferences();
                    DrawBatchEditTools();

                    if (ShouldDeferFullInspector(selectedAsset) && !HasCustomInspectorFor(selectedAsset) && !allowFullInspectorForSelectedAsset)
                    {
                        DrawDeferredInspectorState();
                    }
                    else
                    {
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
            }
            GUILayout.EndArea();
        }

        private void DrawEmptyInspectorState()
        {
            EditorGUILayout.HelpBox("Select a data asset from the middle column.", MessageType.Info);
        }

        private void DrawDeferredInspectorState()
        {
            var assetPath = ResolveSelectedAssetPath();
            var sizeInBytes = GetAssetFileSize(assetPath);

            EditorGUILayout.HelpBox(
                "This asset is large, so the full inspector is deferred to keep Data Manager responsive.",
                MessageType.Info);

            EditorGUILayout.LabelField("Type", selectedAsset.GetType().Name);
            EditorGUILayout.LabelField("Path", string.IsNullOrEmpty(assetPath) ? "(unknown)" : assetPath);
            EditorGUILayout.LabelField("Size", FormatByteSize(sizeInBytes));

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Open Full Inspector", GUILayout.Height(28f)))
            {
                allowFullInspectorForSelectedAsset = true;
                inspector.SetTarget(selectedAsset);
                Repaint();
            }
        }

        private void DrawSelectedAssetIssues()
        {
            if (!hasValidationReport || selectedAsset == null)
            {
                return;
            }

            var assetPath = ResolveSelectedAssetPath();
            var issues = validationReport.GetIssuesForAssetPath(assetPath);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Validation passed for this asset.", MessageType.Info);
                return;
            }

            const int maxIssuesToShow = 8;
            foreach (var issue in issues.Take(maxIssuesToShow))
            {
                var fieldPrefix = string.IsNullOrEmpty(issue.FieldPath) ? string.Empty : $"{issue.FieldPath}: ";
                EditorGUILayout.HelpBox(fieldPrefix + issue.Message, ToMessageType(issue.Severity));
            }

            if (issues.Count > maxIssuesToShow)
            {
                EditorGUILayout.HelpBox($"{issues.Count - maxIssuesToShow} more validation issues are hidden.", MessageType.Warning);
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawSelectedAssetReferences()
        {
            if (selectedAsset == null)
            {
                return;
            }

            showReferencePanel = EditorGUILayout.Foldout(showReferencePanel, "References", true);
            if (!showReferencePanel)
            {
                return;
            }

            if (!hasReferenceGraph)
            {
                EditorGUILayout.HelpBox("Click References to build a cross-asset reference graph.", MessageType.Info);
                return;
            }

            var assetPath = ResolveSelectedAssetPath();
            var outgoing = referenceGraph.GetOutgoing(assetPath);
            var incoming = referenceGraph.GetIncoming(assetPath);
            EditorGUILayout.LabelField($"Outgoing {outgoing.Count} / Incoming {incoming.Count}", EditorStyles.miniBoldLabel);
            DrawReferenceEdges("Uses", outgoing, edge => $"{edge.FieldPath} -> {edge.TargetName} ({edge.TargetType})");
            DrawReferenceEdges("Used By", incoming, edge => $"{edge.SourceName} ({edge.SourceType}) via {edge.FieldPath}");
            EditorGUILayout.Space(4f);
        }

        private static void DrawReferenceEdges(
            string label,
            IReadOnlyList<DataAssetReferenceEdge> edges,
            Func<DataAssetReferenceEdge, string> formatter)
        {
            if (edges.Count == 0)
            {
                EditorGUILayout.LabelField(label, "(none)", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            foreach (var edge in edges.Take(8))
            {
                EditorGUILayout.LabelField("  " + formatter(edge), EditorStyles.miniLabel);
            }

            if (edges.Count > 8)
            {
                EditorGUILayout.LabelField($"  +{edges.Count - 8} more", EditorStyles.miniLabel);
            }
        }

        private void DrawBatchEditTools()
        {
            if (selectedType == null)
            {
                return;
            }

            showBatchEditPanel = EditorGUILayout.Foldout(showBatchEditPanel, "Batch Edit Visible Assets", true);
            if (!showBatchEditPanel)
            {
                return;
            }

            batchFieldPath = EditorGUILayout.TextField("Field Path", batchFieldPath);
            batchNewValue = EditorGUILayout.TextField("New Value", batchNewValue);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(batchFieldPath));
                if (GUILayout.Button("Preview Batch Edit", GUILayout.Width(150f), GUILayout.Height(24f)))
                {
                    PreviewBatchEdit();
                }

                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space(4f);
        }

        private bool ShouldDeferFullInspector(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return false;
            }

            var assetPath = ResolveSelectedAssetPath();
            return GetAssetFileSize(assetPath) > LargeAssetInspectorThresholdBytes;
        }

        private bool HasCustomInspectorFor(UnityEngine.Object asset)
        {
            return inspector.HasCustomInspectorFor(asset);
        }

        private string ResolveSelectedAssetPath()
        {
            if (!string.IsNullOrEmpty(selectedAssetPath))
            {
                return selectedAssetPath;
            }

            return selectedAsset == null ? null : AssetDatabase.GetAssetPath(selectedAsset);
        }

        private static long GetAssetFileSize(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return 0L;
            }

            if (!File.Exists(assetPath))
            {
                return 0L;
            }

            return new FileInfo(assetPath).Length;
        }

        private static string FormatByteSize(long bytes)
        {
            if (bytes <= 0L)
            {
                return "0 KB";
            }

            if (bytes < 1024L * 1024L)
            {
                return $"{Mathf.CeilToInt(bytes / 1024f)} KB";
            }

            return $"{bytes / (1024f * 1024f):0.0} MB";
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

        private void DrawColumnResizeHandle(Rect rect, ref float width, string prefsKey, float maxWidthByLayout)
        {
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
            ClearSelectedAsset();
        }

        private void SelectAssetByPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                ClearSelectedAsset();
                return;
            }

            if (selectedAssetPath == assetPath && selectedAsset != null)
            {
                return;
            }

            selectedAssetPath = assetPath;
            SelectAsset(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath));
        }

        private void SelectAsset(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                ClearSelectedAsset();
                return;
            }

            if (selectedAsset == asset)
            {
                return;
            }

            selectedAsset = asset;
            Selection.activeObject = asset;
            inspectorScroll = Vector2.zero;
            allowFullInspectorForSelectedAsset = false;
            inspector.SetTarget(null);
        }

        private void ClearSelectedAsset()
        {
            selectedAssetPath = null;
            selectedAsset = null;
            Selection.activeObject = null;
            inspectorScroll = Vector2.zero;
            allowFullInspectorForSelectedAsset = false;
            inspector.SetTarget(null);
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
            var summary = $"{typesToDisplay.Length} types / {countedAssets}{suffix} assets";
            return hasValidationReport
                ? $"{summary} / E{validationReport.ErrorCount} W{validationReport.WarningCount}"
                : summary;
        }

        private string GetAssetIssueLabel(string assetPath)
        {
            if (!hasValidationReport)
            {
                return null;
            }

            validationReport.GetIssueCountsForAssetPath(assetPath, out var errors, out var warnings);
            if (errors == 0 && warnings == 0)
            {
                return "OK";
            }

            return errors > 0 ? $"E{errors} W{warnings}" : $"W{warnings}";
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
            var selectionSnapshot = CaptureSelectionSnapshot();

            ManageableDataTypeDiscovery.ClearCache();
            AssetDiscoveryService.ClearCaches();
            assetCountCache.Clear();
            pendingCountTypes.Clear();
            validationReport = DataValidationReport.Empty;
            hasValidationReport = false;
            referenceGraph = DataAssetReferenceGraph.Empty;
            hasReferenceGraph = false;
            typesToDisplay = ManageableDataTypeDiscovery.GetManageableScriptableObjectTypes().ToArray();
            RestoreSelectionAfterRefresh(selectionSnapshot);
            StartAssetCountWarmup();
            Repaint();
        }

        private void RunValidation()
        {
            validationReport = DataAssetValidationService.ValidateAssets(typesToDisplay, context.Settings, validationProviders);
            hasValidationReport = true;

            var message = validationReport.HasIssues
                ? $"Validation finished: {validationReport.ErrorCount} errors, {validationReport.WarningCount} warnings."
                : "Validation finished: no issues found.";
            ShowNotification(new GUIContent(message));
            Repaint();
        }

        private void BuildReferenceGraph()
        {
            referenceGraph = DataAssetReferenceGraphService.Build(LoadAssetsForTypes(typesToDisplay));
            hasReferenceGraph = true;
            ShowNotification(new GUIContent($"Reference graph: {referenceGraph.Edges.Count} references."));
            Repaint();
        }

        private void PreviewBatchEdit()
        {
            var assets = GetVisibleAssetPathsForSelectedType()
                .Select(path => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path))
                .Where(asset => asset != null)
                .ToArray();
            var preview = DataAssetBatchEditService.PreviewSetValue(assets, batchFieldPath, batchNewValue);
            DataAssetChangePreviewWindow.ShowPreview(
                "Batch Edit Preview",
                preview,
                appliedPreview =>
                {
                    var applied = DataAssetFieldChangeService.Apply(appliedPreview, "Batch Edit Data Assets");
                    RefreshCaches();
                    ShowNotification(new GUIContent($"Applied {applied} batch changes."));
                });
        }

        private void ImportFieldCsv()
        {
            var inputPath = EditorUtility.OpenFilePanel(
                "Import Data Field CSV",
                Application.dataPath,
                "csv");
            if (string.IsNullOrEmpty(inputPath))
            {
                return;
            }

            DataAssetChangePreview preview;
            try
            {
                preview = DataAssetCsvImporter.PreviewFieldUpdatesFromCsv(File.ReadAllText(inputPath));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Import Failed", exception.Message, "OK");
                return;
            }

            DataAssetChangePreviewWindow.ShowPreview(
                "Import CSV Preview",
                preview,
                appliedPreview =>
                {
                    var applied = DataAssetFieldChangeService.Apply(appliedPreview, "Import Data Field CSV");
                    RefreshCaches();
                    ShowNotification(new GUIContent($"Applied {applied} imported changes."));
                });
        }

        private IEnumerable<UnityEngine.Object> LoadAssetsForTypes(IEnumerable<Type> types)
        {
            foreach (var type in types ?? Array.Empty<Type>())
            {
                foreach (var assetPath in AssetDiscoveryService.GetAssetPathsForType(type, context.Settings))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (asset != null)
                    {
                        yield return asset;
                    }
                }
            }
        }

        private IEnumerable<string> GetVisibleAssetPathsForSelectedType()
        {
            return AssetDiscoveryService.GetAssetPathsForType(selectedType, context.Settings)
                .Where(IsAssetVisible);
        }

        private void ExportValidationCsv()
        {
            if (!hasValidationReport)
            {
                RunValidation();
            }

            var defaultName = $"{context.Settings.ProjectId}_data_validation.csv";
            var outputPath = EditorUtility.SaveFilePanel(
                "Export Data Validation CSV",
                Application.dataPath,
                defaultName,
                "csv");
            if (string.IsNullOrEmpty(outputPath))
            {
                return;
            }

            try
            {
                DataAssetCsvExporter.ExportValidationSummary(outputPath, typesToDisplay, context.Settings, validationReport);
                ShowNotification(new GUIContent("Data validation CSV exported."));
                EditorUtility.RevealInFinder(outputPath);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Export Failed", exception.Message, "OK");
            }
        }

        private static MessageType ToMessageType(DataValidationSeverity severity)
        {
            return severity switch
            {
                DataValidationSeverity.Error => MessageType.Error,
                DataValidationSeverity.Warning => MessageType.Warning,
                _ => MessageType.Info
            };
        }

        private SelectionSnapshot CaptureSelectionSnapshot()
        {
            var assetPath = selectedAssetPath;
            if (string.IsNullOrEmpty(assetPath) && selectedAsset != null)
            {
                assetPath = AssetDatabase.GetAssetPath(selectedAsset);
            }

            var assetGuid = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath);
            return new SelectionSnapshot(selectedType, selectedType?.AssemblyQualifiedName, assetPath, assetGuid);
        }

        private void RestoreSelectionAfterRefresh(SelectionSnapshot selectionSnapshot)
        {
            var previousSelectedType = selectedType;
            selectedType = ResolveTypeAfterRefresh(selectionSnapshot);
            if (selectedType != previousSelectedType)
            {
                assetColumnScroll = Vector2.zero;
                assetSearch = string.Empty;
            }

            ClearSelectedAsset();

            if (selectedType == null)
            {
                EnsureSelectedType();
                return;
            }

            var assetPath = ResolveAssetPathAfterRefresh(selectionSnapshot);
            if (!string.IsNullOrEmpty(assetPath) && AssetBelongsToSelectedType(assetPath))
            {
                SelectAssetByPath(assetPath);
            }
        }

        private Type ResolveTypeAfterRefresh(SelectionSnapshot selectionSnapshot)
        {
            if (!string.IsNullOrEmpty(selectionSnapshot.SelectedTypeId))
            {
                return typesToDisplay.FirstOrDefault(type => type.AssemblyQualifiedName == selectionSnapshot.SelectedTypeId);
            }

            return selectionSnapshot.SelectedType != null && typesToDisplay.Contains(selectionSnapshot.SelectedType)
                ? selectionSnapshot.SelectedType
                : null;
        }

        private string ResolveAssetPathAfterRefresh(SelectionSnapshot selectionSnapshot)
        {
            if (!string.IsNullOrEmpty(selectionSnapshot.AssetGuid))
            {
                var assetPathFromGuid = AssetDatabase.GUIDToAssetPath(selectionSnapshot.AssetGuid);
                if (!string.IsNullOrEmpty(assetPathFromGuid))
                {
                    return assetPathFromGuid;
                }
            }

            return selectionSnapshot.AssetPath;
        }

        private bool AssetBelongsToSelectedType(string assetPath)
        {
            return selectedType != null &&
                   AssetDiscoveryService.GetAssetPathsForType(selectedType, context.Settings).Contains(assetPath);
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
