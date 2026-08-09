using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ZeroGameStudio.ConfigPipeline.Editor;

namespace ZeroGameStudio.ConfigPipeline.Tests.Editor
{
    [TestFixture]
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class TransactionTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "zgs-config-transaction-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Write("Config/source.txt", "source-v1");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Plan_DoesNotWriteOutputs()
        {
            Write("Generated/current.json", "old");
            IReadOnlyList<ConfigArtifact> artifacts = Artifacts(("Generated/current.json", "new"));

            ConfigPipelinePlan plan = Build(artifacts);

            Assert.That(Read("Generated/current.json"), Is.EqualTo("old"));
            Assert.That(plan.Entries.Single().Action, Is.EqualTo(ConfigPlanAction.Update));
        }

        [Test]
        public void Apply_RejectsInputDriftBeforeWriting()
        {
            Write("Generated/current.json", "old");
            IReadOnlyList<ConfigArtifact> artifacts = Artifacts(("Generated/current.json", "new"));
            ConfigPipelinePlan plan = Build(artifacts);
            Write("Config/source.txt", "source-v2");

            Assert.Throws<ConfigPlanStaleException>(() => Apply(plan, artifacts, () => true));
            Assert.That(Read("Generated/current.json"), Is.EqualTo("old"));
        }

        [Test]
        public void Apply_CommitsCreateUpdateDeleteAndCleansJournal()
        {
            Write("Generated/update.json", "old-update");
            Write("Generated/delete.json", "old-delete");
            IReadOnlyList<ConfigArtifact> artifacts = Artifacts(
                ("Generated/create.json", "created"),
                ("Generated/update.json", "updated"));
            ConfigPipelinePlan plan = Build(artifacts, "Generated/delete.json");

            ConfigApplyResult result = Apply(
                plan,
                artifacts,
                () => Read("Generated/create.json") == "created" &&
                      Read("Generated/update.json") == "updated" &&
                      !File.Exists(Absolute("Generated/delete.json")));

            Assert.That(result.ChangedFileCount, Is.EqualTo(3));
            Assert.That(Directory.Exists(Path.Combine(root, ".zgs-config", "transactions", plan.PlanId)), Is.False);
        }

        [Test]
        public void Apply_RollsBackWhenPostCommitCheckFails()
        {
            Write("Generated/update.json", "old-update");
            Write("Generated/delete.json", "old-delete");
            IReadOnlyList<ConfigArtifact> artifacts = Artifacts(
                ("Generated/create.json", "created"),
                ("Generated/update.json", "updated"));
            ConfigPipelinePlan plan = Build(artifacts, "Generated/delete.json");

            Assert.Throws<InvalidOperationException>(() => Apply(plan, artifacts, () => false));

            Assert.That(File.Exists(Absolute("Generated/create.json")), Is.False);
            Assert.That(Read("Generated/update.json"), Is.EqualTo("old-update"));
            Assert.That(Read("Generated/delete.json"), Is.EqualTo("old-delete"));
        }

        [Test]
        public void RecoverPending_RestoresWholeSetAfterInterruptedCommit()
        {
            Write("Generated/a.json", "old-a");
            Write("Generated/b.json", "old-b");
            IReadOnlyList<ConfigArtifact> artifacts = Artifacts(
                ("Generated/a.json", "new-a"),
                ("Generated/b.json", "new-b"));
            ConfigPipelinePlan plan = Build(artifacts);
            var applier = new ConfigTransactionalApplier();

            Assert.Throws<ConfigSimulatedCrashException>(() => applier.Apply(
                root,
                plan,
                "package@1",
                artifacts,
                () => true,
                ConfigTransactionFault.AfterFirstCommit));

            applier.RecoverPending(root);
            Assert.That(Read("Generated/a.json"), Is.EqualTo("old-a"));
            Assert.That(Read("Generated/b.json"), Is.EqualTo("old-b"));
            Assert.That(Directory.Exists(Path.Combine(root, ".zgs-config", "transactions", plan.PlanId)), Is.False);
        }

