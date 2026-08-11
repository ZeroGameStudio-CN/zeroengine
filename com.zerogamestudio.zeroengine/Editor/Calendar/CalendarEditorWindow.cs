using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using ZeroEngine.Calendar;

namespace ZeroEngine.Editor.Calendar
{
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class CalendarEditorWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private int _selectedTab;
        private readonly string[] _tabNames = { "事件列表", "日历预览", "季节配置" };

        private List<CalendarEventSO> _events = new List<CalendarEventSO>();
        private CalendarEventSO _selectedEvent;
        private string _searchFilter = "";

        // 日历预览
        private int _previewYear = 1;
        private int _previewMonth = 1;

        public static void ShowWindow()
        {
            var window = GetWindow<CalendarEditorWindow>("Calendar Editor");
            window.minSize = new Vector2(700, 500);
        }

        private void OnEnable()
        {
            RefreshAssets();
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Calendar Editor", "Configure events, seasons, and calendar previews");
            DrawToolbar();

            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            EditorGUILayout.Space(5);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_selectedTab)
            {
                case 0: DrawEventList(); break;
                case 1: DrawCalendarPreview(); break;
                case 2: DrawSeasonConfig(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(50)))
                RefreshAssets();

            GUILayout.FlexibleSpace();

            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200));

            if (GUILayout.Button("+事件", EditorStyles.toolbarButton, GUILayout.Width(50)))
                CreateNewEvent();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawEventList()
        {
            foreach (var evt in _events)
            {
                if (evt == null) continue;
                if (!MatchesFilter(evt.EventData.DisplayName, evt.EventData.EventId)) continue;

                bool isSelected = _selectedEvent == evt;
                var style = isSelected ? "selectionRect" : "box";

                EditorGUILayout.BeginHorizontal(style);

                // 类型图标
                var typeIcon = GetEventTypeIcon(evt.EventData.Type);       
                GUILayout.Label(typeIcon, GUILayout.Width(24), GUILayout.Height(24));

                // 信息
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(evt.EventData.DisplayName, EditorStyles.boldLabel);

                var startDate = evt.EventData.StartDate;
                var endDate = evt.EventData.EndDate;
                string dateRange = $"{startDate.Month}月{startDate.Day}日 - {endDate.Month}月{endDate.Day}日";
                EditorGUILayout.LabelField($"{evt.EventData.Type} | {dateRange}", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                GUILayout.FlexibleSpace();

                // 标签
                if (IsRecurring(evt.EventData)) GUILayout.Label("循环", EditorStyles.miniLabel);
                if (evt.EventData.ReminderDaysBefore > 0) GUILayout.Label($"提醒:{evt.EventData.ReminderDaysBefore}天", EditorStyles.miniLabel);

                if (GUILayout.Button("选择", GUILayout.Width(50)))
                {
                    _selectedEvent = evt;
                }

                if (GUILayout.Button("编辑", GUILayout.Width(50)))
                    Selection.activeObject = evt;

                EditorGUILayout.EndHorizontal();
            }

            if (_events.Count == 0)
            {
                EditorGUILayout.HelpBox("暂无日历事件，点击 +事件 创建", MessageType.Info);
            }
        }

        private void DrawCalendarPreview()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("◀", GUILayout.Width(30)))
            {
                _previewMonth--;
                if (_previewMonth < 1) { _previewMonth = 12; _previewYear--; }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"第 {_previewYear} 年 {_previewMonth} 月", EditorStyles.boldLabel, GUILayout.Width(120));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("▶", GUILayout.Width(30)))
            {
                _previewMonth++;
                if (_previewMonth > 12) { _previewMonth = 1; _previewYear++; }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 季节显示
            var season = GetSeasonForMonth(_previewMonth);
            EditorGUILayout.LabelField($"季节: {season}", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.Space(5);

            // 星期标题
            EditorGUILayout.BeginHorizontal();
            string[] weekDays = { "日", "一", "二", "三", "四", "五", "六" };
            foreach (var day in weekDays)
            {
                GUILayout.Label(day, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(40));
            }
            EditorGUILayout.EndHorizontal();

            // 日历格子
            int daysInMonth = 28; // 简化：每月28天
            int startDayOfWeek = (_previewMonth - 1) % 7; // 简化计算

            int currentDay = 1;
            for (int week = 0; week < 5; week++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int dayOfWeek = 0; dayOfWeek < 7; dayOfWeek++)
                {
                    if ((week == 0 && dayOfWeek < startDayOfWeek) || currentDay > daysInMonth)
                    {
                        GUILayout.Label("", GUILayout.Width(40), GUILayout.Height(40));
                    }
                    else
                    {
                        var date = new GameDate(_previewYear, _previewMonth, currentDay);
                        var eventsOnDay = GetEventsOnDate(date);

                        var bgColor = GUI.backgroundColor;
                        if (eventsOnDay.Count > 0)
                            GUI.backgroundColor = new Color(0.5f, 0.8f, 0.5f);

                        if (GUILayout.Button(currentDay.ToString(), GUILayout.Width(40), GUILayout.Height(40)))
                        {
                            ShowEventsPopup(date, eventsOnDay);
                        }

                        GUI.backgroundColor = bgColor;
                        currentDay++;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            // 本月事件列表
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("本月事件:", EditorStyles.boldLabel);

            var monthEvents = GetEventsInMonth(_previewYear, _previewMonth);
            if (monthEvents.Count == 0)
            {
                EditorGUILayout.LabelField("  (无事件)", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var evt in monthEvents)
                {
                    EditorGUILayout.LabelField($"  • {evt.EventData.DisplayName} ({evt.EventData.StartDate.Day}日)", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawSeasonConfig()
        {
            EditorGUILayout.LabelField("季节配置", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox("季节按月份划分:\n" +
                "• 春季 (Spring): 3-5月\n" +
                "• 夏季 (Summer): 6-8月\n" +
                "• 秋季 (Autumn): 9-11月\n" +
                "• 冬季 (Winter): 12-2月", MessageType.Info);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("季节统计", EditorStyles.boldLabel);

            var seasonCounts = new Dictionary<Season, int>
            {
                { Season.Spring, 0 },
                { Season.Summer, 0 },
                { Season.Autumn, 0 },
                { Season.Winter, 0 }
            };

            foreach (var evt in _events)
            {
                if (evt == null) continue;
                var season = GetSeasonForMonth(evt.EventData.StartDate.Month);
                seasonCounts[season]++;
            }

            foreach (var kvp in seasonCounts)
            {
                EditorGUILayout.LabelField($"  {kvp.Key}: {kvp.Value} 个事件");
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("事件类型统计", EditorStyles.boldLabel);

            var typeCounts = new Dictionary<CalendarEventType, int>();
            foreach (var evt in _events)
            {
                if (evt == null) continue;
                if (!typeCounts.ContainsKey(evt.EventData.Type))
                    typeCounts[evt.EventData.Type] = 0;
                typeCounts[evt.EventData.Type]++;
            }

            foreach (var kvp in typeCounts)
            {
                EditorGUILayout.LabelField($"  {kvp.Key}: {kvp.Value} 个事件");
            }
        }

        private void RefreshAssets()
        {
            _events.Clear();

            foreach (string guid in AssetDatabase.FindAssets("t:CalendarEventSO"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<CalendarEventSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) _events.Add(asset);
            }

            // 按开始日期排序
            _events.Sort((a, b) =>
            {
                var dateA = a.EventData.StartDate;
                var dateB = b.EventData.StartDate;
                int monthCompare = dateA.Month.CompareTo(dateB.Month);
                return monthCompare != 0 ? monthCompare : dateA.Day.CompareTo(dateB.Day);
            });
        }

        private void CreateNewEvent()
        {
            string path = EditorUtility.SaveFilePanelInProject("创建日历事件", "New Event", "asset", "");
            if (!string.IsNullOrEmpty(path))
            {
                var asset = CreateInstance<CalendarEventSO>();
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
                Selection.activeObject = asset;
                RefreshAssets();
            }
        }

        private bool MatchesFilter(params string[] values)
        {
            if (string.IsNullOrEmpty(_searchFilter)) return true;
            string filter = _searchFilter.ToLower();
            foreach (var v in values)
                if (!string.IsNullOrEmpty(v) && v.ToLower().Contains(filter))
                    return true;
            return false;
        }

        private GUIContent GetEventTypeIcon(CalendarEventType type)
        {
            return type switch
            {
                CalendarEventType.OneTime => EditorGUIUtility.IconContent("d_Favorite"),
                CalendarEventType.Daily => EditorGUIUtility.IconContent("d_Refresh"),
                CalendarEventType.Weekly => EditorGUIUtility.IconContent("d_Animation.Play"),
                CalendarEventType.Monthly => EditorGUIUtility.IconContent("d_PreMatQuad"),
                CalendarEventType.Yearly => EditorGUIUtility.IconContent("d_PreMatSphere"),
                CalendarEventType.Seasonal => EditorGUIUtility.IconContent("d_TreeEditor.Refresh"),
                CalendarEventType.Custom => EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow"),
                _ => EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow")
            };
        }

        private static bool IsRecurring(CalendarEventData data)
        {
            return data != null && data.Type != CalendarEventType.OneTime;
        }

        private Season GetSeasonForMonth(int month)
        {
            return month switch
            {
                3 or 4 or 5 => Season.Spring,
                6 or 7 or 8 => Season.Summer,
                9 or 10 or 11 => Season.Autumn,
                _ => Season.Winter
            };
        }

        private List<CalendarEventSO> GetEventsOnDate(GameDate date)
        {
            var result = new List<CalendarEventSO>();
            foreach (var evt in _events)
            {
                if (evt != null && evt.EventData.IsActiveOn(date))
                    result.Add(evt);
            }
            return result;
        }

        private List<CalendarEventSO> GetEventsInMonth(int year, int month)
        {
            var result = new List<CalendarEventSO>();
            foreach (var evt in _events)
            {
                if (evt == null) continue;
                var startMonth = evt.EventData.StartDate.Month;
                var endMonth = evt.EventData.EndDate.Month;

                if (startMonth <= month && month <= endMonth)
                    result.Add(evt);
            }
            return result;
        }

        private void ShowEventsPopup(GameDate date, List<CalendarEventSO> events)
        {
            if (events.Count == 0)
            {
                EditorUtility.DisplayDialog("日历", $"{date.Month}月{date.Day}日 无事件", "确定");
            }
            else
            {
                var menu = new GenericMenu();
                menu.AddDisabledItem(new GUIContent($"{date.Month}月{date.Day}日"));
                menu.AddSeparator("");
                foreach (var evt in events)
                {
                    menu.AddItem(new GUIContent(evt.EventData.DisplayName), false, () => Selection.activeObject = evt);
                }
                menu.ShowAsContext();
            }
        }
    }
}
