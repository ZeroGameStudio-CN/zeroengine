namespace POB.Extraction
{
    public interface IExtractionProfileStore
    {
        ExtractionProfileLoadResult Load();
        ExtractionProfileCommitResult Commit(ExtractionProfileDraft draft);
        ExtractionProfileSaveData LoadProfile();
        ExtractionProfileCommitResult SaveProfile(ExtractionProfileSaveData profile);
    }
}
