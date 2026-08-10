using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace ZeroEngine.EditorUI.Tests
{
    public sealed class EditorUiCoverageContractTests
    {
        [Serializable]
        private sealed class CoverageDocument
        {
            public int schemaVersion;
            public CoverageRecord[] records;
        }

        [Serializable]
        private sealed class CoverageRecord
        {
            public string targetId;
            public string descriptorFullId;
            public bool countsTowardModuleTotal;
            public string sourcePath;
            public string typeName;
            public string technology;
            public string migrationStatus;
            public string integrationMethod;
        }

        [Test]
        public void CoverageFixture_HasClosedUpstreamInventory()
        {
            var package = PackageInfo.FindForAssembly(typeof(EditorUiCoverageContractTests).Assembly);
            Assert.That(package, Is.Not.Null);
            var fixturePath = Path.Combine(
                package.resolvedPath,
                "Tests",
                "Editor",
                "Fixtures",
                "EditorUiWindowCoverage.json");
            var document = JsonUtility.FromJson<CoverageDocument>(File.ReadAllText(fixturePath));

            Assert.That(document, Is.Not.Null);
            Assert.That(document.schemaVersion, Is.EqualTo(1));
            Assert.That(document.records, Has.Length.EqualTo(30));
            Assert.That(document.records.Count(record => record.countsTowardModuleTotal), Is.EqualTo(28));
            Assert.That(document.records.Count(record => !string.IsNullOrEmpty(record.descriptorFullId)), Is.EqualTo(28));
            Assert.That(document.records.Select(record => record.targetId).Distinct().Count(), Is.EqualTo(30));
            Assert.That(document.records.Select(record => record.typeName).Distinct().Count(), Is.EqualTo(29));
            Assert.That(document.records.All(record => record.migrationStatus == "migrated"), Is.True);
            Assert.That(document.records.All(record => !string.IsNullOrEmpty(record.sourcePath)), Is.True);
            Assert.That(document.records.All(record => !string.IsNullOrEmpty(record.integrationMethod)), Is.True);
            Assert.That(document.records.Single(record => record.targetId == "pob-mounted/data-toolkit").descriptorFullId, Is.Empty.Or.Null);
            Assert.That(document.records.Single(record => record.targetId == "dashboard-shell").countsTowardModuleTotal, Is.False);
            Assert.That(
                document.records.Where(record => record.descriptorFullId != null && record.descriptorFullId.Contains("formula-"))
                    .Select(record => record.typeName)
                    .Distinct()
                    .Single(),
                Is.EqualTo("ZeroEngine.Formula.Editor.FormulaWorkbenchWindow"));
        }
    }
}
