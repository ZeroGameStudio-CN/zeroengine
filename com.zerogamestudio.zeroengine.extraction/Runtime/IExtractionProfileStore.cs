namespace POB.Extraction
{
    public interface IExtractionProfileStore
    {
        ExtractionProfileSaveData LoadProfile();
        void SaveProfile(ExtractionProfileSaveData profile);
    }
}
