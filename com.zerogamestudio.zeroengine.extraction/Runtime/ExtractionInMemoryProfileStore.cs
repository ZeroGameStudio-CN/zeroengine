namespace POB.Extraction
{
    public class ExtractionInMemoryProfileStore : IExtractionProfileStore
    {
        private ExtractionProfileSaveData authoritativeProfile;
        private long revision;
        private ExtractionProfileLoadResult lastLegacyLoad;

        public ExtractionInMemoryProfileStore()
            : this(ExtractionProfileSaveData.CreateEmpty())
        {
        }

        public ExtractionInMemoryProfileStore(ExtractionProfileSaveData initialProfile)
        {
            authoritativeProfile = ExtractionProfileCloneUtility.Clone(initialProfile);
        }

        /// <summary>
        /// Legacy mutable view retained for in-memory fixture seeding and inspection.
        /// Production mutations must use Load/CreateDraft/Commit so failures remain observable.
        /// </summary>
        public ExtractionProfileSaveData Profile => authoritativeProfile;
        public ExtractionProfileInMemoryCommitFault NextCommitFault { get; set; }

        public ExtractionProfileLoadResult Load()
        {
            if (authoritativeProfile.SchemaVersion > ExtractionProfileSaveData.CurrentSchemaVersion)
            {
                return ExtractionProfileLoadResult.Failed(
                    authoritativeProfile,
                    CurrentRevision,
                    ExtractionProfileLoadFailure.UnsupportedSchema,
                    "A newer profile schema is read-only in this client.");
            }

            return ExtractionProfileLoadResult.Loaded(authoritativeProfile, CurrentRevision);
        }

        public ExtractionProfileSaveData LoadProfile()
        {
            lastLegacyLoad = Load();
            return lastLegacyLoad.Profile;
        }

        public ExtractionProfileCommitResult SaveProfile(ExtractionProfileSaveData profile)
        {
            lastLegacyLoad ??= Load();
            if (!lastLegacyLoad.TryCreateDraft(profile, out var draft))
            {
                return ExtractionProfileCommitResult.Failed(
                    ExtractionProfileCommitFailure.ReadOnly,
                    "The loaded profile is read-only.",
                    Load());
            }

            var result = Commit(draft);
            if (result.Success)
                lastLegacyLoad = result.Snapshot;
            return result;
        }

        public ExtractionProfileCommitResult Commit(ExtractionProfileDraft draft)
        {
            if (draft == null)
            {
                return ExtractionProfileCommitResult.Failed(
                    ExtractionProfileCommitFailure.InvalidDraft,
                    "Profile draft is required.",
                    Load());
            }

            if (!string.Equals(draft.BaseRevision, CurrentRevision, System.StringComparison.Ordinal))
            {
                return ExtractionProfileCommitResult.Failed(
                    ExtractionProfileCommitFailure.RevisionConflict,
                    "The profile changed after the draft was loaded.",
                    Load());
            }

            if (draft.Profile.SchemaVersion > ExtractionProfileSaveData.CurrentSchemaVersion)
            {
                return ExtractionProfileCommitResult.Failed(
                    ExtractionProfileCommitFailure.ReadOnly,
                    "A newer profile schema cannot be committed by this client.",
                    Load());
            }

            if (ConsumeFault(ExtractionProfileInMemoryCommitFault.Prepare))
            {
                return ExtractionProfileCommitResult.Failed(
                    ExtractionProfileCommitFailure.PrepareFailed,
                    "Injected prepare failure.",
                    Load());
            }

            ExtractionProfileSaveData prepared = ExtractionProfileCloneUtility.Clone(draft.Profile);
            if (ConsumeFault(ExtractionProfileInMemoryCommitFault.Commit))
            {
                return ExtractionProfileCommitResult.Failed(
                    ExtractionProfileCommitFailure.WriteFailed,
                    "Injected commit failure.",
                    Load());
            }

            ExtractionProfileSaveData readback = ExtractionProfileCloneUtility.Clone(prepared);
            if (ConsumeFault(ExtractionProfileInMemoryCommitFault.Readback))
            {
                return ExtractionProfileCommitResult.Failed(
                    ExtractionProfileCommitFailure.ReadbackFailed,
                    "Injected readback failure.",
                    Load());
            }

            authoritativeProfile = readback;
            revision++;
            return ExtractionProfileCommitResult.Succeeded(Load());
        }

        private string CurrentRevision => $"memory:{revision}";

        private bool ConsumeFault(ExtractionProfileInMemoryCommitFault fault)
        {
            if (NextCommitFault != fault)
                return false;

            NextCommitFault = ExtractionProfileInMemoryCommitFault.None;
            return true;
        }
    }
}
