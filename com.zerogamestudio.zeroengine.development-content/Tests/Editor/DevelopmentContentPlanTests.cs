using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ZeroEngine.DevelopmentContent;

namespace ZeroEngine.DevelopmentContent.Tests.Editor
{
    // Core contract: durable planning, explicit player release and optimistic preservation of concurrent edits.
    public sealed class DevelopmentContentPlanTests
    {
        private string root;
        private string path;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "ZeroEngineContentPlan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            path = Path.Combine(root, "plan.json");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string file in Directory.GetFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }

        [Test]
        public void MissingPlan_IsReadOnly_AndRoundTripPreservesStableIdentity()
        {
            var store = new DevelopmentContentPlanStore(path, "fixture");
            var plan = store.Load();
            Assert.That(File.Exists(path), Is.False, "Opening inventory does not register contents or invent a version.");
            Assert.That(plan.CurrentReleaseId, Is.Empty);
            plan.Releases.Add(new DevelopmentContentRelease { Id = "release-a", Name = "版本 A" });
            plan.CurrentReleaseId = "release-a";
            plan.Records.Add(new DevelopmentContentRecord
            {
                Id = "feature-a", Title = "原名", Stage = ContentDevelopmentStage.Previewable,
                Availability = ContentAvailability.DevelopmentOnly, TargetReleaseId = "release-a",
                ResponsibleChat = "负责聊天", NextAction = "确认采用", SourceKey = "分类\0原名"
            });
            store.Save(plan);
            plan.Records[0].Title = "改名";
            store.Save(plan);
            var loaded = new DevelopmentContentPlanStore(path, "fixture").Load();
            Assert.That(loaded.Records.Single().Id, Is.EqualTo("feature-a"));
            Assert.That(loaded.Records[0].Title, Is.EqualTo("改名"));
            Assert.That(loaded.Records[0].SourceKey, Is.EqualTo("分类\0原名"));
            Assert.That(loaded.Records[0].ResponsibleChat, Is.EqualTo("负责聊天"));
            Assert.That(loaded.Records[0].NextAction, Is.EqualTo("确认采用"));
            Assert.That(loaded.Releases.Single(release => release.Id == loaded.Records[0].TargetReleaseId).Name, Is.EqualTo("版本 A"));
            Assert.That(Directory.GetFiles(root), Has.Length.EqualTo(1), "Atomic writes leave no sidecars.");
        }

        [Test]
        public void StaleSave_RefusesOverwrite_AndKeepsLatestBytes()
        {
            var first = new DevelopmentContentPlanStore(path, "fixture");
            var second = new DevelopmentContentPlanStore(path, "fixture");
            var a = first.Load();
            var b = second.Load();
            a.Records.Add(new DevelopmentContentRecord { Id = "first", Title = "第一条" });
            first.Save(a);
            byte[] expected = File.ReadAllBytes(path);
            b.Records.Add(new DevelopmentContentRecord { Id = "second", Title = "第二条" });
            Assert.Throws<IOException>(() => second.Save(b));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(expected));
            Assert.That(b.Records.Single().Id, Is.EqualTo("second"), "The caller retains its draft.");
            Assert.That(Directory.GetFiles(root), Has.Length.EqualTo(1));
        }

        [Test]
        public void InvalidOrFuturePlan_CannotBeLoadedOrSilentlyReplaced()
        {
            var store = new DevelopmentContentPlanStore(path, "fixture");
            var original = store.Load();
            store.Save(original);
            string valid = File.ReadAllText(path);
            foreach (string bad in new[] { "{", valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"),
                         valid.Replace("\"projectId\": \"fixture\"", "\"projectId\": \"other\"") })
            {
                File.WriteAllText(path, bad);
                Assert.Catch(() => store.Load());
                Assert.Throws<IOException>(() => store.Save(original));
                Assert.That(File.ReadAllText(path), Is.EqualTo(bad));
            }
        }

