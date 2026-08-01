using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ZeroEngine.ModSystem.Tests.Editor
{
    public sealed class ModLoadOrchestratorTests
    {
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "ZeroEngineModLoadOrchestratorTests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
            Directory.CreateDirectory(tempRoot);
            ModSourceRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ModSourceRegistry.Clear();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }

        [Test]
        public void LoadFromRegisteredSources_ImportsValidEnabledManifest()
        {
            string modRoot = CreateMod("author.mod", "Author Mod");
            var source = new FixedSource(modRoot);
            var importer = new RecordingImporter();
            ModSourceRegistry.Register(source);

            var report = ModLoadOrchestrator.LoadFromRegisteredSources(new[] { importer });

            Assert.That(report.LoadedManifests, Has.Count.EqualTo(1));
            Assert.That(report.Issues, Is.Empty);
            Assert.That(importer.ImportedIds, Is.EquivalentTo(new[] { "author.mod" }));
        }

        [Test]
        public void LoadFromRegisteredSources_WhenOneImporterFails_KeepsOtherImports()
        {
            string modRoot = CreateMod("author.mod", "Author Mod");
            var recordingImporter = new RecordingImporter();
            ModSourceRegistry.Register(new FixedSource(modRoot));

            var report = ModLoadOrchestrator.LoadFromRegisteredSources(new IModContentImporter[]
            {
                new FailingImporter(),
                recordingImporter
            });

            Assert.That(report.LoadedManifests, Is.Empty);
            Assert.That(recordingImporter.ImportedIds, Is.EqualTo(new[] { "author.mod" }));
            Assert.That(report.Issues, Has.Count.EqualTo(1));
            Assert.That(report.Issues[0].Message, Does.Contain("failed"));
        }

        [Test]
        public void LoadFromRegisteredSources_WhenSourceThrows_RecordsIssueAndKeepsOtherSources()
        {
            string modRoot = CreateMod("author.mod", "Author Mod");
            var importer = new RecordingImporter();
            ModSourceRegistry.Register(new ThrowingSource());
            ModSourceRegistry.Register(new FixedSource(modRoot));

            var report = ModLoadOrchestrator.LoadFromRegisteredSources(new[] { importer });

            Assert.That(report.LoadedManifests.Select(manifest => manifest.Id), Is.EqualTo(new[] { "author.mod" }));
            Assert.That(importer.ImportedIds, Is.EqualTo(new[] { "author.mod" }));
            Assert.That(report.Issues, Has.Count.EqualTo(1));
            Assert.That(report.Issues[0].Severity, Is.EqualTo(ModIssueSeverity.Error));
            Assert.That(report.Issues[0].Path, Is.EqualTo("throwing"));
            Assert.That(report.Issues[0].Message, Does.Contain("Source query failed."));
        }

        [Test]
        public void LoadFromRegisteredSources_WithMissingDependency_DoesNotImportDependentMod()
        {
            string modRoot = CreateMod("author.mod", "Author Mod", dependencies: new[] { "missing.mod" });
            var importer = new RecordingImporter();
            ModSourceRegistry.Register(new FixedSource(modRoot));

            ModLoadReport report = ModLoadOrchestrator.LoadFromRegisteredSources(new[] { importer });

            Assert.That(report.LoadedManifests, Is.Empty);
            Assert.That(importer.ImportedIds, Is.Empty);
            Assert.That(report.Issues, Has.Count.EqualTo(1));
            Assert.That(report.Issues[0].Severity, Is.EqualTo(ModIssueSeverity.Error));
            Assert.That(report.Issues[0].Message, Does.Contain("Missing dependency"));
        }

        [Test]
        public void LoadFromRegisteredSources_WithCircularDependency_DoesNotImportCycle()
        {
            string first = CreateMod("first.mod", "First Mod", dependencies: new[] { "second.mod" });
            string second = CreateMod("second.mod", "Second Mod", dependencies: new[] { "first.mod" });
            var importer = new RecordingImporter();
            ModSourceRegistry.Register(new FixedSource(first, second));

            ModLoadReport report = ModLoadOrchestrator.LoadFromRegisteredSources(new[] { importer });

            Assert.That(report.LoadedManifests, Is.Empty);
            Assert.That(importer.ImportedIds, Is.Empty);
            Assert.That(report.Issues, Has.Count.EqualTo(1));
            Assert.That(report.Issues[0].Severity, Is.EqualTo(ModIssueSeverity.Error));
            Assert.That(report.Issues[0].Message, Does.Contain("Circular mod dependency"));
        }

        [Test]
        public void LoadFromRegisteredSources_WithConflict_DoesNotImportConflictingMod()
        {
            string baseMod = CreateMod("base.mod", "Base Mod");
            string conflicting = CreateMod("conflicting.mod", "Conflicting Mod", conflicts: new[] { "base.mod" });
            var importer = new RecordingImporter();
            ModSourceRegistry.Register(new FixedSource(baseMod, conflicting));

            ModLoadReport report = ModLoadOrchestrator.LoadFromRegisteredSources(new[] { importer });

            Assert.That(report.LoadedManifests, Has.Count.EqualTo(1));
            Assert.That(report.LoadedManifests[0].Id, Is.EqualTo("base.mod"));
            Assert.That(importer.ImportedIds, Is.EquivalentTo(new[] { "base.mod" }));
            Assert.That(report.Issues, Has.Count.EqualTo(1));
            Assert.That(report.Issues[0].Severity, Is.EqualTo(ModIssueSeverity.Error));
            Assert.That(report.Issues[0].Message, Does.Contain("Conflicts with loaded mod"));
        }

        [Test]
        public void LoadFromRegisteredSources_WithForwardDeclaredConflict_DoesNotImportConflictingMod()
        {
            string baseMod = CreateMod("base.mod", "Base Mod", conflicts: new[] { "conflicting.mod" });
            string conflicting = CreateMod("conflicting.mod", "Conflicting Mod");
            var importer = new RecordingImporter();
            ModSourceRegistry.Register(new FixedSource(baseMod, conflicting));

            ModLoadReport report = ModLoadOrchestrator.LoadFromRegisteredSources(new[] { importer });

            Assert.That(report.LoadedManifests, Has.Count.EqualTo(1));
            Assert.That(report.LoadedManifests[0].Id, Is.EqualTo("base.mod"));
            Assert.That(importer.ImportedIds, Is.EquivalentTo(new[] { "base.mod" }));
            Assert.That(report.Issues, Has.Count.EqualTo(1));
            Assert.That(report.Issues[0].Severity, Is.EqualTo(ModIssueSeverity.Error));
            Assert.That(report.Issues[0].Message, Does.Contain("Conflicts with loaded mod"));
        }

        [Test]
        public void LoadFromRegisteredSources_WithDuplicateModId_DoesNotImportDuplicates()
        {
            string first = CreateMod("duplicate.mod", "Duplicate Mod A", directoryName: "A");
            string second = CreateMod("duplicate.mod", "Duplicate Mod B", directoryName: "B");
            var importer = new RecordingImporter();
            ModSourceRegistry.Register(new FixedSource(first, second));

            ModLoadReport report = ModLoadOrchestrator.LoadFromRegisteredSources(new[] { importer });

            Assert.That(report.LoadedManifests, Is.Empty);
            Assert.That(importer.ImportedIds, Is.Empty);
            Assert.That(report.Issues, Has.Count.EqualTo(1));
            Assert.That(report.Issues[0].Severity, Is.EqualTo(ModIssueSeverity.Error));
            Assert.That(report.Issues[0].Message, Does.Contain("Duplicate mod Id"));
        }

        [Test]
        public void LoadFromSources_WithCustomManifestFileName_ReadsPobModJson()
        {
            string modRoot = CreateMod("author.pob", "POB Mod", manifestFileName: "mod.json");
            var source = new FixedSource(modRoot);
            var importer = new RecordingImporter();

            var report = ModLoadOrchestrator.LoadFromSources(
                new[] { source },
                new IModContentImporter[] { importer },
                new ModLoadOptions { ManifestFileName = "mod.json" });

            Assert.That(
                report.Issues.Where(issue => issue.Severity == ModIssueSeverity.Error).Select(issue => issue.Message),
                Is.Empty);
            Assert.That(importer.ImportedIds, Is.EqualTo(new[] { "author.pob" }));
        }

        [UnityTest]
        public IEnumerator LoadFromSourcesAsync_WaitsForDelayedSourceBeforeImporting()
        {
            string modRoot = CreateMod("delayed.mod", "Delayed Mod");
            var source = new DelayedAsyncSource("delayed");
            var importer = new RecordingImporter();

            Task<ModLoadReport> loadTask = ModLoadOrchestrator.LoadFromSourcesAsync(
                new IModSource[] { source },
                new IModContentImporter[] { importer },
                new ModLoadOptions { SourceQueryTimeout = TimeSpan.FromSeconds(1) });

            Assert.That(loadTask.IsCompleted, Is.False);
            Assert.That(importer.ImportedIds, Is.Empty);

            source.Complete(ModSourceQueryResult.Success(source.SourceId, new[] { modRoot }));
            while (!loadTask.IsCompleted)
                yield return null;
            ModLoadReport report = loadTask.GetAwaiter().GetResult();

            Assert.That(report.LoadedManifests.Select(manifest => manifest.Id), Is.EqualTo(new[] { "delayed.mod" }));
            Assert.That(report.LoadedManifests.Single().SourceId, Is.EqualTo("delayed"));
            Assert.That(importer.ImportedIds, Is.EqualTo(new[] { "delayed.mod" }));
        }

        [UnityTest]
        public IEnumerator LoadFromSourcesAsync_WhenOneSourceTimesOut_ImportsCompletedSources()
        {
            string modRoot = CreateMod("ready.mod", "Ready Mod");
            var importer = new RecordingImporter();

            Task<ModLoadReport> loadTask = ModLoadOrchestrator.LoadFromSourcesAsync(
                new IModSource[]
                {
                    new NeverCompletingAsyncSource("slow"),
                    new FixedSource(modRoot)
                },
                new IModContentImporter[] { importer },
                new ModLoadOptions { SourceQueryTimeout = TimeSpan.FromMilliseconds(25) });
            while (!loadTask.IsCompleted)
                yield return null;
            ModLoadReport report = loadTask.GetAwaiter().GetResult();

            Assert.That(report.LoadedManifests.Select(manifest => manifest.Id), Is.EqualTo(new[] { "ready.mod" }));
            Assert.That(importer.ImportedIds, Is.EqualTo(new[] { "ready.mod" }));
            Assert.That(report.Issues.Any(issue => issue.ReasonCode == "source_timeout" && issue.Path == "slow"), Is.True);
        }

        [UnityTest]
        public IEnumerator LoadFromSourcesAsync_WhenSourcesCompleteOutOfOrder_UsesInputPriority()
        {
            string firstRoot = CreateMod("first.mod", "First Mod");
            string secondRoot = CreateMod("second.mod", "Second Mod");
            var first = new DelayedAsyncSource("first-source");
            var second = new DelayedAsyncSource("second-source");
            var importer = new RecordingImporter();

            Task<ModLoadReport> loadTask = ModLoadOrchestrator.LoadFromSourcesAsync(
                new IModSource[] { first, second },
                new IModContentImporter[] { importer },
                new ModLoadOptions { SourceQueryTimeout = TimeSpan.FromSeconds(1) });

            second.Complete(ModSourceQueryResult.Success(second.SourceId, new[] { secondRoot }));
            first.Complete(ModSourceQueryResult.Success(first.SourceId, new[] { firstRoot }));
            while (!loadTask.IsCompleted)
                yield return null;
            ModLoadReport report = loadTask.GetAwaiter().GetResult();

            Assert.That(report.LoadedManifests.Select(manifest => manifest.Id),
                Is.EqualTo(new[] { "first.mod", "second.mod" }));
            Assert.That(report.LoadedManifests.Select(manifest => manifest.SourceId),
                Is.EqualTo(new[] { "first-source", "second-source" }));
            Assert.That(importer.ImportedIds, Is.EqualTo(new[] { "first.mod", "second.mod" }));
        }

        [UnityTest]
        public IEnumerator LoadFromSourcesAsync_WhenLegacySourceCompletesTwice_UsesFirstResultOnce()
        {
            string firstRoot = CreateMod("first.mod", "First Mod");
            string ignoredRoot = CreateMod("ignored.mod", "Ignored Mod");
            var importer = new RecordingImporter();

            Task<ModLoadReport> loadTask = ModLoadOrchestrator.LoadFromSourcesAsync(
                new IModSource[] { new DoubleCompletingLegacySource(firstRoot, ignoredRoot) },
                new IModContentImporter[] { importer },
                new ModLoadOptions { SourceQueryTimeout = TimeSpan.FromSeconds(1) });
            while (!loadTask.IsCompleted)
                yield return null;
            ModLoadReport report = loadTask.GetAwaiter().GetResult();

            Assert.That(report.LoadedManifests.Select(manifest => manifest.Id), Is.EqualTo(new[] { "first.mod" }));
            Assert.That(importer.ImportedIds, Is.EqualTo(new[] { "first.mod" }));
        }

        [UnityTest]
        public IEnumerator LoadFromSourcesAsync_WhenDependencyImportFails_BlocksDependentImport()
        {
            string dependency = CreateMod("dependency.mod", "Dependency Mod");
            string dependent = CreateMod(
                "dependent.mod",
                "Dependent Mod",
                dependencies: new[] { "dependency.mod" });
            var recordingImporter = new RecordingImporter();

            Task<ModLoadReport> loadTask = ModLoadOrchestrator.LoadFromSourcesAsync(
                new IModSource[] { new FixedSource(dependency, dependent) },
                new IModContentImporter[]
                {
                    new SelectiveFailingImporter("dependency.mod"),
                    recordingImporter
                });
            while (!loadTask.IsCompleted)
                yield return null;
            ModLoadReport report = loadTask.GetAwaiter().GetResult();

            Assert.That(report.LoadedManifests, Is.Empty);
            Assert.That(recordingImporter.ImportedIds, Is.EqualTo(new[] { "dependency.mod" }));
            Assert.That(report.Issues.Any(issue =>
                issue.ReasonCode == "dependency_import_failed" && issue.ModId == "dependent.mod"), Is.True);
        }

        [UnityTest]
        public IEnumerator LoadFromSourcesAsync_WithDisabledMod_SkipsImportWithStableReason()
        {
            string disabled = CreateMod("disabled.mod", "Disabled Mod");
            var importer = new RecordingImporter();
            Task<ModLoadReport> loadTask = ModLoadOrchestrator.LoadFromSourcesAsync(
                new IModSource[] { new FixedSource(disabled) },
                new IModContentImporter[] { importer },
                new ModLoadOptions
                {
                    DisabledModIds = new HashSet<string>(StringComparer.Ordinal) { "disabled.mod" }
                });
            while (!loadTask.IsCompleted)
                yield return null;
            ModLoadReport report = loadTask.GetAwaiter().GetResult();

            Assert.That(importer.ImportedIds, Is.Empty);
            Assert.That(report.Issues.Any(issue =>
                issue.ReasonCode == "mod_disabled" && issue.ModId == "disabled.mod"), Is.True);
        }

        [UnityTest]
        public IEnumerator LoadFromSourcesAsync_WithDisabledDependency_SkipsDependentTransitively()
        {
            string dependency = CreateMod("dependency.mod", "Dependency Mod");
            string dependent = CreateMod("dependent.mod", "Dependent Mod", dependencies: new[] { "dependency.mod" });
            string transitive = CreateMod("transitive.mod", "Transitive Mod", dependencies: new[] { "dependent.mod" });
            var importer = new RecordingImporter();
            Task<ModLoadReport> loadTask = ModLoadOrchestrator.LoadFromSourcesAsync(
                new IModSource[] { new FixedSource(dependency, dependent, transitive) },
                new IModContentImporter[] { importer },
                new ModLoadOptions
                {
                    DisabledModIds = new HashSet<string>(StringComparer.Ordinal) { "dependency.mod" }
                });
            while (!loadTask.IsCompleted)
                yield return null;
            ModLoadReport report = loadTask.GetAwaiter().GetResult();

            Assert.That(importer.ImportedIds, Is.Empty);
            Assert.That(report.Issues.Any(issue =>
                issue.ReasonCode == "dependency_disabled" && issue.ModId == "dependent.mod"), Is.True);
            Assert.That(report.Issues.Any(issue =>
                issue.ReasonCode == "dependency_disabled" && issue.ModId == "transitive.mod"), Is.True);
        }

        private string CreateMod(
            string id,
            string name,
            string[] dependencies = null,
            string[] conflicts = null,
            string directoryName = null,
            string manifestFileName = "manifest.json")
        {
            string modRoot = Path.Combine(tempRoot, directoryName ?? id);
            Directory.CreateDirectory(modRoot);
            string dependenciesJson = dependencies == null ? "[]" : $"[ {string.Join(", ", Array.ConvertAll(dependencies, item => $"\"{item}\""))} ]";
            string conflictsJson = conflicts == null ? "[]" : $"[ {string.Join(", ", Array.ConvertAll(conflicts, item => $"\"{item}\""))} ]";

            File.WriteAllText(Path.Combine(modRoot, manifestFileName), $@"{{
  ""Id"": ""{id}"",
  ""Name"": ""{name}"",
  ""Version"": ""1.0.0"",
  ""Dependencies"": {dependenciesJson},
  ""Conflicts"": {conflictsJson}
}}");
            return modRoot;
        }

        private sealed class FixedSource : IModSource
        {
            private readonly string[] folders;

            public FixedSource(params string[] folders)
            {
                this.folders = folders ?? Array.Empty<string>();
            }

            public string SourceId => "fixed";
            public bool IsAvailable => true;

            public void QueryInstalledModFolders(Action<ModSourceQueryResult> onCompleted)
            {
                onCompleted?.Invoke(ModSourceQueryResult.Success(SourceId, folders));
            }
        }

        private sealed class ThrowingSource : IModSource
        {
            public string SourceId => "throwing";
            public bool IsAvailable => true;

            public void QueryInstalledModFolders(Action<ModSourceQueryResult> onCompleted)
            {
                throw new InvalidOperationException("Source query failed.");
            }
        }

        private sealed class DelayedAsyncSource : IAsyncModSource
        {
            private readonly TaskCompletionSource<ModSourceQueryResult> completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public DelayedAsyncSource(string sourceId)
            {
                SourceId = sourceId;
            }

            public string SourceId { get; }
            public bool IsAvailable => true;

            public Task<ModSourceQueryResult> QueryInstalledModFoldersAsync(CancellationToken cancellationToken)
            {
                cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
                return completion.Task;
            }

            public void QueryInstalledModFolders(Action<ModSourceQueryResult> onCompleted)
            {
                completion.Task.GetAwaiter().OnCompleted(() => onCompleted?.Invoke(completion.Task.Result));
            }

            public void Complete(ModSourceQueryResult result)
            {
                completion.TrySetResult(result);
            }
        }

        private sealed class NeverCompletingAsyncSource : IAsyncModSource
        {
            public NeverCompletingAsyncSource(string sourceId)
            {
                SourceId = sourceId;
            }

            public string SourceId { get; }
            public bool IsAvailable => true;

            public Task<ModSourceQueryResult> QueryInstalledModFoldersAsync(CancellationToken cancellationToken)
            {
                var completion = new TaskCompletionSource<ModSourceQueryResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
                return completion.Task;
            }

            public void QueryInstalledModFolders(Action<ModSourceQueryResult> onCompleted)
            {
            }
        }

        private sealed class DoubleCompletingLegacySource : IModSource
        {
            private readonly string first;
            private readonly string second;

            public DoubleCompletingLegacySource(string first, string second)
            {
                this.first = first;
                this.second = second;
            }

            public string SourceId => "legacy-double";
            public bool IsAvailable => true;

            public void QueryInstalledModFolders(Action<ModSourceQueryResult> onCompleted)
            {
                onCompleted?.Invoke(ModSourceQueryResult.Success(SourceId, new[] { first }));
                onCompleted?.Invoke(ModSourceQueryResult.Success(SourceId, new[] { second }));
            }
        }

        private sealed class RecordingImporter : IModContentImporter
        {
            public List<string> ImportedIds { get; } = new();

            public ModContentImportResult Import(ModImportContext context)
            {
                ImportedIds.Add(context.Manifest.Id);
                return ModContentImportResult.Success();
            }
        }

        private sealed class FailingImporter : IModContentImporter
        {
            public ModContentImportResult Import(ModImportContext context)
            {
                return ModContentImportResult.Failed(new ModLoadIssue(
                    ModIssueSeverity.Error,
                    context.Manifest.Id,
                    string.Empty,
                    "Importer failed."));
            }
        }

        private sealed class SelectiveFailingImporter : IModContentImporter
        {
            private readonly string failedId;

            public SelectiveFailingImporter(string failedId)
            {
                this.failedId = failedId;
            }

            public ModContentImportResult Import(ModImportContext context)
            {
                return context.Manifest.Id == failedId
                    ? ModContentImportResult.Failed(new ModLoadIssue(
                        ModIssueSeverity.Error,
                        context.Manifest.Id,
                        string.Empty,
                        "Importer failed."))
                    : ModContentImportResult.Success();
            }
        }
    }
}
