using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace ZGS.DataToolkit.Editor.Tests
{
    public sealed class DataAuthoringTableTests
    {
        [Test]
        public void IssueTable_BuildSortsFiltersAndCapsRows()
        {
            var tableType = RequireType("ZGS.DataToolkit.Editor.DataAuthoringIssueTable");
            var issues = new[]
            {
                DataAuthoringIssue.Warning("Assets/Data/Enemies/Bandit.asset", "Enemy", "enemy.bandit", "sprite", "hero missing art"),
                DataAuthoringIssue.Error("Assets/Data/Characters/Hero.asset", "Character", "hero", "portrait", "missing portrait"),
                DataAuthoringIssue.Info("Assets/Data/Items/Herb.asset", "Item", "herb", "id", "not matched")
            };

            var result = InvokeBuild(
                tableType,
                typeof(IEnumerable<DataAuthoringIssue>),
                issues,
                maxRows: 1,
                searchText: "hero");

            var rows = GetRows<DataAuthoringIssue>(result);
            Assert.AreEqual(2, GetProperty<int>(result, "TotalCount"));
            Assert.True(GetProperty<bool>(result, "HasOverflow"));
            Assert.AreEqual(1, rows.Length);
            Assert.AreEqual(DataAuthoringIssueSeverity.Error, rows[0].Severity);
            Assert.AreEqual("hero", rows[0].StableId);
        }

        [Test]
        public void ChangeTable_BuildFiltersAndPreservesDiffFields()
        {
            var tableType = RequireType("ZGS.DataToolkit.Editor.DataAuthoringChangeTable");
            var changes = new[]
            {
                new TabularImportChange("A", TabularImportChangeKind.UpdateScalar, "Enemies", 5, "enemyName", "Assets/Data/Enemies/Bandit.asset", "enemy.bandit", "enemyName", "Old", "New"),
                new TabularImportChange("A", TabularImportChangeKind.CreateAsset, "Characters", 2, "characterId", "Assets/Data/Characters/Hero.asset", "hero", "asset", string.Empty, "Assets/Data/Characters/Hero.asset"),
                new TabularImportChange("A", TabularImportChangeKind.UpdateScalar, "Characters", 3, "characterName", "Assets/Data/Characters/Other.asset", "other", "characterName", "Old", "Other")
            };

            var result = InvokeBuild(
                tableType,
                typeof(IEnumerable<TabularImportChange>),
                changes,
                maxRows: 10,
                searchText: "hero");

            var rows = GetRows<TabularImportChange>(result);
            Assert.AreEqual(1, GetProperty<int>(result, "TotalCount"));
            Assert.False(GetProperty<bool>(result, "HasOverflow"));
            Assert.AreEqual(1, rows.Length);
            Assert.AreEqual(TabularImportChangeKind.CreateAsset, rows[0].Kind);
            Assert.AreEqual("Characters", rows[0].SheetName);
            Assert.AreEqual(2, rows[0].RowNumber);
            Assert.AreEqual("asset", rows[0].FieldPath);
            Assert.AreEqual("Assets/Data/Characters/Hero.asset", rows[0].NewValue);
        }

        [Test]
        public void IssueTable_SearchMatchesLocalizedSeverityLabel()
        {
            var labels = new DataAuthoringIssueTableLabels
            {
                Severity = "严重度",
                Warning = "警告",
                Error = "错误",
                Info = "提示"
            };
            var issues = new[]
            {
                DataAuthoringIssue.Warning("Assets/Data/Characters/Hero.asset", "Character", "hero", "portrait", "missing art")
            };

            var result = DataAuthoringIssueTable.Build(issues, 10, "警告", labels);

            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual(DataAuthoringIssueSeverity.Warning, result.Rows[0].Severity);
        }

        [Test]
        public void DataAuthoringWindow_UsesReusableProblemAndChangeTables()
        {
            var source = ReadEditorScriptSource("ZGS.DataToolkit.Editor.DataAuthoringWindow");

            StringAssert.Contains("DataAuthoringIssueTable.Draw", source);
            StringAssert.Contains("DataAuthoringChangeTable.Draw", source);
            StringAssert.Contains("_issueSearch", source);
            StringAssert.Contains("_changeSearch", source);
            StringAssert.Contains("_importIssueSearch", source);
        }

        private static Type RequireType(string fullName)
        {
            var type = Type.GetType($"{fullName}, ZGS.DataToolkit.Editor");
            Assert.NotNull(type, fullName);
            return type;
        }

        private static object InvokeBuild(Type tableType, Type rowsType, object rows, int maxRows, string searchText)
        {
            var method = tableType.GetMethod(
                "Build",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { rowsType, typeof(int), typeof(string) },
                modifiers: null);
            Assert.NotNull(method, $"{tableType.FullName}.Build");
            return method.Invoke(null, new[] { rows, maxRows, searchText });
        }

        private static T[] GetRows<T>(object result)
        {
            var rows = GetProperty<IEnumerable>(result, "Rows");
            return rows.Cast<T>().ToArray();
        }

        private static T GetProperty<T>(object result, string propertyName)
        {
            var property = result.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property, propertyName);
            return (T)property.GetValue(result);
        }

        private static string ReadEditorScriptSource(string fullName)
        {
            Assert.That(fullName, Is.EqualTo("ZGS.DataToolkit.Editor.DataAuthoringWindow"));
            var path = Path.Combine(
                "Packages",
                "com.zerogamestudio.zeroengine.data-toolkit",
                "Editor",
                "Authoring",
                "DataAuthoringWindow.cs");
            Assert.That(File.Exists(path), Is.True, $"Could not find script source for {fullName} at {path}.");
            return File.ReadAllText(path);
        }
    }
}
