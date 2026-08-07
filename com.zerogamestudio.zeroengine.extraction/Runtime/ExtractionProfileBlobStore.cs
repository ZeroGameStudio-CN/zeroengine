namespace POB.Extraction
{
    public interface IExtractionProfileBlobStore
    {
        ExtractionProfileBlobLoadResult Load();
        bool TryLoad(out string json);
        ExtractionProfileBlobCommitResult Save(string json);
        ExtractionProfileBlobCommitResult TryCommit(string expectedRevision, string json);
    }

    public class ExtractionInMemoryProfileBlobStore : IExtractionProfileBlobStore
    {
        private string json;
        private bool hasProfile;

        public bool HasProfile => hasProfile;

        public ExtractionProfileBlobLoadResult Load()
        {
            return !hasProfile
                ? ExtractionProfileBlobLoadResult.Missing()
                : ExtractionProfileBlobLoadResult.Loaded(json);
        }

        public bool TryLoad(out string json)
        {
            var result = Load();
            json = result.Json;
            return result.Success && result.Found;
        }

        public ExtractionProfileBlobCommitResult Save(string json)
        {
            return TryCommit(Load().Revision, json);
        }

        public ExtractionProfileBlobCommitResult TryCommit(string expectedRevision, string json)
        {
            string currentRevision = Load().Revision;
            if (!string.Equals(expectedRevision, currentRevision, System.StringComparison.Ordinal))
            {
                return ExtractionProfileBlobCommitResult.Failed(
                    ExtractionProfileCommitFailure.RevisionConflict,
                    "The profile blob changed after it was loaded.");
            }

            if (json == null)
            {
                return ExtractionProfileBlobCommitResult.Failed(
                    ExtractionProfileCommitFailure.PrepareFailed,
                    "Profile JSON is required.");
            }

            this.json = json;
            hasProfile = true;
            return ExtractionProfileBlobCommitResult.Succeeded(ExtractionProfileRevision.FromJson(json));
        }
    }
}
