using System;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace ZGS.DataToolkit.Editor.Tests
{
    public sealed class DataAuthoringReportHistoryTests
    {
        [Test]
        public void CreateEntry_CreatesAuditableMetadataAndNormalizedOutputFolder()
        {
            var exportedAt = new DateTime(2026, 5, 26, 10, 0, 0);

            var entry = DataAuthoringReportHistory.CreateEntry(
                "C:\\Reports\\quality.tsv",
                "PROFILE_A",
                "QualityReportExport",
                issueCount: 9,
                changeCount: 2,
                exportedAt);

            Assert.AreEqual("PROFILE_A", entry.ProfileId);
            Assert.AreEqual("QualityReportExport", entry.ActionKind);
            Assert.AreEqual(9, entry.IssueCount);
            Assert.AreEqual(2, entry.ChangeCount);
            Assert.AreEqual("C:/Reports", entry.OutputFolder);
            Assert.AreEqual("C:\\Reports\\quality.tsv", entry.Path);
            Assert.AreEqual(exportedAt, entry.ExportedAt);
        }

        [Test]
        public void CreateUpdatedEntries_RetainsNewestUniquePaths()
        {
            var first = DataAuthoringReportHistory.CreateEntry("C:/Reports/old.tsv", "P", "A", 2, 0, new DateTime(2026, 5, 26, 10, 0, 0));
            var second = DataAuthoringReportHistory.CreateEntry("C:/Reports/new.tsv", "P", "A", 3, 0, new DateTime(2026, 5, 26, 11, 0, 0));
            var replacement = DataAuthoringReportHistory.CreateEntry("C:/Reports/old.tsv", "P", "A", 4, 0, new DateTime(2026, 5, 26, 12, 0, 0));

            var entries = DataAuthoringReportHistory.CreateUpdatedEntries(new[] { first, second }, replacement, maxEntryCount: 5);

            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual("C:/Reports/old.tsv", entries[0].Path);
            Assert.AreEqual(4, entries[0].IssueCount);
            Assert.AreEqual("C:/Reports/new.tsv", entries[1].Path);
        }

        [Test]
        public void SerializeAndDeserialize_PreservesCurrentMetadata()
        {
            var exportedAt = new DateTime(2026, 5, 26, 14, 0, 0, DateTimeKind.Local);
            var entry = new DataAuthoringReportHistoryEntry(
                "PROFILE_A",
                "BatchApply",
                issueCount: 7,
                changeCount: 3,
                "C:/Reports",
                "C:/Reports/apply.tsv",
                exportedAt);

            var serialized = DataAuthoringReportHistory.Serialize(new[] { entry });
            var entries = DataAuthoringReportHistory.Deserialize(
                serialized,
                defaultProfileId: "DEFAULT_PROFILE",
                defaultActionKind: "DefaultAction",
                maxEntryCount: 5);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("PROFILE_A", entries[0].ProfileId);
            Assert.AreEqual("BatchApply", entries[0].ActionKind);
            Assert.AreEqual(7, entries[0].IssueCount);
            Assert.AreEqual(3, entries[0].ChangeCount);
            Assert.AreEqual("C:/Reports", entries[0].OutputFolder);
            Assert.AreEqual("C:/Reports/apply.tsv", entries[0].Path);
            Assert.AreEqual(exportedAt, entries[0].ExportedAt);
        }

        [Test]
        public void Deserialize_ReadsLegacyRowsWithCallerDefaults()
        {
            var exportedAt = new DateTime(2026, 5, 26, 15, 0, 0, DateTimeKind.Utc);
            var path = "C:/Reports/legacy.tsv";
            var legacy = $"{exportedAt:O}\t5\t{Convert.ToBase64String(Encoding.UTF8.GetBytes(path))}\n";

            var entries = DataAuthoringReportHistory.Deserialize(
                legacy,
                defaultProfileId: "PROFILE_DEFAULT",
                defaultActionKind: "QualityReportExport",
                maxEntryCount: 5);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("PROFILE_DEFAULT", entries[0].ProfileId);
            Assert.AreEqual("QualityReportExport", entries[0].ActionKind);
            Assert.AreEqual(5, entries[0].IssueCount);
            Assert.AreEqual(0, entries[0].ChangeCount);
            Assert.AreEqual("C:/Reports", entries[0].OutputFolder);
            Assert.AreEqual(path, entries[0].Path);
        }
    }
}
