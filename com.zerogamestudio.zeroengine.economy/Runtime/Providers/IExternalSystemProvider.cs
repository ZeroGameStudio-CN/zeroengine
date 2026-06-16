namespace ZeroEngine.Economy
{
    public interface IExternalSystemProvider
    {
        int GetPlayerLevel();
        int GetPlayerReputation(string reputationId = null);
        bool IsQuestCompleted(string questId);
        int GetRelationshipLevel(string npcId);
    }
}