        [Test]
        public void ReadyUnreleased_RequiresKnownScope_AndReleaseNeedsEvidence()
        {
            var plan = new DevelopmentContentPlan { ProjectId = "fixture" };
            var record = new DevelopmentContentRecord { Id = "feature", Title = "功能", Stage = ContentDevelopmentStage.Ready };
            plan.Records.Add(record);
            Assert.That(record.ReadyUnreleased, Is.False, "Unknown visibility is not a confirmed unreleased state.");
            foreach (var scope in new[] { ContentAvailability.DevelopmentOnly, ContentAvailability.DisabledByDefault, ContentAvailability.TestOnly })
            {
                record.Availability = scope;
                Assert.That(record.ReadyUnreleased, Is.True);
            }
            record.Availability = ContentAvailability.Released;
            Assert.That(record.ReadyUnreleased, Is.False);
            Assert.Throws<FormatException>(() => plan.Validate("fixture"));
            record.ReleaseEvidence = "公开版本 A / 发布记录";
            Assert.DoesNotThrow(() => plan.Validate("fixture"));
            Assert.That(plan.InView(record, ContentPlanView.Unplanned), Is.False);
            Assert.That(plan.InView(record, ContentPlanView.All), Is.True, "Released content stays searchable without filling the backlog.");
            record.Availability = ContentAvailability.InternalOnly;
            Assert.That(record.ReadyUnreleased, Is.False);
            Assert.That(plan.InView(record, ContentPlanView.Unplanned), Is.False);
            Assert.That(plan.InView(record, ContentPlanView.All), Is.True);
            record.Availability = ContentAvailability.DevelopmentOnly;
            record.Stage = ContentDevelopmentStage.Abandoned;
            Assert.That(plan.InView(record, ContentPlanView.Unplanned), Is.False);
            Assert.That(plan.InView(record, ContentPlanView.All), Is.True);
        }

        [Test]
        public void VersionRollover_PreservesOriginalTargetsAndDoesNotMarkReleased()
        {
            var plan = new DevelopmentContentPlan { ProjectId = "fixture", CurrentReleaseId = "a" };
            plan.Releases.Add(new DevelopmentContentRelease { Id = "a", Name = "A" });
            plan.Releases.Add(new DevelopmentContentRelease { Id = "b", Name = "B" });
            var record = new DevelopmentContentRecord
            { Id = "feature", Title = "功能", TargetReleaseId = "a", Stage = ContentDevelopmentStage.Ready, Availability = ContentAvailability.DevelopmentOnly };
            plan.Records.Add(record);
            Assert.That(plan.InView(record, ContentPlanView.Current), Is.True);
            plan.CurrentReleaseId = "b";
            Assert.That(plan.InView(record, ContentPlanView.Current), Is.False);
            Assert.That(plan.InView(record, ContentPlanView.OtherVersions), Is.True);
            Assert.That(plan.Releases.Single(release => release.Id == record.TargetReleaseId).Name, Is.EqualTo("A"));
            Assert.That(record.ReadyUnreleased, Is.True);
            Assert.That(record.TargetReleaseId, Is.EqualTo("a"));
        }

        [Test]
        public void InventoryMerge_DoesNotInventProgress_AndRetainsOrphanedRecords()
        {
            var entry = new DevelopmentContentInventoryEntry("tool.import", "工具", "入口", "app import", "本地试用");
            var plan = new DevelopmentContentPlan { ProjectId = "fixture" };
            var unregistered = DevelopmentContentRow.Merge(plan, new[] { entry }).Single();
            Assert.That(unregistered.Record, Is.Null);
            Assert.That(unregistered.InView(plan, ContentPlanView.Unplanned), Is.True);
            plan.Records.Add(new DevelopmentContentRecord { Id = "stable", Title = "新名称", SourceKey = entry.Key, ResponsibleChat = "武器聊天", NextAction = "确认范围" });
            var linked = DevelopmentContentRow.Merge(plan, new[] { entry }).Single();
            Assert.That(linked.Source, Is.SameAs(entry));
            Assert.That(linked.Title, Is.EqualTo("新名称"));
            Assert.That(linked.Matches(" 武器聊天 "), Is.True);
            Assert.That(linked.Matches("确认范围"), Is.True);
            var orphan = DevelopmentContentRow.Merge(plan, Array.Empty<DevelopmentContentInventoryEntry>()).Single();
            Assert.That(orphan.Key, Is.EqualTo("stable"));
            Assert.That(orphan.Source, Is.Null);
            Assert.That(orphan.Title, Is.EqualTo("新名称"));
        }

        [Test]
        public void Validation_RejectsDuplicateIdsSourcesAndDanglingVersions()
        {
            var plan = new DevelopmentContentPlan { ProjectId = "fixture" };
            var record = new DevelopmentContentRecord { Id = "stable", Title = "内容", SourceKey = "entry" };
            plan.Records.Add(record);
            plan.Records.Add(record.Copy());
            Assert.Throws<FormatException>(() => plan.Validate("fixture"));
            plan.Records[1].Id = "other";
            Assert.Throws<FormatException>(() => plan.Validate("fixture"));
            plan.Records.RemoveAt(1);
            record.TargetReleaseId = "missing";
            Assert.Throws<FormatException>(() => plan.Validate("fixture"));
            record.TargetReleaseId = string.Empty;
            Assert.DoesNotThrow(() => plan.Validate("fixture"));
            plan.Releases.Add(new DevelopmentContentRelease { Id = "a", Name = "版本 A" });
            plan.Releases.Add(new DevelopmentContentRelease { Id = "b", Name = "版本 A" });
            Assert.Throws<FormatException>(() => plan.Validate("fixture"));
        }
    }
}
