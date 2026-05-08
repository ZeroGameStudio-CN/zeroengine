namespace ZeroEngine.Quest
{
    /// <summary>
    /// 任务系统事件常量。
    /// </summary>
    public static class QuestEvents
    {
        // 条件事件
        public const string EntityKilled = "Quest.EntityKilled";
        public const string ItemObtained = "Quest.ItemObtained";
        public const string Interacted = "Quest.Interacted";
        public const string LocationReached = "Quest.LocationReached";
        public const string SurviveCompleted = "Quest.SurviveCompleted";

        // 任务状态事件
        public const string QuestAccepted = "Quest.Accepted";
        public const string QuestCompleted = "Quest.Completed";
        public const string QuestSubmitted = "Quest.Submitted";
        public const string QuestFailed = "Quest.Failed";
        public const string QuestAbandoned = "Quest.Abandoned";

        // 进度事件
        public const string ConditionProgress = "Quest.ConditionProgress";
        public const string ConditionCompleted = "Quest.ConditionCompleted";
    }
}