        [Test]
        public void RecoverPending_CleansPreparedTransactionBeforeAnyTargetWrite()
        {
            Write("Generated/a.json", "old-a");
            IReadOnlyList<ConfigArtifact> artifacts = Artifacts(("Generated/a.json", "new-a"));
            ConfigPipelinePlan plan = Build(artifacts);
            var applier = new ConfigTransactionalApplier();

            Assert.Throws<ConfigSimulatedCrashException>(() => applier.Apply(
                root,
                plan,
                "package@1",
                artifacts,
                () => true,
                ConfigTransactionFault.AfterPrepared));
            Assert.That(Read("Generated/a.json"), Is.EqualTo("old-a"));

            applier.RecoverPending(root);
            Assert.That(Read("Generated/a.json"), Is.EqualTo("old-a"));
        }

        [Test]
        public void Apply_StopsOnLockedOutputWithoutWriting()
        {
            Write("Generated/a.json", "old-a");
            IReadOnlyList<ConfigArtifact> artifacts = Artifacts(("Generated/a.json", "new-a"));
            ConfigPipelinePlan plan = Build(artifacts);

            using (var locked = new FileStream(
                       Absolute("Generated/a.json"),
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                Assert.Throws<IOException>(() => Apply(plan, artifacts, () => true));
            }

            Assert.That(Read("Generated/a.json"), Is.EqualTo("old-a"));
        }

        [Test]
        public void Plan_RequiresMetaForNewUnityArtifact()
        {
            IReadOnlyList<ConfigArtifact> artifacts = Artifacts(("Assets/Generated/new.json", "{}"));

            Assert.Throws<InvalidOperationException>(() => Build(artifacts));
        }

        [Test]
        public void Apply_PreservesExistingUnityMetaWhenOnlyDataChanges()
        {
            Write("Assets/Generated/data.json", "old");
            Write("Assets/Generated/data.json.meta", "guid: 0123456789abcdef0123456789abcdef\n");
            string metaBefore = ConfigPipelinePlanBuilder.HashFile(Absolute("Assets/Generated/data.json.meta"));
            IReadOnlyList<ConfigArtifact> artifacts = Artifacts(("Assets/Generated/data.json", "new"));
            ConfigPipelinePlan plan = Build(artifacts);

            Apply(plan, artifacts, () => true);

            Assert.That(Read("Assets/Generated/data.json"), Is.EqualTo("new"));
            Assert.That(ConfigPipelinePlanBuilder.HashFile(Absolute("Assets/Generated/data.json.meta")), Is.EqualTo(metaBefore));
        }

        [Test]
        public void Plan_RejectsTraversal()
        {
            IReadOnlyList<ConfigArtifact> artifacts = Artifacts(("../outside.json", "bad"));
            Assert.Throws<ArgumentException>(() => Build(artifacts));
        }

        private ConfigPipelinePlan Build(
            IReadOnlyList<ConfigArtifact> artifacts,
            params string[] deletes)
        {
            return new ConfigPipelinePlanBuilder().Build(
                root,
                "test-config",
                "package@1",
                new[] { "Config/source.txt" },
                artifacts,
                deletes);
        }

        private ConfigApplyResult Apply(
            ConfigPipelinePlan plan,
            IReadOnlyList<ConfigArtifact> artifacts,
            Func<bool> check)
        {
            return new ConfigTransactionalApplier().Apply(root, plan, "package@1", artifacts, check);
        }

        private static IReadOnlyList<ConfigArtifact> Artifacts(
            params (string Path, string Content)[] values)
        {
            return values.Select(value => new ConfigArtifact(
                value.Path,
                new UTF8Encoding(false).GetBytes(value.Content))).ToList();
        }

        private void Write(string relativePath, string content)
        {
            string path = Absolute(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private string Read(string relativePath)
        {
            return File.ReadAllText(Absolute(relativePath), Encoding.UTF8);
        }

        private string Absolute(string relativePath)
        {
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
