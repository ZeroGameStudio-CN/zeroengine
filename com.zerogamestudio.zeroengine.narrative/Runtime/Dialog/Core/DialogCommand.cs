using System;

namespace ZeroEngine.Dialog
{
    public enum DialogCommandKind
    {
        Unknown,
        QuestAccept,
        QuestSubmit,
        QuestProgress,
        QuestEvent,
        FactSet,
        FactAdd,
        RewardGrant
    }

    public readonly struct DialogCommand
    {
        public DialogCommand(
            DialogCommandKind kind,
            string commandId,
            string targetId = null,
            int amount = 1,
            string factId = null,
            string value = null,
            string eventType = null,
            string rewardId = null,
            string rawParameter = null)
        {
            Kind = kind;
            CommandId = commandId ?? string.Empty;
            TargetId = targetId ?? string.Empty;
            Amount = amount <= 0 ? 1 : amount;
            FactId = factId ?? string.Empty;
            Value = value ?? string.Empty;
            EventType = eventType ?? string.Empty;
            RewardId = rewardId ?? string.Empty;
            RawParameter = rawParameter ?? string.Empty;
        }

        public DialogCommandKind Kind { get; }
        public string CommandId { get; }
        public string TargetId { get; }
        public int Amount { get; }
        public string FactId { get; }
        public string Value { get; }
        public string EventType { get; }
        public string RewardId { get; }
        public string RawParameter { get; }
    }
}
