namespace ZeroEngine.Quest
{
    public enum QuestState
    {
        Inactive,   // Not accepted
        Active,     // Accepted, in progress
        Successful, // Goals met, ready to submit
        TheEnd      // Submitted & Completed
    }

    public enum QuestLifecycle
    {
        Persistent, // Survives across runs
        PerRun      // Cleared at end of each run
    }

    /// <summary>
    /// Legacy objective category retained for serialized quest assets and consumer compatibility.
    /// </summary>
    public enum QuestType
    {
        Custom,
        Dialogue,
        Collect,
        KillMonster
    }
}
