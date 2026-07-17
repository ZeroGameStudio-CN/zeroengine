using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionProfileStoreContractTests
    {
        [Test]
        public void Load_DraftMutationWithoutCommit_DoesNotChangeAuthority()
        {
            var store = CreateStore("old");
            var loaded = store.Load();

            Assert.IsTrue(loaded.TryCreateDraft(out var draft));
            draft.Profile.activeRaidId = "draft";

            Assert.AreEqual("old", store.Load().Profile.activeRaidId);
            Assert.AreEqual(loaded.Revision, store.Load().Revision);
        }

        [Test]
        public void Profile_LegacyMutableView_CanSeedInMemoryFixture()
        {
            var store = CreateStore("old");

            store.Profile.activeRaidId = "seeded";

            Assert.AreEqual("seeded", store.Load().Profile.activeRaidId);
        }

        [Test]
        public void Commit_ValidDraft_ReplacesAuthorityAndAdvancesRevision()
        {
            var store = CreateStore("old");
            var loaded = store.Load();
            Assert.IsTrue(loaded.TryCreateDraft(out var draft));
            draft.Profile.activeRaidId = "committed";

            ExtractionProfileCommitResult result = store.Commit(draft);

            Assert.IsTrue(result.Success);
            Assert.AreNotEqual(loaded.Revision, result.Revision);
            Assert.AreEqual("committed", store.Load().Profile.activeRaidId);
        }

        [TestCase(ExtractionProfileInMemoryCommitFault.Prepare, ExtractionProfileCommitFailure.PrepareFailed)]
        [TestCase(ExtractionProfileInMemoryCommitFault.Commit, ExtractionProfileCommitFailure.WriteFailed)]
        [TestCase(ExtractionProfileInMemoryCommitFault.Readback, ExtractionProfileCommitFailure.ReadbackFailed)]
        public void Commit_InjectedFault_PreservesAuthorityAndRevision(
            ExtractionProfileInMemoryCommitFault fault,
            ExtractionProfileCommitFailure expectedFailure)
        {
            var store = CreateStore("old");
            var loaded = store.Load();
            Assert.IsTrue(loaded.TryCreateDraft(out var draft));
            draft.Profile.activeRaidId = "new";
            store.NextCommitFault = fault;

            ExtractionProfileCommitResult result = store.Commit(draft);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(expectedFailure, result.Failure);
            Assert.AreEqual("old", store.Load().Profile.activeRaidId);
            Assert.AreEqual(loaded.Revision, store.Load().Revision);
        }

        [Test]
        public void Commit_StaleDraft_ReturnsConflictAndKeepsFirstCommit()
        {
            var store = CreateStore("old");
            var baseline = store.Load();
            Assert.IsTrue(baseline.TryCreateDraft(out var first));
            Assert.IsTrue(baseline.TryCreateDraft(out var stale));
            first.Profile.activeRaidId = "first";
            stale.Profile.activeRaidId = "stale";
            Assert.IsTrue(store.Commit(first).Success);

            ExtractionProfileCommitResult result = store.Commit(stale);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ExtractionProfileCommitFailure.RevisionConflict, result.Failure);
            Assert.AreEqual("first", store.Load().Profile.activeRaidId);
        }

        [Test]
        public void SaveProfile_ReturnsObservableCommitResult()
        {
            var store = CreateStore("old");
            ExtractionProfileSaveData profile = store.LoadProfile();
            profile.activeRaidId = "saved";

            ExtractionProfileCommitResult result = store.SaveProfile(profile);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("saved", store.LoadProfile().activeRaidId);
        }

        [Test]
        public void Load_NewerSchema_IsReadOnlyAndCannotCreateDraft()
        {
            var store = new ExtractionInMemoryProfileStore(new ExtractionProfileSaveData
            {
                SchemaVersion = ExtractionProfileSaveData.CurrentSchemaVersion + 1,
                activeRaidId = "future"
            });

            ExtractionProfileLoadResult loaded = store.Load();

            Assert.IsTrue(loaded.IsReadOnly);
            Assert.IsFalse(loaded.CanCommit);
            Assert.AreEqual(ExtractionProfileLoadFailure.UnsupportedSchema, loaded.Failure);
            Assert.IsFalse(loaded.TryCreateDraft(out _));
            Assert.AreEqual("future", loaded.Profile.activeRaidId);
        }

        [Test]
        public void InMemoryBlob_TryCommitWithStaleRevision_ReturnsConflict()
        {
            var blob = new ExtractionInMemoryProfileBlobStore();
            ExtractionProfileBlobLoadResult missing = blob.Load();
            Assert.IsTrue(blob.TryCommit(missing.Revision, "{\"v\":1}").Success);

            ExtractionProfileBlobCommitResult result = blob.TryCommit(missing.Revision, "{\"v\":2}");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ExtractionProfileCommitFailure.RevisionConflict, result.Failure);
            Assert.IsTrue(blob.TryLoad(out string json));
            Assert.AreEqual("{\"v\":1}", json);
        }

        private static ExtractionInMemoryProfileStore CreateStore(string activeRaidId)
        {
            return new ExtractionInMemoryProfileStore(new ExtractionProfileSaveData
            {
                activeRaidId = activeRaidId
            });
        }
    }
}
