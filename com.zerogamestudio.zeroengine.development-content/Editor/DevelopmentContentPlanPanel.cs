using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ZeroEngine.DevelopmentContent;
using UnityEditor;
using UnityEngine;
using ZeroEngine.EditorUI;

namespace ZeroEngine.DevelopmentContent.Editor
{
    public sealed class DevelopmentContentPlanPanel : IEditorWorkspacePanel, IEditorWorkspaceFullWidthPanel
    {
        private readonly DevelopmentContentPanelOptions options;
        private IDevelopmentContentInventory source => options.Inventory;
        private string SessionPrefix => options.SessionStatePrefix;
        private DevelopmentContentPlanStore store;
        private DevelopmentContentPlan plan;
        private DevelopmentContentRecord draft;
        private string selectedKey;
        private ContentPlanView view;
        private string search = string.Empty;
        private string selectedGroup = string.Empty;
        private string[] groups = { "全部分类" };
        private bool readyOnly, dirty, showSources, showVersions;
        private string newVersion = string.Empty;
        private int currentVersionIndex;
        private string error, message;
        private Vector2 scroll;
        private DevelopmentContentRow[] rows = Array.Empty<DevelopmentContentRow>();
        private string ProjectRoot => options.ProjectRoot;

        public DevelopmentContentPlanPanel(DevelopmentContentPanelOptions options)
        { this.options = options ?? throw new ArgumentNullException(nameof(options)); }
        public float RefreshInterval => 1f;

        public void Activate(EditorWorkspacePanelContext context)
        {
            view = (ContentPlanView)Mathf.Clamp(SessionState.GetInt(SessionPrefix + "View", 2), 0, 3);
            search = SessionState.GetString(SessionPrefix + "Search", string.Empty);
            selectedKey = SessionState.GetString(SessionPrefix + "Selected", string.Empty);
            readyOnly = SessionState.GetBool(SessionPrefix + "Ready", false);
            Reload();
            string savedDraft = SessionState.GetString(SessionPrefix + "Draft", string.Empty);
            if (!string.IsNullOrEmpty(savedDraft))
            {
                try
                {
                    var restored = JsonConvert.DeserializeObject<DevelopmentContentRecord>(savedDraft);
                    if (restored != null && Enum.IsDefined(typeof(ContentDevelopmentStage), restored.Stage) &&
                        Enum.IsDefined(typeof(ContentAvailability), restored.Availability))
                    { draft = restored; dirty = true; message = "上次未保存的草稿已保留。"; }
                }
                catch (JsonException) { message = "未保存草稿无法读取，项目中的计划未改变。"; }
            }
        }

        public void Deactivate()
        {
            SessionState.SetInt(SessionPrefix + "View", (int)view);
            SessionState.SetString(SessionPrefix + "Search", search);
            SessionState.SetString(SessionPrefix + "Selected", selectedKey ?? string.Empty);
            SessionState.SetBool(SessionPrefix + "Ready", readyOnly);
            RememberDraft();
        }

        public void Dispose() => Deactivate();
        public void Tick(EditorWorkspacePanelContext context, double timeSinceStartup) => Reload();

        private void Reload()
        {
            source.Refresh();
            try
            {
                var nextStore = new DevelopmentContentPlanStore(options.RegistryPath, options.ProjectId);
                var next = nextStore.Load();
                if (store == null || store.Revision != nextStore.Revision)
                {
                    if (dirty) message = "计划已在外部更新；草稿保留。请取消后重新载入，或先复制草稿内容再合并。";
                    else
                    {
                        store = nextStore;
                        plan = next;
                        currentVersionIndex = 1 + plan.Releases.FindIndex(release => release.Id == plan.CurrentReleaseId);
                        draft = null;
                    }
                }
                error = null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException || exception is FormatException)
            {
                error = "计划读取失败，原文件与草稿均保留：" + exception.Message;
            }
            // An unreadable plan disables saving, while the original inventory remains available.
            plan ??= new DevelopmentContentPlan { ProjectId = options.ProjectId };
            rows = DevelopmentContentRow.Merge(plan, source.Entries);
            groups = new[] { "全部分类" }.Concat(rows.Select(row => DevelopmentContentLabels.Group(row)).Distinct()).ToArray();
            if (!groups.Contains(selectedGroup)) selectedGroup = groups[0];
            if (draft == null)
            {
                var selected = rows.FirstOrDefault(row => row.Key == selectedKey) ?? rows.FirstOrDefault();
                if (selected != null) Select(selected);
            }
        }

