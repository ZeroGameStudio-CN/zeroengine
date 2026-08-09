namespace POB.Extraction
{
    public interface IExtractionMetaProgressProvider
    {
        int GetSharedUnlockLevel(string unlockId);
        bool HasSharedTalent(string talentId);
    }

    public sealed class ExtractionNoOpMetaProgressProvider : IExtractionMetaProgressProvider
    {
        public int GetSharedUnlockLevel(string unlockId)
        {
            return 0;
        }

        public bool HasSharedTalent(string talentId)
        {
            return false;
        }
    }
}
