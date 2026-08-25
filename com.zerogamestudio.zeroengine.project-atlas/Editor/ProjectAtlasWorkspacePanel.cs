using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.EditorUI;

namespace ZeroEngine.ProjectAtlas
{
    [EditorWorkspacePanelProvider("zeroengine.project-atlas")]
    public sealed class ProjectAtlasWorkspacePanelProvider : IEditorWorkspacePanelProvider
    {
        public IEditorWorkspacePanel CreatePanel(string panelId)
        {
            return string.Equals(panelId, "project-atlas", StringComparison.Ordinal)
                ? new ProjectAtlasWorkspacePanel()
                : null;
        }
    }

    internal sealed class ProjectAtlasWorkspacePanel :
        IEditorWorkspacePanel,
        IEditorWorkspaceFullWidthPanel,
        IEditorWorkspaceRouteReceiver
    {
        private const string StatePrefix = "ZeroEngine.ProjectAtlas.FeatureWorkspace.";
        private const float DefaultDomainColumnWidth = 190f;
        private const float DefaultFeatureColumnWidth = 280f;
        private const float MinDomainColumnWidth = 160f;
        private const float MinFeatureColumnWidth = 220f;
        private const float MaxNavigationColumnWidth = 520f;
        private const float MinDetailColumnWidth = 320f;
        private const float SplitterWidth = 5f;
        private const float FeatureRowHeight = 34f;
        private const float ThreeColumnMinContentWidth =
            MinDomainColumnWidth + MinFeatureColumnWidth + MinDetailColumnWidth + SplitterWidth * 2f;
        private const string DomainColumnWidthStateKey = StatePrefix + "DomainColumnWidth";
        private const string FeatureColumnWidthStateKey = StatePrefix + "FeatureColumnWidth";

        private readonly struct BodyLayoutRects
        {
            public BodyLayoutRects(Rect domainColumn, Rect domainSplitter, Rect featureColumn, Rect featureSplitter, Rect detailColumn)
            {
                DomainColumn = domainColumn;
                DomainSplitter = domainSplitter;
                FeatureColumn = featureColumn;
                FeatureSplitter = featureSplitter;
                DetailColumn = detailColumn;
            }

            public Rect DomainColumn { get; }
            public Rect DomainSplitter { get; }
            public Rect FeatureColumn { get; }
            public Rect FeatureSplitter { get; }
            public Rect DetailColumn { get; }
        }

        private ProjectFeatureCatalog _catalog;
        private string _selectedDomainId = string.Empty;
        private string _selectedFeatureId = string.Empty;
        private string _search = string.Empty;
        private string _audience = "全部";
        private string _operationMessage = string.Empty;
        private MessageType _operationMessageType = MessageType.Info;
        private Vector2 _domainScroll;
        private Vector2 _featureScroll;
        private Vector2 _detailScroll;
        private Vector2 _bodyHorizontalScroll;
        private float _domainColumnWidth = DefaultDomainColumnWidth;
        private float _featureColumnWidth = DefaultFeatureColumnWidth;
        private string _activeResizeKey;

        public float RefreshInterval => 0f;

        public void Activate(EditorWorkspacePanelContext context)
        {
            RestoreState();
            Reload();
        }

        public void Deactivate()
        {
            SaveState();
        }

        public void Tick(EditorWorkspacePanelContext context, double timeSinceStartup)
        {
        }

        public void OnGUI(EditorWorkspacePanelContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (_catalog == null)
                Reload();

            DrawPanelIntro(context);
            if (!string.IsNullOrEmpty(_operationMessage))
                EditorGUILayout.HelpBox(_operationMessage, _operationMessageType);
            if (_catalog == null || _catalog.Domains.Count == 0)
            {
                DrawUnconfiguredState();
                DrawHumanDiagnostics();
                return;
            }

            DrawSearchAndAudience();
            ProjectFeatureDomain[] domains = VisibleDomains().ToArray();
            EnsureSelection(domains);
            if (domains.Length == 0)
            {
                EditorUiGUILayout.EmptyState("没有符合当前搜索或岗位筛选的功能。");
                return;
            }

            DrawBodyLayout(context, domains);
            DrawHumanDiagnostics();
        }

        public bool TryApplyWorkspaceRoute(string subrouteId)
        {
            ProjectFeature feature = _catalog?.FindFeature(subrouteId);
            if (feature == null)
                return false;
            _selectedDomainId = feature.DomainId;
            _selectedFeatureId = feature.Id;
            _search = string.Empty;
            _audience = "全部";
            _featureScroll = Vector2.zero;
            _detailScroll = Vector2.zero;
            SaveState();
            return true;
        }

        public void Dispose()
        {
            SaveState();
        }

        private void DrawPanelIntro(EditorWorkspacePanelContext context)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    "按领域和功能查找项目配置、预览与检查入口。",
                    EditorStyles.wordWrappedMiniLabel);
                GUILayout.FlexibleSpace();
                context.DrawAction(new EditorWorkspaceAction(
                    new GUIContent("刷新", "重新读取项目功能目录；不会修改项目内容。"),
                    () =>
                    {
                        Reload();
                        context.RequestRepaint();
                    },
                    EditorWorkspaceActionSafety.ReadOnly),
                    GUILayout.Width(52f),
                    GUILayout.Height(22f));
            }
            EditorGUILayout.Space(4f);
        }

        private void DrawSearchAndAudience()
        {
            string[] audiences = new[] { "全部" }
                .Concat(_catalog.Features.SelectMany(feature => feature.AudienceTags))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value == "全部" ? string.Empty : value, StringComparer.Ordinal)
                .ToArray();
            int audienceIndex = Math.Max(0, Array.IndexOf(audiences, _audience));
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("搜索", GUILayout.Width(36f));
                string nextSearch = GUILayout.TextField(_search ?? string.Empty, EditorStyles.toolbarSearchField);
                if (!string.Equals(nextSearch, _search, StringComparison.Ordinal))
                {
                    _search = nextSearch;
                    _selectedFeatureId = string.Empty;
                }
                GUILayout.Space(8f);
                GUILayout.Label("我的岗位", GUILayout.Width(56f));
                int nextAudience = EditorGUILayout.Popup(audienceIndex, audiences, EditorStyles.toolbarPopup, GUILayout.Width(96f));
                if (nextAudience != audienceIndex)
                {
                    _audience = audiences[nextAudience];
                    _selectedFeatureId = string.Empty;
                }
                if (!string.IsNullOrEmpty(_search) && GUILayout.Button("清除", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                {
                    _search = string.Empty;
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.Space(6f);
        }

        private void DrawBodyLayout(EditorWorkspacePanelContext context, ProjectFeatureDomain[] domains)
        {
            float height = Mathf.Max(440f, context.Owner.position.height - 240f);
            Rect bodyRect = GUILayoutUtility.GetRect(
                Mathf.Max(0f, context.AvailableWidth),
                height,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(height));
            float contentWidth = ResolveBodyContentWidth(bodyRect.width);
            if (contentWidth <= bodyRect.width)
            {
                DrawThreeColumnBodyLayout(context, bodyRect, domains);
                return;
            }

            var contentRect = new Rect(0f, 0f, contentWidth, bodyRect.height);
            _bodyHorizontalScroll = GUI.BeginScrollView(
                bodyRect,
                _bodyHorizontalScroll,
                contentRect,
                false,
                false);
            DrawThreeColumnBodyLayout(context, contentRect, domains);
            GUI.EndScrollView();
        }

        private static float ResolveBodyContentWidth(float bodyWidth)
        {
            return Mathf.Max(bodyWidth, ThreeColumnMinContentWidth);
        }

        private void DrawThreeColumnBodyLayout(
            EditorWorkspacePanelContext context,
            Rect bodyRect,
            ProjectFeatureDomain[] domains)
        {
            BodyLayoutRects layoutRects = CalculateBodyLayoutRects(bodyRect);
            DrawDomainColumn(layoutRects.DomainColumn, domains);
            DrawColumnResizeHandle(
                context,
                layoutRects.DomainSplitter,
                ref _domainColumnWidth,
                DomainColumnWidthStateKey,
                MinDomainColumnWidth,
                bodyRect.width - _featureColumnWidth - SplitterWidth * 2f - MinDetailColumnWidth);

            ProjectFeatureDomain selectedDomain = domains.First(domain => domain.Id == _selectedDomainId);
            ProjectFeature[] features = VisibleFeatures(selectedDomain).ToArray();
            DrawFeatureColumn(layoutRects.FeatureColumn, selectedDomain, features);
            DrawColumnResizeHandle(
                context,
                layoutRects.FeatureSplitter,
                ref _featureColumnWidth,
                FeatureColumnWidthStateKey,
                MinFeatureColumnWidth,
                bodyRect.width - _domainColumnWidth - SplitterWidth * 2f - MinDetailColumnWidth);
            DrawDetailColumn(context, layoutRects.DetailColumn, features);
        }

        private BodyLayoutRects CalculateBodyLayoutRects(Rect bodyRect)
        {
            float maxDomainWidth = Mathf.Max(
                MinDomainColumnWidth,
                bodyRect.width - _featureColumnWidth - SplitterWidth * 2f - MinDetailColumnWidth);
            float resolvedDomainWidth = Mathf.Clamp(
                _domainColumnWidth,
                MinDomainColumnWidth,
                Mathf.Min(MaxNavigationColumnWidth, maxDomainWidth));
            float maxFeatureWidth = Mathf.Max(
                MinFeatureColumnWidth,
                bodyRect.width - resolvedDomainWidth - SplitterWidth * 2f - MinDetailColumnWidth);
            float resolvedFeatureWidth = Mathf.Clamp(
                _featureColumnWidth,
                MinFeatureColumnWidth,
                Mathf.Min(MaxNavigationColumnWidth, maxFeatureWidth));
            float detailWidth = Mathf.Max(
                0f,
                bodyRect.width - resolvedDomainWidth - resolvedFeatureWidth - SplitterWidth * 2f);

            var domainColumn = new Rect(bodyRect.x, bodyRect.y, resolvedDomainWidth, bodyRect.height);
            var domainSplitter = new Rect(domainColumn.xMax, bodyRect.y, SplitterWidth, bodyRect.height);
            var featureColumn = new Rect(domainSplitter.xMax, bodyRect.y, resolvedFeatureWidth, bodyRect.height);
            var featureSplitter = new Rect(featureColumn.xMax, bodyRect.y, SplitterWidth, bodyRect.height);
            var detailColumn = new Rect(featureSplitter.xMax, bodyRect.y, detailWidth, bodyRect.height);
            return new BodyLayoutRects(domainColumn, domainSplitter, featureColumn, featureSplitter, detailColumn);
        }

        private void DrawDomainColumn(Rect rect, ProjectFeatureDomain[] domains)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField("工作领域", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("先选择要处理的项目领域", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4f);
                _domainScroll = EditorGUILayout.BeginScrollView(_domainScroll);
                foreach (ProjectFeatureDomain domain in domains)
                {
                    if (EditorUiGUILayout.SelectionButton(
                            new GUIContent(domain.DisplayName, domain.Summary),
                            domain.Id == _selectedDomainId,
                            GUILayout.ExpandWidth(true),
                            GUILayout.Height(34f)))
                    {
                        SelectDomain(domain.Id);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        private void DrawFeatureColumn(Rect rect, ProjectFeatureDomain selectedDomain, ProjectFeature[] features)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField(selectedDomain.DisplayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(selectedDomain.Summary, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4f);
                _featureScroll = EditorGUILayout.BeginScrollView(_featureScroll);
                DrawFeatureButtons(features);
                EditorGUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        private void DrawDetailColumn(EditorWorkspacePanelContext context, Rect rect, ProjectFeature[] features)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField("功能说明与入口", EditorStyles.boldLabel);
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                DrawSelectedFeature(context, features);
                EditorGUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        private void DrawColumnResizeHandle(
            EditorWorkspacePanelContext context,
            Rect rect,
            ref float width,
            string prefsKey,
            float minWidth,
            float maxWidthByLayout)
        {
            EditorGUI.DrawRect(
                new Rect(rect.x + 2f, rect.y, 1f, rect.height),
                new Color(0.28f, 0.28f, 0.28f, 1f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown && rect.Contains(currentEvent.mousePosition))
            {
                _activeResizeKey = prefsKey;
                currentEvent.Use();
            }

            if (_activeResizeKey == prefsKey && currentEvent.type == EventType.MouseDrag)
            {
                float maxWidth = Mathf.Max(minWidth, Mathf.Min(MaxNavigationColumnWidth, maxWidthByLayout));
                width = Mathf.Clamp(width + currentEvent.delta.x, minWidth, maxWidth);
                context.RequestRepaint();
                currentEvent.Use();
            }

            if (_activeResizeKey == prefsKey && currentEvent.rawType == EventType.MouseUp)
            {
                _activeResizeKey = null;
                EditorPrefs.SetFloat(prefsKey, Mathf.Clamp(width, minWidth, MaxNavigationColumnWidth));
                currentEvent.Use();
            }
        }

        private void DrawFeatureButtons(ProjectFeature[] features)
        {
            if (features.Length == 0)
            {
                EditorUiGUILayout.EmptyState("这个领域没有符合筛选的功能。");
                return;
            }
            EnsureFeatureSelection(features);
            foreach (ProjectFeature feature in features)
            {
                if (EditorUiGUILayout.SelectionButton(
                        new GUIContent(feature.DisplayName, feature.Summary),
                        feature.Id == _selectedFeatureId,
                        GUILayout.ExpandWidth(true),
                        GUILayout.Height(FeatureRowHeight)))
                {
                    SelectFeature(feature.Id);
                }
            }
        }

        private void DrawSelectedFeature(EditorWorkspacePanelContext context, ProjectFeature[] visibleFeatures)
        {
            ProjectFeature feature = visibleFeatures.FirstOrDefault(item => item.Id == _selectedFeatureId);
            if (feature == null)
            {
                EditorUiGUILayout.EmptyState("请选择一个项目功能。");
                return;
            }

            EditorUiGUILayout.Header(feature.DisplayName, feature.Summary, null);
            EditorUiGUILayout.Chip(ConfigurationStatus(feature));
            if (feature.AudienceTags.Count > 0)
                EditorGUILayout.LabelField(
                    "适用岗位：" + string.Join("、", feature.AudienceTags),
                    EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(6f);

            using (EditorUiGUILayout.Section("可完成的工作"))
            {
                foreach (string capability in feature.Capabilities)
                    EditorGUILayout.LabelField("• " + capability, EditorStyles.wordWrappedLabel);
            }

            using (EditorUiGUILayout.Section("直接入口"))
            {
                if (feature.ConfigurationMode == ProjectFeatureConfigurationMode.None)
                    EditorGUILayout.HelpBox(feature.ConfigurationReason, MessageType.Info);
                if (feature.Actions.Count == 0)
                {
                    EditorUiGUILayout.EmptyState("当前功能没有日常入口。");
                    return;
                }
                foreach (ProjectFeatureAction featureAction in feature.Actions
                             .OrderByDescending(item => item.Primary).ThenBy(item => item.Id, StringComparer.Ordinal))
                {
                    DrawFeatureAction(context, feature, featureAction);
                }
            }
        }

        private void DrawFeatureAction(EditorWorkspacePanelContext context, ProjectFeature feature, ProjectFeatureAction featureAction)
        {
            bool resolved = _catalog.Routes.TryGetRoute(featureAction.RouteId, out ProjectFeatureRouteDescriptor route);
            EditorToolActionState state = resolved && route.Action != null
                ? route.Action.GetState()
                : new EditorToolActionState(false, false, "项目尚未配置这个入口。");
            bool enabled = resolved && route.Available && state.Enabled;
            string disabledReason = !resolved
                ? "项目尚未配置这个入口。"
                : !route.Available ? route.DisabledReason : state.DisabledReason;
            string tooltip = enabled ? feature.Summary : disabledReason;
            var action = new EditorWorkspaceAction(
                new GUIContent(featureAction.Label, tooltip),
                () =>
                {
                    EditorToolActionResult result = route.Action.Execute(
                        new EditorToolActionContext(context.Owner, context.ModuleId, feature.Id));
                    _operationMessage = result.Message;
                    _operationMessageType = result.Status == EditorToolActionStatus.Failed
                        ? MessageType.Warning
                        : MessageType.Info;
                    context.RequestRepaint();
                },
                EditorWorkspaceActionSafety.Navigation,
                featureAction.Primary ? EditorWorkspaceActionStyle.Primary : EditorWorkspaceActionStyle.Secondary,
                null,
                enabled);
            context.DrawAction(action, GUILayout.ExpandWidth(true), GUILayout.Height(28f));
            if (!enabled && !string.IsNullOrEmpty(disabledReason))
                EditorGUILayout.LabelField(disabledReason, EditorStyles.wordWrappedMiniLabel);
        }

        private System.Collections.Generic.IEnumerable<ProjectFeatureDomain> VisibleDomains()
        {
            return _catalog.Domains.Where(domain => VisibleFeatures(domain).Any());
        }

        private System.Collections.Generic.IEnumerable<ProjectFeature> VisibleFeatures(ProjectFeatureDomain domain)
        {
            string query = (_search ?? string.Empty).Trim();
            foreach (string featureId in domain.FeatureIds)
            {
                ProjectFeature feature = _catalog.FindFeature(featureId);
                if (feature == null)
                    continue;
                if (_audience != "全部" && !feature.AudienceTags.Contains(_audience))
                    continue;
                if (!string.IsNullOrEmpty(query) &&
                    feature.SearchText.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    domain.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    domain.Keywords.All(keyword => keyword.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }
                yield return feature;
            }
        }

        private void EnsureSelection(ProjectFeatureDomain[] domains)
        {
            if (domains.Length == 0)
                return;
            if (domains.All(domain => domain.Id != _selectedDomainId))
                SelectDomain(domains[0].Id);
            EnsureFeatureSelection(VisibleFeatures(domains.First(domain => domain.Id == _selectedDomainId)).ToArray());
        }

        private void EnsureFeatureSelection(ProjectFeature[] features)
        {
            if (features.Length > 0 && features.All(feature => feature.Id != _selectedFeatureId))
                _selectedFeatureId = features[0].Id;
        }

        private void SelectDomain(string domainId)
        {
            _selectedDomainId = domainId;
            _selectedFeatureId = string.Empty;
            _featureScroll = Vector2.zero;
            _detailScroll = Vector2.zero;
        }

        private void SelectFeature(string featureId)
        {
            _selectedFeatureId = featureId;
            _detailScroll = Vector2.zero;
        }

        private static string ConfigurationStatus(ProjectFeature feature)
        {
            switch (feature.ConfigurationMode)
            {
                case ProjectFeatureConfigurationMode.Configurable: return "可配置";
                case ProjectFeatureConfigurationMode.ReadOnly: return "仅查看";
                default: return "无配置入口";
            }
        }

        private void DrawHumanDiagnostics()
        {
            if (_catalog == null || _catalog.Diagnostics.Count == 0)
                return;
            int errors = _catalog.Diagnostics.Count(item => item.Severity == ProjectAtlasDiagnosticSeverity.Error);
            if (errors > 0)
            {
                EditorGUILayout.HelpBox(
                    "有 " + errors + " 个功能入口配置问题；受影响按钮已停用，其他功能仍可浏览。请交由程序维护项目功能目录。",
                    MessageType.Warning);
            }
        }

        private static void DrawUnconfiguredState()
        {
            using (EditorUiGUILayout.Section("项目尚未建立功能导航"))
            {
                EditorGUILayout.LabelField(
                    "当前项目还没有面向项目人员的功能目录。请由程序在 docs/project 下接入功能清单；这里不会自动创建或修改业务配置。",
                    EditorStyles.wordWrappedLabel);
            }
        }

        private void Reload()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            _catalog = ProjectFeatureCatalogLoader.LoadProject(projectRoot);
            _operationMessage = string.Empty;
            ProjectFeatureDomain[] domains = VisibleDomains().ToArray();
            if (domains.Length == 0)
                return;
            if (domains.All(domain => domain.Id != _selectedDomainId))
                _selectedDomainId = _catalog.Domains.Any(domain => domain.Id == _catalog.DefaultDomainId)
                    ? _catalog.DefaultDomainId
                    : domains[0].Id;
            EnsureSelection(domains);
        }

        private void RestoreState()
        {
            _selectedDomainId = EditorPrefs.GetString(StatePrefix + "Domain", string.Empty);
            _selectedFeatureId = EditorPrefs.GetString(StatePrefix + "Feature", string.Empty);
            _search = EditorPrefs.GetString(StatePrefix + "Search", string.Empty);
            _audience = EditorPrefs.GetString(StatePrefix + "Audience", "全部");
            _domainScroll = LoadVector("DomainScroll");
            _featureScroll = LoadVector("FeatureScroll");
            _detailScroll = LoadVector("DetailScroll");
            _bodyHorizontalScroll = LoadVector("BodyHorizontalScroll");
            _domainColumnWidth = Mathf.Clamp(
                EditorPrefs.GetFloat(DomainColumnWidthStateKey, DefaultDomainColumnWidth),
                MinDomainColumnWidth,
                MaxNavigationColumnWidth);
            _featureColumnWidth = Mathf.Clamp(
                EditorPrefs.GetFloat(FeatureColumnWidthStateKey, DefaultFeatureColumnWidth),
                MinFeatureColumnWidth,
                MaxNavigationColumnWidth);
        }

        private void SaveState()
        {
            EditorPrefs.SetString(StatePrefix + "Domain", _selectedDomainId ?? string.Empty);
            EditorPrefs.SetString(StatePrefix + "Feature", _selectedFeatureId ?? string.Empty);
            EditorPrefs.SetString(StatePrefix + "Search", _search ?? string.Empty);
            EditorPrefs.SetString(StatePrefix + "Audience", _audience ?? "全部");
            SaveVector("DomainScroll", _domainScroll);
            SaveVector("FeatureScroll", _featureScroll);
            SaveVector("DetailScroll", _detailScroll);
            SaveVector("BodyHorizontalScroll", _bodyHorizontalScroll);
            EditorPrefs.SetFloat(
                DomainColumnWidthStateKey,
                Mathf.Clamp(_domainColumnWidth, MinDomainColumnWidth, MaxNavigationColumnWidth));
            EditorPrefs.SetFloat(
                FeatureColumnWidthStateKey,
                Mathf.Clamp(_featureColumnWidth, MinFeatureColumnWidth, MaxNavigationColumnWidth));
        }

        private static Vector2 LoadVector(string key)
        {
            return new Vector2(
                EditorPrefs.GetFloat(StatePrefix + key + "X", 0f),
                EditorPrefs.GetFloat(StatePrefix + key + "Y", 0f));
        }

        private static void SaveVector(string key, Vector2 value)
        {
            EditorPrefs.SetFloat(StatePrefix + key + "X", value.x);
            EditorPrefs.SetFloat(StatePrefix + key + "Y", value.y);
        }
    }
}
