using System;

namespace ZeroEngine.Quest
{
    public enum QuestState
    {
        Inactive,   // Not accepted
        Active,     // Accepted, in progress
        Successful, // Goals met, ready to submit
        TheEnd      // Submitted & Completed
    }

    public enum QuestType
    {
        Dialogue,
        KillMonster,
        Collect,
        Custom
    }

    public enum QuestLifecycle
    {
        Persistent, // Survives across runs
        PerRun      // Cleared at end of each run
    }
}
