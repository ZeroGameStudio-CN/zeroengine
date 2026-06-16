using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor.Tests
{
    public sealed class DataAuthoringValidationServiceTests
    {
        [Test]
        public void ValidateProfile_AggregatesIssuesFromAllAdaptersAndAssets()
        {
            var firstAdapter = new ValidationAdapter(
                "Characters",
                CreateAsset("char_001"),
                "characterId");
            var secondAdapter = new ValidationAdapter(
                "Enemies",
                CreateAsset("enemy_001"),
                "enemyId");
            var profile = new DataAuthoringProfile(
                "TEST_VALIDATION",
                "Validation Test",
                new IDataAuthoringAssetAdapter[] { firstAdapter, secondAdapter });

            var issues = DataAuthoringValidationService.ValidateProfile(profile);

            Assert.That(issues.Select(issue => issue.StableId), Does.Contain("char_001"));
            Assert.That(issues.Select(issue => issue.StableId), Does.Contain("enemy_001"));
            Assert.That(issues.Select(issue => issue.FieldPath), Does.Contain("characterId"));
            Assert.That(issues.Select(issue => issue.FieldPath), Does.Contain("enemyId"));
        }

        [Test]
        public void AddValidationReportSheet_WritesStableColumnsAndRows()
        {
            var workbook = new TabularWorkbook();
            var issues = new[]
            {
                DataAuthoringIssue.Error(
                    "Assets/Data/Characters/Test.asset",
                    "CharacterData",
                    "char_test",
                    "initialSkills[0]",
                    "初始技能引用为空。")
            };

            DataAuthoringValidationService.AddValidationReportSheet(workbook, issues);

            var sheet = workbook.Sheets.Single();
            Assert.AreEqual("ValidationReport", sheet.Name);
            CollectionAssert.AreEqual(
                new[] { "severity", "assetPath", "assetType", "stableId", "fieldPath", "message" },
                sheet.Columns);
            Assert.AreEqual("Error", sheet.Rows[0].Cells[0]);
            Assert.AreEqual("char_test", sheet.Rows[0].Cells[3]);
            Assert.AreEqual("initialSkills[0]", sheet.Rows[0].Cells[4]);
        }

        private static TestAsset CreateAsset(string id)
        {
            var asset = ScriptableObject.CreateInstance<TestAsset>();
            asset.id = id;
            return asset;
        }

        private sealed class TestAsset : ScriptableObject
        {
            public string id;
        }

        private sealed class ValidationAdapter : IDataAuthoringAssetAdapter
        {
            private readonly TestAsset _asset;
            private readonly string _fieldPath;

            public ValidationAdapter(string groupId, TestAsset asset, string fieldPath)
            {
                GroupId = groupId;
                DisplayName = groupId;
                _asset = asset;
                _fieldPath = fieldPath;
            }

            public string GroupId { get; }
            public string DisplayName { get; }
            public int Order => 0;

            public IReadOnlyList<DataAuthoringAssetRecord> GetAssets()
            {
                return new[]
                {
                    new DataAuthoringAssetRecord(_asset, $"Assets/Tests/{_asset.id}.asset", _asset.id, _asset.id, GroupId, null)
                };
            }

            public Object CreateAsset()
            {
                return ScriptableObject.CreateInstance<TestAsset>();
            }

            public Object DuplicateAsset(Object source)
            {
                return Object.Instantiate(source);
            }

            public void DrawInspector(Object asset)
            {
            }

            public IReadOnlyList<DataAuthoringIssue> Validate(Object asset)
            {
                var testAsset = (TestAsset)asset;
                return new[]
                {
                    DataAuthoringIssue.Warning(
                        $"Assets/Tests/{testAsset.id}.asset",
                        nameof(TestAsset),
                        testAsset.id,
                        _fieldPath,
                        "test issue")
                };
            }

            public void AddExportSheets(TabularWorkbook workbook)
            {
            }
        }
    }
}
