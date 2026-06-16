using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.AbilitySystem.Editor
{
    public sealed class AbilityAuthoringWindow : EditorWindow
    {
        private static readonly GUIContent ProfileLabel = new("配置集", "选择当前项目注册的 Ability/Skill 编辑配置。");
        private static readonly GUIContent SearchLabel = new("搜索", "按 id、名称、描述或项目自定义搜索文本过滤资产。");
        private static readonly GUIContent CreateButton = new("新建", "通过当前配置集创建技能资产。");
        private static readonly GUIContent DuplicateButton = new("复制", "复制当前选中的技能资产。");
        private static readonly GUIContent PingButton = new("定位", "在 Project 窗口定位当前资产。");
        private static readonly GUIContent SaveButton = new("保存", "保存当前资产和 AssetDatabase。");
        private static readonly GUIContent ValidateAllButton = new("全部校验", "校验当前配置集中的全部资产。");
        private static readonly GUIContent EmptyMessage = new("没有已注册的 Ability 编辑配置。", "请确认项目侧已提供 AbilityAuthoringProvider。");

        private int _selectedProfileIndex;
        private Vector2 _assetScroll;
        private Vector2 _detailScroll;
        private string _search = string.Empty;
        private string _requestedProfileId;
        private string _lastResult;
        private Object _selectedAsset;
        private bool _showProjectSection = true;
        private bool _showAbilitySummary = true;
        private bool _showAbilityLogic = true;
        private bool _showBatchGovernance;
        private bool _showDebugRawAbility;
        private BatchGovernanceFilter _batchFilter = BatchGovernanceFilter.All;
        private readonly List<AbilityBatchValidationRecord> _batchRecords = new();

        [MenuItem("ZGS/Ability/Ability Workbench")]
        public static void Open()
        {
            Open(null);
        }

        public static void Open(string profileId)
        {
            AbilityAuthoringRegistry.RefreshFromProviders();
            var window = GetWindow<AbilityAuthoringWindow>("Ability 工作台");
            window.minSize = new Vector2(960f, 600f);
            window._requestedProfileId = profileId;
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), AbilityAuthoringStyles.BackgroundColor);
            var profiles = AbilityAuthoringRegistry.GetProfiles();
            if (profiles.Count == 0)
            {
                EditorGUILayout.HelpBox(EmptyMessage.text, MessageType.Info);
                return;
            }

            ApplyRequestedProfile(profiles);
            DrawProfileToolbar(profiles);

            var profile = profiles[_selectedProfileIndex];
            var allRecords = profile.Adapter.GetAssets().ToList();
            if (!IsSelectedAssetInCurrentProfile(allRecords))
            {
                ResetSelectedAsset();
            }

            var records = allRecords
                .Where(record => profile.Adapter.MatchesSearch(record, _search))
                .ToList();
            var selectedRecord = allRecords.FirstOrDefault(record => record.Asset == _selectedAsset);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawAssetList(profile, records);
                GUILayout.Space(8f);
                DrawSelectedAsset(profile, selectedRecord);
            }

            DrawBatchGovernance();

            if (!string.IsNullOrWhiteSpace(_lastResult))
            {
                EditorGUILayout.HelpBox(_lastResult, MessageType.None);
            }
        }

        private void ApplyRequestedProfile(IReadOnlyList<AbilityAuthoringProfile> profiles)
        {
            if (string.IsNullOrWhiteSpace(_requestedProfileId))
            {
                _selectedProfileIndex = Mathf.Clamp(_selectedProfileIndex, 0, profiles.Count - 1);
                return;
            }

            var previousIndex = Mathf.Clamp(_selectedProfileIndex, 0, profiles.Count - 1);
            var index = profiles
                .Select((profile, i) => new { profile, i })
                .FirstOrDefault(item => item.profile.ProfileId == _requestedProfileId)?.i;
            if (index.HasValue)
            {
                if (index.Value != previousIndex)
                {
                    HandleProfileChanged(index.Value);
                }
                else
                {
                    _selectedProfileIndex = index.Value;
                }
            }

            _requestedProfileId = null;
        }

        private void DrawProfileToolbar(IReadOnlyList<AbilityAuthoringProfile> profiles)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(ProfileLabel, GUILayout.Width(48f));
                var nextProfileIndex = EditorGUILayout.Popup(
                    Mathf.Clamp(_selectedProfileIndex, 0, profiles.Count - 1),
                    profiles.Select(profile => profile.Title).ToArray(),
                    GUILayout.Width(220f));
                if (nextProfileIndex != _selectedProfileIndex)
                {
                    HandleProfileChanged(nextProfileIndex);
                }

                GUILayout.Label(
                    new GUIContent(profiles[_selectedProfileIndex].Description, profiles[_selectedProfileIndex].Description),
                    AbilityAuthoringStyles.ToolbarDescription,
                    GUILayout.Width(260f));

                GUILayout.FlexibleSpace();
                if (GUILayout.Button(CreateButton, EditorStyles.toolbarButton, GUILayout.Width(64f)))
                {
                    CreateAsset(profiles[_selectedProfileIndex]);
                }

                using (new EditorGUI.DisabledScope(!IsSelectedAssetInCurrentProfile(profiles[_selectedProfileIndex])))
                {
                    if (GUILayout.Button(DuplicateButton, EditorStyles.toolbarButton, GUILayout.Width(64f)))
                    {
                        DuplicateAsset(profiles[_selectedProfileIndex]);
                    }

                    if (GUILayout.Button(PingButton, EditorStyles.toolbarButton, GUILayout.Width(64f)))
                    {
                        EditorGUIUtility.PingObject(_selectedAsset);
                    }

                    if (GUILayout.Button(SaveButton, EditorStyles.toolbarButton, GUILayout.Width(64f)))
                    {
                        SaveSelectedAsset();
                    }
                }

                if (GUILayout.Button(ValidateAllButton, EditorStyles.toolbarButton, GUILayout.Width(84f)))
                {
                    ValidateAll(profiles[_selectedProfileIndex]);
                }
            }
        }

        private void DrawAssetList(AbilityAuthoringProfile profile, IReadOnlyList<AbilityAuthoringAssetRecord> records)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(AbilityAuthoringStyles.AssetListWidth)))
            {
                AbilityAuthoringStyles.DrawPanel(() =>
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(SearchLabel, GUILayout.Width(44f));
                        _search = EditorGUILayout.TextField(_search);
                        if (GUILayout.Button("清空", GUILayout.Width(52f)))
                        {
                            _search = string.Empty;
                        }
                    }

                    EditorGUILayout.LabelField($"{records.Count} 个资产", EditorStyles.miniBoldLabel);
                });

                _assetScroll = EditorGUILayout.BeginScrollView(_assetScroll);
                if (records.Count == 0)
                {
                    AbilityAuthoringStyles.DrawEmptyState("没有匹配的技能资产。");
                }
                else
                {
                    foreach (var record in records)
                    {
                        DrawAssetCard(profile, record);
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawAssetCard(AbilityAuthoringProfile profile, AbilityAuthoringAssetRecord record)
        {
            var selected = record.Asset == _selectedAsset;
            var validation = SafeValidateAsset(profile.Adapter, record.Asset);
            var rect = GUILayoutUtility.GetRect(
                0f,
                AbilityAuthoringStyles.AssetCardHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(AbilityAuthoringStyles.AssetCardHeight));
            rect = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, selected ? AbilityAuthoringStyles.SelectedColor : AbilityAuthoringStyles.ComponentColor);
                DrawStatusMarker(new Rect(rect.x, rect.y, 4f, rect.height), validation);
            }

            if (GUI.Button(rect, new GUIContent(string.Empty, record.AssetPath), GUIStyle.none))
            {
                _selectedAsset = record.Asset;
                _lastResult = string.Empty;
                GUI.FocusControl(null);
            }

            var iconRect = new Rect(rect.x + 12f, rect.y + 12f, AbilityAuthoringStyles.IconSize, AbilityAuthoringStyles.IconSize);
            if (record.Icon != null)
            {
                GUI.DrawTexture(iconRect, record.Icon, ScaleMode.ScaleToFit);
            }

            var textX = iconRect.xMax + 10f;
            var titleRect = new Rect(textX, rect.y + 10f, rect.xMax - textX - 10f, 20f);
            var subtitleRect = new Rect(textX, titleRect.yMax + 4f, rect.xMax - textX - 10f, 18f);
            GUI.Label(titleRect, record.DisplayName, AbilityAuthoringStyles.AssetTitle);
            GUI.Label(subtitleRect, $"{record.Id}  {record.Subtitle}", AbilityAuthoringStyles.AssetSubtitle);
        }

        private void DrawSelectedAsset(AbilityAuthoringProfile profile, AbilityAuthoringAssetRecord selectedRecord)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (_selectedAsset == null)
                {
                    DrawEmptyDetailState();
                    return;
                }

                profile.Adapter.PrepareAsset(_selectedAsset);
                var serializedObject = new SerializedObject(_selectedAsset);
                serializedObject.Update();
                var abilityProperty = profile.Adapter.FindAbilityProperty(serializedObject);
                var selectedValidation = selectedRecord == null
                    ? AbilityAuthoringValidationResult.Warning("当前资产不在配置集中。")
                    : SafeValidateAsset(profile.Adapter, selectedRecord.Asset);

                DrawSelectedAssetHeader(selectedRecord, selectedValidation);

                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

                _showProjectSection = DrawSectionHeader("项目配置", _showProjectSection);
                if (_showProjectSection)
                {
                    profile.Adapter.DrawProjectSections(serializedObject, _selectedAsset);
                }

                _showAbilitySummary = DrawSectionHeader("Ability 概览", _showAbilitySummary);
                if (_showAbilitySummary)
                {
                    DrawAbilitySummaryChips(abilityProperty);
                }

                _showAbilityLogic = DrawSectionHeader("Ability 逻辑", _showAbilityLogic);
                if (_showAbilityLogic)
                {
                    AbilityDefinitionEditorDrawer.Draw(
                        serializedObject,
                        abilityProperty,
                        new AbilityEditorOptions
                        {
                            Labels = AbilityEditorLabels.Chinese(),
                            DrawSummary = false,
                            DrawValidation = true,
                            DrawDebugRawAbility = false,
                            CompactComponentRows = true,
                            CollapseAddSectionsByDefault = true,
                            ShowComponentActionsInMenu = true
                        });
                }

                _showDebugRawAbility = DrawSectionHeader("调试", _showDebugRawAbility);
                if (_showDebugRawAbility)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        if (abilityProperty == null)
                        {
                            EditorGUILayout.HelpBox("当前配置集没有提供 Ability 字段。", MessageType.Warning);
                        }
                        else
                        {
                            EditorGUILayout.PropertyField(abilityProperty, true);
                        }
                    }
                }

                EditorGUILayout.EndScrollView();

                if (serializedObject.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(_selectedAsset);
                }
            }
        }

        private void CreateAsset(AbilityAuthoringProfile profile)
        {
            _selectedAsset = profile.Adapter.CreateAsset();
            SetResult(_selectedAsset == null
                ? AbilityAuthoringValidationResult.Error($"创建失败：{profile.Adapter.GetCreateAssetPath()}")
                : AbilityAuthoringValidationResult.Success($"已创建：{AssetDatabase.GetAssetPath(_selectedAsset)}"));
        }

        private void DuplicateAsset(AbilityAuthoringProfile profile)
        {
            _selectedAsset = profile.Adapter.DuplicateAsset(_selectedAsset);
            SetResult(_selectedAsset == null
                ? AbilityAuthoringValidationResult.Error("复制失败。")
                : AbilityAuthoringValidationResult.Success($"已复制：{AssetDatabase.GetAssetPath(_selectedAsset)}"));
        }

        private void SaveSelectedAsset()
        {
            EditorUtility.SetDirty(_selectedAsset);
            AssetDatabase.SaveAssets();
            SetResult(AbilityAuthoringValidationResult.Success("保存完成。"));
        }

        private void ValidateAll(AbilityAuthoringProfile profile)
        {
            _batchRecords.Clear();
            foreach (var record in profile.Adapter.GetAssets())
            {
                _batchRecords.Add(BuildBatchRecord(profile, record));
            }

            _showBatchGovernance = true;
            _batchFilter = BatchGovernanceFilter.All;
            var errors = _batchRecords.Where(record => record.Status == AbilityAuthoringValidationStatus.Error).ToArray();
            if (errors.Length == 0)
            {
                SetResult(AbilityAuthoringValidationResult.Success($"全部校验通过：{_batchRecords.Count} 个资产。"));
                return;
            }

            SetResult(AbilityAuthoringValidationResult.Error(
                $"校验失败：{errors.Length}/{_batchRecords.Count} 个资产。",
                errors.Select(record => $"{record.AssetPath}: {record.Message}").ToArray()));
        }

        private static AbilityBatchValidationRecord BuildBatchRecord(
            AbilityAuthoringProfile profile,
            AbilityAuthoringAssetRecord record)
        {
            try
            {
                var result = profile.Adapter.ValidateAsset(record.Asset)
                             ?? AbilityAuthoringValidationResult.Error("校验未返回结果。");
                return new AbilityBatchValidationRecord(record, result);
            }
            catch (System.Exception exception)
            {
                return new AbilityBatchValidationRecord(
                    record,
                    AbilityAuthoringValidationResult.Error("校验执行异常。", new[] { exception.Message }));
            }
        }

        private void DrawBatchGovernance()
        {
            if (_batchRecords.Count == 0)
            {
                return;
            }

            _showBatchGovernance = DrawSectionHeader($"批量治理 ({_batchRecords.Count})", _showBatchGovernance);
            if (!_showBatchGovernance)
            {
                return;
            }

            AbilityAuthoringStyles.DrawPanel(() =>
            {
                DrawBatchFilterToolbar();
                var records = _batchRecords
                    .Where(MatchesBatchFilter)
                    .ToArray();
                if (records.Length == 0)
                {
                    AbilityAuthoringStyles.DrawEmptyState("当前过滤条件下没有结果。");
                    return;
                }

                foreach (var record in records)
                {
                    DrawBatchRecordRow(record);
                }
            });
        }

        private void DrawBatchFilterToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                DrawBatchFilterButton(BatchGovernanceFilter.All, "全部");
                DrawBatchFilterButton(BatchGovernanceFilter.Error, "错误");
                DrawBatchFilterButton(BatchGovernanceFilter.Warning, "警告");
                DrawBatchFilterButton(BatchGovernanceFilter.Success, "有效");
                GUILayout.FlexibleSpace();
                GUILayout.Label("只读报告，不会自动修改资产。", EditorStyles.miniLabel);
            }
        }

        private void DrawBatchFilterButton(BatchGovernanceFilter filter, string label)
        {
            if (GUILayout.Toggle(_batchFilter == filter, label, EditorStyles.toolbarButton, GUILayout.Width(48f)))
            {
                _batchFilter = filter;
            }
        }

        private bool MatchesBatchFilter(AbilityBatchValidationRecord record)
        {
            return _batchFilter switch
            {
                BatchGovernanceFilter.Error => record.Status == AbilityAuthoringValidationStatus.Error,
                BatchGovernanceFilter.Warning => record.Status == AbilityAuthoringValidationStatus.Warning,
                BatchGovernanceFilter.Success => record.Status == AbilityAuthoringValidationStatus.Success,
                _ => true
            };
        }

        private void DrawBatchRecordRow(AbilityBatchValidationRecord record)
        {
            using (new EditorGUILayout.HorizontalScope(AbilityAuthoringStyles.ComponentCard))
            {
                var markerRect = GUILayoutUtility.GetRect(5f, 18f, GUILayout.Width(5f));
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(markerRect, AbilityAuthoringStyles.StatusColor(record.Status));
                }

                if (GUILayout.Button(new GUIContent(record.DisplayName, record.AssetPath), EditorStyles.label, GUILayout.Width(180f)))
                {
                    SelectBatchRecord(record);
                }

                GUILayout.Label(record.Id, EditorStyles.miniLabel, GUILayout.Width(120f));
                GUILayout.Label(GetBatchStatusText(record.Status), EditorStyles.miniBoldLabel, GUILayout.Width(44f));
                GUILayout.Label(new GUIContent(record.Message, record.AssetPath), EditorStyles.miniLabel);
            }
        }

        private void SelectBatchRecord(AbilityBatchValidationRecord record)
        {
            if (record?.Asset == null)
            {
                return;
            }

            _selectedAsset = record.Asset;
            _lastResult = record.Message;
            GUI.FocusControl(null);
        }

        private static string GetBatchStatusText(AbilityAuthoringValidationStatus status)
        {
            return status switch
            {
                AbilityAuthoringValidationStatus.Error => "错误",
                AbilityAuthoringValidationStatus.Warning => "警告",
                _ => "有效"
            };
        }

        private void SetResult(AbilityAuthoringValidationResult result)
        {
            result ??= AbilityAuthoringValidationResult.Error("操作未返回结果。");
            _lastResult = result.Details.Count == 0
                ? result.Message
                : $"{result.Message}\n{string.Join("\n", result.Details)}";
        }

        private void HandleProfileChanged(int profileIndex)
        {
            _selectedProfileIndex = profileIndex;
            ResetSelectedAsset();
            _batchRecords.Clear();
            _assetScroll = Vector2.zero;
            _detailScroll = Vector2.zero;
        }

        private void ResetSelectedAsset()
        {
            _selectedAsset = null;
            _lastResult = string.Empty;
        }

        private static void DrawEmptyDetailState()
        {
            AbilityAuthoringStyles.DrawPanel(() =>
            {
                AbilityAuthoringStyles.DrawEmptyState("请选择左侧技能资产。");
            }, GUILayout.ExpandHeight(true));
        }

        private static void DrawSelectedAssetHeader(
            AbilityAuthoringAssetRecord selectedRecord,
            AbilityAuthoringValidationResult validation)
        {
            AbilityAuthoringStyles.DrawPanel(() =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (selectedRecord?.Icon != null)
                    {
                        GUILayout.Label(
                            selectedRecord.Icon,
                            GUILayout.Width(AbilityAuthoringStyles.IconSize),
                            GUILayout.Height(AbilityAuthoringStyles.IconSize));
                    }

                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(
                            selectedRecord?.DisplayName ?? "未命名技能",
                            AbilityAuthoringStyles.HeaderTitle);
                        EditorGUILayout.LabelField(
                            new GUIContent(
                                selectedRecord == null
                                    ? "当前资产不在配置集中。"
                                    : $"{selectedRecord.Id}  {selectedRecord.Subtitle}",
                                selectedRecord?.AssetPath),
                            AbilityAuthoringStyles.HeaderSubtitle);
                    }

                    GUILayout.FlexibleSpace();
                    DrawStatusPill(validation);
                }
            });
        }

        private bool IsSelectedAssetInCurrentProfile(AbilityAuthoringProfile profile)
        {
            return _selectedAsset != null
                   && IsSelectedAssetInCurrentProfile(profile.Adapter.GetAssets());
        }

        private bool IsSelectedAssetInCurrentProfile(IReadOnlyList<AbilityAuthoringAssetRecord> records)
        {
            return _selectedAsset != null
                   && records.Any(record => record.Asset == _selectedAsset);
        }

        private static AbilityAuthoringValidationResult SafeValidateAsset(
            IAbilityAuthoringAssetAdapter adapter,
            Object asset)
        {
            try
            {
                return adapter.ValidateAsset(asset)
                       ?? AbilityAuthoringValidationResult.Error("校验未返回结果。");
            }
            catch (System.Exception exception)
            {
                return AbilityAuthoringValidationResult.Error(
                    "校验执行异常。",
                    new[] { exception.Message });
            }
        }

        private static void DrawStatusMarker(Rect rect, AbilityAuthoringValidationResult result)
        {
            result ??= AbilityAuthoringValidationResult.Error("校验未返回结果。");
            EditorGUI.DrawRect(rect, AbilityAuthoringStyles.StatusColor(result.Status));
        }

        private static void DrawStatusPill(AbilityAuthoringValidationResult result)
        {
            result ??= AbilityAuthoringValidationResult.Error("校验未返回结果。");
            var text = result.Status switch
            {
                AbilityAuthoringValidationStatus.Error => "错误",
                AbilityAuthoringValidationStatus.Warning => "警告",
                _ => "有效"
            };
            var rect = GUILayoutUtility.GetRect(74f, 20f, AbilityAuthoringStyles.Pill, GUILayout.Width(74f));
            GUI.Label(rect, text, AbilityAuthoringStyles.Pill);
            var markerRect = new Rect(rect.x + 8f, rect.y + 7f, 6f, 6f);
            EditorGUI.DrawRect(markerRect, AbilityAuthoringStyles.StatusColor(result.Status));
        }

        private static bool DrawSectionHeader(string title, bool open)
        {
            return EditorGUILayout.Foldout(open, title, true, AbilityAuthoringStyles.SectionHeader);
        }

        private static void DrawAbilitySummaryChips(SerializedProperty abilityProperty)
        {
            AbilityAuthoringStyles.DrawPanel(() =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawChip("消耗", GetRelativeValue(abilityProperty, nameof(AbilityDefinition.ResourceCost)));
                    DrawChip("冷却", GetRelativeValue(abilityProperty, nameof(AbilityDefinition.CooldownTurns)));
                    DrawChip("目标", GetRelativeValue(abilityProperty, nameof(AbilityDefinition.TargetMode)));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawChip("触发器", GetListCount(abilityProperty, nameof(AbilityDefinition.Triggers)).ToString());
                    DrawChip("条件", GetListCount(abilityProperty, nameof(AbilityDefinition.Conditions)).ToString());
                    DrawChip("效果", GetListCount(abilityProperty, nameof(AbilityDefinition.Effects)).ToString());
                }
            });
        }

        private static void DrawChip(string label, string value)
        {
            GUILayout.Label($"{label}  {value}", AbilityAuthoringStyles.Chip, GUILayout.MinWidth(96f));
        }

        private static string GetRelativeValue(SerializedProperty parent, string relativeName)
        {
            var property = parent?.FindPropertyRelative(relativeName);
            if (property == null)
            {
                return "-";
            }

            return property.propertyType switch
            {
                SerializedPropertyType.Integer => property.intValue.ToString(),
                SerializedPropertyType.Float => property.floatValue.ToString("0.###"),
                SerializedPropertyType.Enum => GetEnumDisplayValue(property),
                SerializedPropertyType.String => string.IsNullOrWhiteSpace(property.stringValue) ? "-" : property.stringValue,
                _ => property.displayName
            };
        }

        private static string GetEnumDisplayValue(SerializedProperty property)
        {
            if (property == null)
            {
                return "-";
            }

            if (property.type == nameof(AbilityTargetMode))
            {
                return (AbilityTargetMode)property.enumValueIndex switch
                {
                    AbilityTargetMode.SelectedTargets => "选中目标",
                    AbilityTargetMode.Self => "自身",
                    AbilityTargetMode.AllTargets => "全部目标",
                    _ => property.enumDisplayNames[property.enumValueIndex]
                };
            }

            if (property.type == nameof(AbilityTargetRelationship))
            {
                return (AbilityTargetRelationship)property.enumValueIndex switch
                {
                    AbilityTargetRelationship.Any => "任意",
                    AbilityTargetRelationship.Ally => "友方",
                    AbilityTargetRelationship.Enemy => "敌方",
                    _ => property.enumDisplayNames[property.enumValueIndex]
                };
            }

            return property.enumDisplayNames[property.enumValueIndex];
        }

        private static int GetListCount(SerializedProperty parent, string relativeName)
        {
            return parent?.FindPropertyRelative(relativeName)?.arraySize ?? 0;
        }

        private enum BatchGovernanceFilter
        {
            All,
            Error,
            Warning,
            Success
        }

        private sealed class AbilityBatchValidationRecord
        {
            public AbilityBatchValidationRecord(
                AbilityAuthoringAssetRecord assetRecord,
                AbilityAuthoringValidationResult result)
            {
                Asset = assetRecord?.Asset;
                AssetPath = assetRecord?.AssetPath ?? string.Empty;
                Id = assetRecord?.Id ?? string.Empty;
                DisplayName = assetRecord?.DisplayName ?? Id;
                Status = result?.Status ?? AbilityAuthoringValidationStatus.Error;
                Message = result?.Message ?? "校验未返回结果。";
            }

            public Object Asset { get; }
            public string AssetPath { get; }
            public string Id { get; }
            public string DisplayName { get; }
            public AbilityAuthoringValidationStatus Status { get; }
            public string Message { get; }
        }
    }
}