        public void OnGUI(EditorWorkspacePanelContext context)
        {
            if (plan == null)
            {
                EditorGUILayout.HelpBox(error ?? "计划尚未载入。", MessageType.Warning);
                if (GUILayout.Button("重新载入")) Reload();
                return;
            }
            DrawToolbar();
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            if (source.Error != null) EditorGUILayout.HelpBox(source.Error, MessageType.Warning);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            var visible = rows.Where(Matches).ToArray();
            GUILayout.Label($"显示 {visible.Length} / {rows.Length} 项 · 已登记 {plan.Records.Count} 项；其余入口待登记", EditorStyles.miniLabel);
            bool compact = context != null && context.AvailableWidth < 650f;
            float listHeight = Mathf.Clamp((context?.Owner.position.height ?? 800f) - 570f, 140f, 320f);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(listHeight));
            if (!compact) DrawColumns(null);
            foreach (var row in visible)
            {
                using (new EditorGUILayout.VerticalScope(row.Key == selectedKey ? EditorStyles.helpBox : GUIStyle.none))
                {
                    if (compact)
                    {
                        if (GUILayout.Button(row.Title, EditorStyles.linkLabel)) TrySelect(row);
                        GUILayout.Label($"{DevelopmentContentLabels.Stage(row.Record)}  ·  {DevelopmentContentLabels.Target(plan, row.Record)}  ·  {DevelopmentContentLabels.Scope(row.Record)}", EditorStyles.miniLabel);
                        if (row.Record != null) GUILayout.Label(row.Record.NextAction, EditorStyles.wordWrappedMiniLabel);
                    }
                    else DrawColumns(row);
                }
            }
            if (visible.Length == 0) GUILayout.Label("没有匹配项；可切换版本、分类或清空搜索。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
            if (draft != null) DrawDetail(visible.Any(row => row.Key == selectedKey));
        }

