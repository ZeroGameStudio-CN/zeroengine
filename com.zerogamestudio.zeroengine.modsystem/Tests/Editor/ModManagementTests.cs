using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ZeroEngine.ModSystem.Tests.Editor
{
    public sealed class ModManagementTests
    {
        [Test]
        public void Build_NullReport_ReturnsEmptySnapshot()
        {
            Assert.That(ModManagementProjection.Build(null, null), Is.Empty);
        }

        [Test]
        public void Build_OrdersByDisplayNameThenStableId()
        {
            var report = Report(
                Manifest("z", "same"),
                Manifest("a", "Same"),
                Manifest("b", "Alpha"));

            Assert.That(
                ModManagementProjection.Build(report, null).Select(item => item.Id),
                Is.EqualTo(new[] { "b", "a", "z" }));
        }

        [Test]
        public void Build_ProjectsLoadedAndPostLoadDisableAsRestartRequired()
        {
            var report = Report(Manifest("loaded", "Loaded"));

            ModManagementItem loaded = ModManagementProjection.Build(report, null).Single();
            ModManagementItem disabled = ModManagementProjection.Build(report, new[] { "loaded" }).Single();

            Assert.That(loaded.Status, Is.EqualTo(ModManagementStatus.Loaded));
            Assert.That(loaded.IsEnabled, Is.True);
            Assert.That(disabled.Status, Is.EqualTo(ModManagementStatus.RestartRequired));
            Assert.That(disabled.ReasonCode, Is.EqualTo("restart_required"));
            Assert.That(disabled.IsEnabled, Is.False);
        }

        [Test]
        public void Build_ProjectsStartupDisabledAndReEnabledAsExpected()
        {
            var report = new ModLoadReport(
                Array.Empty<ModManifest>(),
                new[]
                {
                    Issue("mod_disabled", "disabled"),
                    Issue("mod_disabled", "reenabled")
                });

            IReadOnlyList<ModManagementItem> items = ModManagementProjection.Build(
                report,
                new[] { "disabled" });

            Assert.That(Item(items, "disabled").Status, Is.EqualTo(ModManagementStatus.Disabled));
            Assert.That(Item(items, "disabled").IsEnabled, Is.False);
            Assert.That(Item(items, "reenabled").Status, Is.EqualTo(ModManagementStatus.RestartRequired));
            Assert.That(Item(items, "reenabled").IsEnabled, Is.True);
        }

        [Test]
        public void Build_ProjectsDependencyAndOtherFailuresWithoutExposingDiagnostics()
        {
            var report = new ModLoadReport(
                Array.Empty<ModManifest>(),
                new[]
                {
                    new ModLoadIssue(ModIssueSeverity.Error, "dependency_disabled", "dependent", "C:/secret", "dependency disabled:base"),
                    new ModLoadIssue(ModIssueSeverity.Error, "schema_invalid", "broken", "C:/secret", "absolute path C:/secret")
                });

            IReadOnlyList<ModManagementItem> items = ModManagementProjection.Build(report, new[] { "base" });

            Assert.That(Item(items, "dependent").Status, Is.EqualTo(ModManagementStatus.Disabled));
            Assert.That(Item(items, "broken").Status, Is.EqualTo(ModManagementStatus.Failed));
            Assert.That(Item(items, "broken").ReasonCode, Is.EqualTo("schema_invalid"));
            Assert.That(string.Join("|", items.Select(item => item.ReasonCode)), Does.Not.Contain("C:/secret"));
        }

        [Test]
        public void Build_DeduplicatesIssuesAndDoesNotMutateInputs()
        {
            var issues = new List<ModLoadIssue>
            {
                Issue("warning", "duplicate", ModIssueSeverity.Warning),
                Issue("error", "duplicate", ModIssueSeverity.Error)
            };
            var disabledIds = new List<string> { "other" };
            var report = new ModLoadReport(Array.Empty<ModManifest>(), issues);

            IReadOnlyList<ModManagementItem> items = ModManagementProjection.Build(report, disabledIds);

            Assert.That(items.Count, Is.EqualTo(1));
            Assert.That(items[0].ReasonCode, Is.EqualTo("error"));
            Assert.That(issues.Select(issue => issue.ReasonCode), Is.EqualTo(new[] { "warning", "error" }));
            Assert.That(disabledIds, Is.EqualTo(new[] { "other" }));
        }

        [TestCase(ModActivationChangeStatus.Unchanged)]
        [TestCase(ModActivationChangeStatus.Rejected)]
        [TestCase(ModActivationChangeStatus.PersistenceFailed)]
        public void Service_NonChangedStoreResult_PreservesSnapshot(ModActivationChangeStatus resultStatus)
        {
            var store = new FakeStore();
            store.NextResult = new ModActivationChangeResult(resultStatus, "store_result");
            var service = new ModManagementService(Report(Manifest("mod", "Mod")), store);

            ModActivationChangeResult result = service.SetDisabled("mod", true);

            Assert.That(result.Status, Is.EqualTo(resultStatus));
            Assert.That(service.BuildSnapshot().Single().Status, Is.EqualTo(ModManagementStatus.Loaded));
        }

        [Test]
        public void Service_ChangedStoreResult_RebuildsSnapshotFromPersistedState()
        {
            var store = new FakeStore();
            var service = new ModManagementService(Report(Manifest("mod", "Mod")), store);

            ModActivationChangeResult result = service.SetDisabled("mod", true);

            Assert.That(result.Status, Is.EqualTo(ModActivationChangeStatus.Changed));
            Assert.That(service.BuildSnapshot().Single().Status, Is.EqualTo(ModManagementStatus.RestartRequired));
        }

        [Test]
        public void Service_ExposesExternalChangeSignalWithoutChangingSnapshot()
        {
            var signal = new FakeSignal { RestartRequired = true };
            var service = new ModManagementService(Report(Manifest("mod", "Mod")), new FakeStore(), signal);

            Assert.That(service.ExternalRestartRequired, Is.True);
            Assert.That(service.BuildSnapshot().Single().Status, Is.EqualTo(ModManagementStatus.Loaded));
        }

        private static ModLoadReport Report(params ModManifest[] manifests)
        {
            return new ModLoadReport(manifests, Array.Empty<ModLoadIssue>());
        }

        private static ModManifest Manifest(string id, string name)
        {
            return new ModManifest
            {
                Id = id,
                Name = name,
                Author = "author",
                Version = "1",
                SourceId = "source"
            };
        }

        private static ModLoadIssue Issue(
            string reasonCode,
            string modId,
            ModIssueSeverity severity = ModIssueSeverity.Error)
        {
            return new ModLoadIssue(severity, reasonCode, modId, string.Empty, string.Empty);
        }

        private static ModManagementItem Item(IEnumerable<ModManagementItem> items, string id)
        {
            return items.Single(item => item.Id == id);
        }

        private sealed class FakeStore : IModActivationStore
        {
            private readonly HashSet<string> disabledIds = new(StringComparer.Ordinal);

            public IReadOnlyCollection<string> DisabledModIds => disabledIds;
            public ModActivationChangeResult NextResult { get; set; } =
                new(ModActivationChangeStatus.Changed);

            public ModActivationChangeResult SetDisabled(string modId, bool disabled)
            {
                if (NextResult.Status != ModActivationChangeStatus.Changed)
                    return NextResult;

                if (disabled)
                    disabledIds.Add(modId);
                else
                    disabledIds.Remove(modId);
                return NextResult;
            }
        }

        private sealed class FakeSignal : IExternalModChangeSignal
        {
            public bool RestartRequired { get; set; }
        }
    }
}
