using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#if UNITY_LOCALIZATION
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
#endif

namespace ZeroEngine.Editor.Localization
{
    /// <summary>
    /// 翻译完整性检查工具
    /// 扫描所有 String Table，显示各语言覆盖率
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class TranslationCheckerWindow : EditorWindow
    {
        [MenuItem("ZeroEngine/Localization/Translation Checker")]
        public static void ShowWindow()
        {
            var window = GetWindow<TranslationCheckerWindow>("Translation Checker");
            window.minSize = new Vector2(400, 300);
        }

#if UNITY_LOCALIZATION

        private Vector2 _scrollPos;
        private List<TableReport> _reports = new();
        private bool _isScanning;
        private string _lastScanTime;

        private class TableReport
        {
            public string TableName;
            public int TotalKeys;
            public Dictionary<string, LocaleReport> LocaleReports = new();
            public bool IsFoldout = true;
        }

        private class LocaleReport
        {
            public string LocaleCode;
            public string LocaleName;
            public int TranslatedCount;
            public float Coverage => TotalKeys > 0 ? (float)TranslatedCount / TotalKeys : 0;
            public int TotalKeys;
            public List<string> MissingKeys = new();
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Translation Checker", "Review localization coverage and missing entries");
            DrawToolbar();

            if (_reports.Count == 0)
            {
                DrawEmptyState();
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            DrawReports();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Scan All Tables", EditorStyles.toolbarButton, GUILayout.Width(120)))
            {
                ScanAllTables();
            }

            if (_reports.Count > 0 && GUILayout.Button("Export Report", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                ExportReport();
            }

            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(_lastScanTime))
            {
                GUILayout.Label($"Last scan: {_lastScanTime}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawEmptyState()
        {
            EditorGUILayout.Space(50);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Click 'Scan All Tables' to analyze translation coverage.", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawReports()
        {
            foreach (var report in _reports)
            {
                DrawTableReport(report);
                EditorGUILayout.Space(5);
            }
        }

        private void DrawTableReport(TableReport report)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Table header
            EditorGUILayout.BeginHorizontal();
            report.IsFoldout = EditorGUILayout.Foldout(report.IsFoldout, $"{report.TableName} ({report.TotalKeys} keys)", true);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (report.IsFoldout)
            {
                EditorGUI.indentLevel++;

                foreach (var localeReport in report.LocaleReports.Values.OrderByDescending(r => r.Coverage))
                {
                    DrawLocaleReport(localeReport);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLocaleReport(LocaleReport report)
        {
            EditorGUILayout.BeginHorizontal();

            // Locale name
            EditorGUILayout.LabelField($"{report.LocaleName} ({report.LocaleCode})", GUILayout.Width(150));

            // Progress bar
            var rect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.Height(18));
            EditorGUI.ProgressBar(rect, report.Coverage, $"{report.TranslatedCount}/{report.TotalKeys} ({report.Coverage:P0})");

            // Status icon
            var iconContent = report.Coverage >= 1f
                ? EditorGUIUtility.IconContent("d_greenLight")
                : report.Coverage >= 0.8f
                    ? EditorGUIUtility.IconContent("d_orangeLight")
                    : EditorGUIUtility.IconContent("d_redLight");
            GUILayout.Label(iconContent, GUILayout.Width(20), GUILayout.Height(18));

            EditorGUILayout.EndHorizontal();

            // Missing keys (if any and expanded)
            if (report.MissingKeys.Count > 0 && report.Coverage < 1f)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Missing ({report.MissingKeys.Count}):", EditorStyles.miniBoldLabel);

                var displayKeys = report.MissingKeys.Take(5);
                foreach (var key in displayKeys)
                {
                    EditorGUILayout.LabelField($"  - {key}", EditorStyles.miniLabel);
                }

                if (report.MissingKeys.Count > 5)
                {
                    EditorGUILayout.LabelField($"  ... and {report.MissingKeys.Count - 5} more", EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
        }

        private void ScanAllTables()
        {
            _reports.Clear();
            _isScanning = true;

            try
            {
                var locales = LocalizationSettings.AvailableLocales.Locales;
                var stringTables = GetAllStringTables();

                foreach (var tableCollection in stringTables)
                {
                    var report = new TableReport
                    {
                        TableName = tableCollection.TableCollectionName
                    };

                    // Get all keys from the shared table data
                    var sharedData = tableCollection.SharedData;
                    var allKeys = sharedData.Entries.Select(e => e.Key).ToList();
                    report.TotalKeys = allKeys.Count;

                    // Check each locale
                    foreach (var locale in locales)
                    {
                        var table = tableCollection.GetTable(locale.Identifier) as StringTable;
                        var localeReport = new LocaleReport
                        {
                            LocaleCode = locale.Identifier.Code,
                            LocaleName = locale.LocaleName,
                            TotalKeys = report.TotalKeys
                        };

                        if (table != null)
                        {
                            foreach (var key in allKeys)
                            {
                                var entry = table.GetEntry(key);
                                if (entry != null && !string.IsNullOrEmpty(entry.LocalizedValue))
                                {
                                    localeReport.TranslatedCount++;
                                }
                                else
                                {
                                    localeReport.MissingKeys.Add(key);
                                }
                            }
                        }
                        else
                        {
                            localeReport.MissingKeys.AddRange(allKeys);
                        }

                        report.LocaleReports[locale.Identifier.Code] = localeReport;
                    }

                    _reports.Add(report);
                }

                _lastScanTime = System.DateTime.Now.ToString("HH:mm:ss");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TranslationChecker] Scan failed: {e.Message}");
            }
            finally
            {
                _isScanning = false;
                Repaint();
            }
        }

        private List<StringTableCollection> GetAllStringTables()
        {
            var guids = AssetDatabase.FindAssets("t:StringTableCollection");
            var tables = new List<StringTableCollection>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var table = AssetDatabase.LoadAssetAtPath<StringTableCollection>(path);
                if (table != null)
                    tables.Add(table);
            }

            return tables;
        }

        private void ExportReport()
        {
            var path = EditorUtility.SaveFilePanel("Export Translation Report", "", "translation_report.md", "md");
            if (string.IsNullOrEmpty(path)) return;

            var sb = new StringBuilder();
            sb.AppendLine("# Translation Completeness Report");
            sb.AppendLine($"\nGenerated: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            foreach (var report in _reports)
            {
                sb.AppendLine($"## {report.TableName}");
                sb.AppendLine($"\nTotal Keys: {report.TotalKeys}\n");
                sb.AppendLine("| Locale | Coverage | Missing |");
                sb.AppendLine("|--------|----------|---------|");

                foreach (var locale in report.LocaleReports.Values.OrderByDescending(r => r.Coverage))
                {
                    sb.AppendLine($"| {locale.LocaleName} ({locale.LocaleCode}) | {locale.Coverage:P0} | {locale.MissingKeys.Count} |");
                }

                sb.AppendLine();

                // List missing keys
                foreach (var locale in report.LocaleReports.Values.Where(r => r.MissingKeys.Count > 0))
                {
                    sb.AppendLine($"### Missing Keys - {locale.LocaleName}");
                    sb.AppendLine();
                    foreach (var key in locale.MissingKeys)
                    {
                        sb.AppendLine($"- `{key}`");
                    }
                    sb.AppendLine();
                }
            }

            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log($"[TranslationChecker] Report exported to: {path}");
            EditorUtility.RevealInFinder(path);
        }

#else
        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Translation Checker", "Unity Localization is required for coverage checks");
            EditorGUILayout.Space(20);
            EditorGUILayout.HelpBox(
                "Unity Localization package is not installed.\n\n" +
                "Install via Package Manager:\n" +
                "Window > Package Manager > + > Add package by name\n" +
                "Enter: com.unity.localization",
                MessageType.Warning);
        }
#endif
    }
}