        private bool Matches(DevelopmentContentRow row) => row.InView(plan, view) && (!readyOnly || row.Record?.ReadyUnreleased == true) &&
            (selectedGroup == "全部分类" || DevelopmentContentLabels.Group(row) == selectedGroup) && row.Matches(search);

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string[] labels = { "本版", "其他版本", "待排期", "全部" };
                for (int i = 0; i < labels.Length; i++)
                {
                    var candidate = (ContentPlanView)i;
                    string label = labels[i] + " " + rows.Count(row => row.InView(plan, candidate));
                    if (GUILayout.Toggle(view == candidate, label, EditorStyles.miniButton) && view != candidate)
                    { view = candidate; scroll = Vector2.zero; }
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("登记内容", GUILayout.Width(80f)) && CanLeaveDraft())
                {
                    selectedKey = null;
                    draft = new DevelopmentContentRecord { Id = options.ProjectId + ".feature." + Guid.NewGuid().ToString("N") };
                    dirty = true;
                    RememberDraft();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                search = EditorGUILayout.TextField(new GUIContent("搜索", "搜索名称、入口、下一步和负责聊天。"), search);
                readyOnly = GUILayout.Toggle(readyOnly, "只看就绪未上线", GUILayout.Width(135f));
            }
            int group = EditorGUILayout.Popup("分类", Mathf.Max(0, Array.IndexOf(groups, selectedGroup)), groups);
            selectedGroup = groups[group];
            showVersions = EditorGUILayout.Foldout(showVersions,
                "版本设置 · 本版：" + (plan.Releases.FirstOrDefault(release => release.Id == plan.CurrentReleaseId)?.Name ?? "未设置"), true);
            if (!showVersions) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                var options = new[] { "未设置" }.Concat(plan.Releases.Select(release => release.Name)).ToArray();
                currentVersionIndex = EditorGUILayout.Popup("本版", Mathf.Clamp(currentVersionIndex, 0, options.Length - 1), options);
                using (new EditorGUI.DisabledScope(error != null))
                    if (GUILayout.Button("保存版本设置", GUILayout.Width(105f)))
                    {
                        var next = DevelopmentContentPlanStore.Copy(plan);
                        next.CurrentReleaseId = currentVersionIndex == 0 ? string.Empty : next.Releases[currentVersionIndex - 1].Id;
                        Save(next);
                    }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                newVersion = EditorGUILayout.TextField("新增版本", newVersion);
                using (new EditorGUI.DisabledScope(error != null || string.IsNullOrWhiteSpace(newVersion)))
                    if (GUILayout.Button("新增", GUILayout.Width(55f)))
                    {
                        if (plan.Releases.Any(release => string.Equals(release.Name, newVersion.Trim(), StringComparison.OrdinalIgnoreCase)))
                            message = "该版本已登记，请从本版或目标版本中选择。";
                        else
                        {
                            var next = DevelopmentContentPlanStore.Copy(plan);
                            next.Releases.Add(new DevelopmentContentRelease { Id = Guid.NewGuid().ToString("N"), Name = newVersion.Trim() });
                            if (Save(next)) newVersion = string.Empty;
                        }
                    }
            }
        }

        private void DrawColumns(DevelopmentContentRow row)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(125f), GUILayout.ExpandWidth(true)))
                {
                    if (row == null) GUILayout.Label("内容", EditorStyles.miniLabel);
                    else if (GUILayout.Button(row.Title, EditorStyles.linkLabel)) TrySelect(row);
                }
                GUILayout.Label((row == null ? null : DevelopmentContentLabels.Stage(row.Record)) ?? "开发进度", GUILayout.Width(65f));
                GUILayout.Label((row == null ? null : DevelopmentContentLabels.Target(plan, row.Record)) ?? "目标版本", GUILayout.Width(100f));
                GUILayout.Label((row == null ? null : DevelopmentContentLabels.Scope(row.Record)) ?? "用户可见范围", GUILayout.Width(95f));
                GUILayout.Label(row?.Record?.NextAction ?? (row == null ? "下一步" : "尚未登记"), EditorStyles.wordWrappedMiniLabel, GUILayout.Width(180f));
            }
        }

        private bool CanLeaveDraft()
        {
            if (!dirty) { message = null; return true; }
            message = "当前草稿尚未保存，请保存或取消后再切换内容。";
            return false;
        }

        private void TrySelect(DevelopmentContentRow row)
        {
            if (row.Key == selectedKey || !CanLeaveDraft()) return;
            Select(row);
        }

        private void Select(DevelopmentContentRow row)
        {
            selectedKey = row.Key;
            draft = row.Record?.Copy() ?? new DevelopmentContentRecord
            { Id = options.ProjectId + ".feature." + Guid.NewGuid().ToString("N"), Title = row.Title, SourceKey = row.Source.Key };
            dirty = false;
        }

        private void DrawDetail(bool inFilter)
        {
            using (EditorUiGUILayout.Section(string.IsNullOrEmpty(draft.Title) ? "新内容" : draft.Title))
            {
                GUILayout.Label(draft.Id + (inFilter ? string.Empty : " · 所选项不在当前筛选内"), EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                draft.Title = EditorGUILayout.TextField("内容名称", draft.Title);
                draft.Stage = (ContentDevelopmentStage)EditorGUILayout.Popup("开发进度", (int)draft.Stage, DevelopmentContentLabels.Stages);
                var availability = (ContentAvailability)EditorGUILayout.Popup("用户可见范围", (int)draft.Availability, DevelopmentContentLabels.Availability);
                if (availability != draft.Availability && availability == ContentAvailability.InternalOnly) draft.TargetReleaseId = string.Empty;
                draft.Availability = availability;
                string[] targetIds = new[] { string.Empty, DevelopmentContentPlan.LaterReleaseId }.Concat(plan.Releases.Select(release => release.Id)).ToArray();
                string[] targetNames = new[] { "待排期", "后续版本（未定）" }.Concat(plan.Releases.Select(release => release.Name + (release.Id == plan.CurrentReleaseId ? " · 本版" : string.Empty))).ToArray();
                int targetIndex = Array.IndexOf(targetIds, draft.TargetReleaseId ?? string.Empty);
                if (targetIndex < 0)
                {
                    targetIds = targetIds.Concat(new[] { draft.TargetReleaseId }).ToArray();
                    targetNames = targetNames.Concat(new[] { "原目标已变更，请重新选择" }).ToArray();
                    targetIndex = targetIds.Length - 1;
                }
                using (new EditorGUI.DisabledScope(draft.Availability == ContentAvailability.InternalOnly))
                    draft.TargetReleaseId = targetIds[EditorGUILayout.Popup("目标版本", targetIndex, targetNames)];
                draft.ResponsibleChat = EditorGUILayout.TextField("负责聊天", draft.ResponsibleChat);
                draft.NextAction = EditorGUILayout.TextField("下一步", draft.NextAction);
                if (draft.Availability == ContentAvailability.Released)
                    draft.ReleaseEvidence = EditorGUILayout.TextField(new GUIContent("实际发布依据", "实际发布版本或发布记录；不以本地提交和可试用推断上线。"), draft.ReleaseEvidence);
                bool edited = EditorGUI.EndChangeCheck();
                showSources = EditorGUILayout.Foldout(showSources, "试用入口与来源", true);
                EditorGUI.BeginChangeCheck();
                var inventory = source.Entries;
                if (showSources)
                {
                    string[] sourceKeys = new[] { string.Empty }.Concat(inventory.Select(entry => entry.Key)).ToArray();
                    int sourceIndex = Array.IndexOf(sourceKeys, draft.SourceKey ?? string.Empty);
                    string[] sourceNames = new[] { "独立登记" }.Concat(inventory.Select(entry => entry.Group + " / " + entry.Title)).ToArray();
                    if (sourceIndex < 0)
                    {
                        sourceKeys = sourceKeys.Concat(new[] { draft.SourceKey }).ToArray();
                        sourceNames = sourceNames.Concat(new[] { "原入口已变更（保留关联）" }).ToArray();
                        sourceIndex = sourceKeys.Length - 1;
                    }
                    int nextSource = EditorGUILayout.Popup("关联清单入口", sourceIndex, sourceNames);
                    if (nextSource != sourceIndex) draft.SourceKey = sourceKeys[nextSource];
                    draft.Entrance = EditorGUILayout.TextField("补充试用入口", draft.Entrance);
                    draft.ResourcePath = EditorGUILayout.TextField("项目内资源路径", draft.ResourcePath);
                    draft.ChatUrl = EditorGUILayout.TextField("聊天链接", draft.ChatUrl);
                    draft.Revision = EditorGUILayout.TextField("来源提交／记录", draft.Revision);
                }
                if (EditorGUI.EndChangeCheck() || edited) { dirty = true; RememberDraft(); }
                var linked = inventory.FirstOrDefault(entry => entry.Key == draft.SourceKey);
                string entrance = string.IsNullOrWhiteSpace(draft.Entrance) ? linked?.Entrance : draft.Entrance;
                if (showSources && linked != null)
                {
                    GUILayout.Label(linked.Entrance, EditorStyles.wordWrappedLabel);
                    GUILayout.Label(linked.Boundary, EditorStyles.wordWrappedMiniLabel);
                    foreach (var link in linked.Links)
                    {
                        string path = ResolveResourcePath(link.Value);
                        if (path != null && GUILayout.Button("查看：" + link.Key, EditorStyles.miniButton)) OpenLocal(path);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(error != null))
                        if (GUILayout.Button(plan.Records.Any(record => record.Id == draft.Id) ? "保存" : "登记此项", GUILayout.Width(80f)))
                        {
                            var next = DevelopmentContentPlanStore.Copy(plan);
                            next.Records.RemoveAll(record => record.Id == draft.Id);
                            next.Records.Add(draft.Copy());
                            if (Save(next)) { selectedKey = draft.Id; dirty = false; RememberDraft(); Reload(); }
                        }
                    if (GUILayout.Button("取消草稿", GUILayout.Width(80f))) { dirty = false; RememberDraft(); draft = null; store = null; Reload(); }
                    GUILayout.FlexibleSpace();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(entrance)))
                        if (GUILayout.Button("复制试用入口", GUILayout.Width(100f))) EditorGUIUtility.systemCopyBuffer = entrance;
                    string resource = ResolveResourcePath(draft.ResourcePath);
                    if (!string.IsNullOrWhiteSpace(draft.ResourcePath) && resource != null &&
                        GUILayout.Button("查看资源", GUILayout.Width(80f))) OpenLocal(resource);
                    if (Uri.TryCreate(draft.ChatUrl, UriKind.Absolute, out var chat) && chat.Scheme == "thread" &&
                        GUILayout.Button("打开负责聊天", GUILayout.Width(100f))) UnityEngine.Application.OpenURL(chat.AbsoluteUri);
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void RememberDraft() => SessionState.SetString(SessionPrefix + "Draft",
            dirty && draft != null ? JsonConvert.SerializeObject(draft) : string.Empty);

        private string ResolveResourcePath(string path) => DevelopmentContentPaths.ResolveProjectPath(ProjectRoot, path);

        private bool Save(DevelopmentContentPlan next)
        {
            if (store == null)
            {
                message = "计划尚未成功载入；请保全或取消当前草稿，再重新载入计划。";
                return false;
            }
            try
            {
                store.Save(next);
                plan = next;
                rows = DevelopmentContentRow.Merge(plan, source.Entries);
                currentVersionIndex = 1 + plan.Releases.FindIndex(release => release.Id == plan.CurrentReleaseId);
                message = "计划已保存；试用开关和用户开放状态仍由各自入口决定。";
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException)
            { message = exception.Message; return false; }
        }

        private static void OpenLocal(string path)
        {
            if (Directory.Exists(path)) EditorUtility.RevealInFinder(path);
            else if (File.Exists(path)) UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(path, 1);
            else Debug.LogWarning("内容清单链接已失效：" + path);
        }
    }

}
