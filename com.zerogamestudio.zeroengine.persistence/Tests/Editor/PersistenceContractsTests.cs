using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZeroEngine.Persistence.Tests
{
    public sealed class PersistenceContractsTests
    {
        private string _temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            _temporaryRoot = Path.Combine(Path.GetTempPath(), "ZeroEngine.Persistence.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryRoot))
            {
                Directory.Delete(_temporaryRoot, true);
            }
        }

        [Test]
        public void Registry_CapturesRegistrationOrder_AndRejectsDuplicateKeys()
        {
            var events = new List<string>();
            var registry = new SaveParticipantRegistry();
            registry.Register(new DelegateSaveParticipantAdapter("b", () => "B", state => { }));
            registry.Register(new DelegateSaveParticipantAdapter("a", () => "A", state => { }));

            var capture = registry.Capture();

            Assert.That(capture.Success, Is.True);
            Assert.That(capture.States.Keys.ToArray(), Is.EqualTo(new[] { "b", "a" }));
            Assert.Throws<ArgumentException>(() => registry.Register(
                new DelegateSaveParticipantAdapter("b", () => "duplicate", state => { })));
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void Registry_PreparesAllParticipantsBeforeOrderedRestore()
        {
            var events = new List<string>();
            var registry = new SaveParticipantRegistry();
            registry.Register(new DelegateSaveParticipantAdapter(
                "first",
                () => 1,
                state => events.Add("restore:first"),
                async (state, cancellationToken) =>
                {
                    await Task.Delay(10, cancellationToken);
                    events.Add("prepare:first");
                }));
            registry.Register(new DelegateSaveParticipantAdapter(
                "second",
                () => 2,
                state => events.Add("restore:second"),
                async (state, cancellationToken) =>
                {
                    await Task.Delay(1, cancellationToken);
                    events.Add("prepare:second");
                }));

            var restore = registry.RestoreAsync(
                new Dictionary<string, object> { ["first"] = 10, ["second"] = 20 }).GetAwaiter().GetResult();

            Assert.That(restore.Success, Is.True);
            Assert.That(events, Is.EqualTo(new[]
            {
                "prepare:first",
                "prepare:second",
                "restore:first",
                "restore:second"
            }));
        }

        [Test]
        public void Pipeline_ReturnsStructuredSuccessBlockedFailureAndCancelledResults()
        {
            var backend = new InMemoryBackend();
            var registry = new SaveParticipantRegistry();
            var captured = 0;
            registry.Register(new DelegateSaveParticipantAdapter(
                "counter",
                () => ++captured,
                state => { }));

            var pipeline = new SaveSlotPipeline<string>(backend, registry, new SaveSlotPipelineOptions<string>
            {
                MetadataProvider = new DelegateSaveSlotMetadataProvider<string>((slot, token) =>
                    Task.FromResult("meta:" + slot))
            });

            var saved = pipeline.SaveAsync("slot-0").GetAwaiter().GetResult();
            Assert.That(saved.Status, Is.EqualTo(SaveSlotOperationStatus.Saved));
            Assert.That(backend.LastPayload.Metadata, Is.EqualTo("meta:slot-0"));
            Assert.That(captured, Is.EqualTo(1));

            backend.NextLoad = SaveSlotReadResult<string>.Failed("read-failed");
            var failed = pipeline.LoadAsync("slot-0").GetAwaiter().GetResult();
            Assert.That(failed.Status, Is.EqualTo(SaveSlotOperationStatus.Failed));

            var blockedPipeline = new SaveSlotPipeline<string>(backend, registry, new SaveSlotPipelineOptions<string>
            {
                Gate = new DelegateSaveSlotOperationGate((slot, operation) => SaveSlotGateDecision.Block("battle"))
            });
            var blocked = blockedPipeline.SaveAsync("slot-0").GetAwaiter().GetResult();
            Assert.That(blocked.Status, Is.EqualTo(SaveSlotOperationStatus.Blocked));
            Assert.That(captured, Is.EqualTo(1), "blocked save must not capture participants");

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var cancelled = pipeline.SaveAsync("slot-0", cancellation.Token).GetAwaiter().GetResult();
                Assert.That(cancelled.Status, Is.EqualTo(SaveSlotOperationStatus.Cancelled));
            }
        }

        [Test]
        public void AtomicPromotion_RollsBackAllFiles_WhenSecondPromotionFails()
        {
            var first = CreateAtomicFiles("data");
            var second = CreateAtomicFiles("meta");
            File.WriteAllText(first.FinalPath, "old-data");
            File.WriteAllText(first.BackupPath, "old-data-backup");
            File.WriteAllText(second.FinalPath, "old-meta");
            File.WriteAllText(second.BackupPath, "old-meta-backup");
            File.WriteAllText(first.TempPath, "new-data");
            File.WriteAllText(second.TempPath, "new-meta");

            var result = AtomicFilePromotion.Promote(
                new[] { first, second },
                new DelegateAtomicPromotionHook((phase, file) =>
                {
                    if (phase == AtomicPromotionPhase.BeforeTempPromotion && file == second)
                    {
                        throw new IOException("injected promotion failure");
                    }
                }));

            Assert.That(result.Success, Is.False);
            Assert.That(result.RolledBack, Is.True);
            Assert.That(File.ReadAllText(first.FinalPath), Is.EqualTo("old-data"));
            Assert.That(File.ReadAllText(first.BackupPath), Is.EqualTo("old-data-backup"));
            Assert.That(File.ReadAllText(second.FinalPath), Is.EqualTo("old-meta"));
            Assert.That(File.ReadAllText(second.BackupPath), Is.EqualTo("old-meta-backup"));
            Assert.That(File.Exists(first.TempPath), Is.False);
            Assert.That(File.Exists(second.TempPath), Is.False);
        }

        [Test]
        public void AtomicPromotion_CleansBackupsAfterSuccess()
        {
            var first = CreateAtomicFiles("data");
            var second = CreateAtomicFiles("meta");
            File.WriteAllText(first.FinalPath, "old-data");
            File.WriteAllText(second.FinalPath, "old-meta");
            File.WriteAllText(first.TempPath, "new-data");
            File.WriteAllText(second.TempPath, "new-meta");

            var result = AtomicFilePromotion.Promote(new[] { first, second });

            Assert.That(result.Success, Is.True);
            Assert.That(File.ReadAllText(first.FinalPath), Is.EqualTo("new-data"));
            Assert.That(File.ReadAllText(second.FinalPath), Is.EqualTo("new-meta"));
            Assert.That(File.Exists(first.BackupPath), Is.False);
            Assert.That(File.Exists(second.BackupPath), Is.False);
        }

        [Test]
        public void AtomicPromotion_CleanupHookFailure_DoesNotRollbackCommittedFiles()
        {
            var first = CreateAtomicFiles("data-cleanup");
            var second = CreateAtomicFiles("meta-cleanup");
            File.WriteAllText(first.FinalPath, "old-data");
            File.WriteAllText(second.FinalPath, "old-meta");
            File.WriteAllText(first.TempPath, "new-data");
            File.WriteAllText(second.TempPath, "new-meta");

            var result = AtomicFilePromotion.Promote(
                new[] { first, second },
                new DelegateAtomicPromotionHook((phase, _) =>
                {
                    if (phase == AtomicPromotionPhase.BeforeCleanup)
                    {
                        throw new IOException("injected cleanup failure");
                    }
                }));

            Assert.That(result.Success, Is.True);
            Assert.That(File.ReadAllText(first.FinalPath), Is.EqualTo("new-data"));
            Assert.That(File.ReadAllText(second.FinalPath), Is.EqualTo("new-meta"));
        }

        [Test]
        public void ScreenshotPolicy_RejectsTraversalAbsolutePathAndOversizedPng()
        {
            var root = Path.Combine(_temporaryRoot, "screenshots");
            Directory.CreateDirectory(root);
            var policy = new ScreenshotFilePolicy(root, maxWidth: 100, maxHeight: 100, maxPixels: 5000, maxFileLength: 64);
            var valid = CreatePngHeader(10, 20);
            var validScreenshot = new SaveScreenshot(policy.GetFileName("slot-0"), valid, 10, 20);

            Assert.That(policy.Validate("slot-0", validScreenshot).IsValid, Is.True);
            Assert.That(ScreenshotFilePolicy.IsValidSlotId("../outside"), Is.False);
            Assert.That(policy.TryValidatePath("slot-0", Path.Combine(root, "..", "outside.png"), out _), Is.False);
            Assert.That(policy.TryValidatePath("../outside", Path.Combine(root, "slot-0.png"), out _), Is.False);
            Assert.That(policy.Validate("slot-0", new SaveScreenshot(policy.GetFileName("slot-0"), CreatePngHeader(101, 1))).IsValid, Is.False);
            Assert.That(policy.Validate("slot-0", new SaveScreenshot(policy.GetFileName("slot-0"), new byte[65])).IsValid, Is.False);
        }

        private AtomicPromotionFile CreateAtomicFiles(string name)
        {
            return new AtomicPromotionFile(
                Path.Combine(_temporaryRoot, name + ".final"),
                Path.Combine(_temporaryRoot, name + ".temp"),
                Path.Combine(_temporaryRoot, name + ".bak"));
        }

        private static byte[] CreatePngHeader(int width, int height)
        {
            var bytes = new byte[24];
            bytes[0] = 0x89;
            bytes[1] = 0x50;
            bytes[2] = 0x4E;
            bytes[3] = 0x47;
            bytes[4] = 0x0D;
            bytes[5] = 0x0A;
            bytes[6] = 0x1A;
            bytes[7] = 0x0A;
            bytes[16] = (byte)(width >> 24);
            bytes[17] = (byte)(width >> 16);
            bytes[18] = (byte)(width >> 8);
            bytes[19] = (byte)width;
            bytes[20] = (byte)(height >> 24);
            bytes[21] = (byte)(height >> 16);
            bytes[22] = (byte)(height >> 8);
            bytes[23] = (byte)height;
            return bytes;
        }

        private sealed class InMemoryBackend : ISaveSlotBackend<string>
        {
            private readonly Dictionary<string, SaveSlotPayload<string>> _slots =
                new Dictionary<string, SaveSlotPayload<string>>(StringComparer.Ordinal);

            public SaveSlotPayload<string> LastPayload { get; private set; }
            public SaveSlotReadResult<string> NextLoad { get; set; }

            public Task<SaveSlotBackendWriteResult> SaveAsync(
                string slotId,
                SaveSlotPayload<string> payload,
                CancellationToken cancellationToken)
            {
                LastPayload = payload;
                _slots[slotId] = payload;
                return Task.FromResult(SaveSlotBackendWriteResult.Saved());
            }

            public Task<SaveSlotReadResult<string>> LoadAsync(string slotId, CancellationToken cancellationToken)
            {
                if (NextLoad.Status != SaveSlotOperationStatus.Saved)
                {
                    var next = NextLoad;
                    NextLoad = default(SaveSlotReadResult<string>);
                    return Task.FromResult(next);
                }

                return Task.FromResult(_slots.TryGetValue(slotId, out var payload)
                    ? SaveSlotReadResult<string>.Loaded(payload)
                    : SaveSlotReadResult<string>.Missing());
            }

            public Task<SaveSlotBackendDeleteResult> DeleteAsync(string slotId, CancellationToken cancellationToken)
            {
                _slots.Remove(slotId);
                return Task.FromResult(SaveSlotBackendDeleteResult.Deleted());
            }
        }
    }
}
