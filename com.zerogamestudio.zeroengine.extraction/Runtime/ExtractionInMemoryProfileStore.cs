namespace POB.Extraction
{
    public class ExtractionInMemoryProfileStore : IExtractionProfileStore
    {
        public ExtractionProfileSaveData Profile { get; private set; } = new();

        public ExtractionProfileSaveData LoadProfile()
        {
            Profile ??= ExtractionProfileSaveData.CreateEmpty();
            Profile.EnsureInitialized();
            return Profile;
        }

        public void SaveProfile(ExtractionProfileSaveData profile)
        {
            Profile = profile ?? ExtractionProfileSaveData.CreateEmpty();
            Profile.EnsureInitialized();
        }
    }
}
