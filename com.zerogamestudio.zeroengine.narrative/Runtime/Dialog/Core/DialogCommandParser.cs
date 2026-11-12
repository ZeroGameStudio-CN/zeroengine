using System;
using ZeroEngine.Quest;

namespace ZeroEngine.Dialog
{
    public static class DialogCommandParser
    {
        public static bool TryParse(string callbackId, string parameter, out DialogCommand command)
        {
            command = default;
            if (string.IsNullOrWhiteSpace(callbackId))
            {
                return false;
            }

            var normalized = callbackId.Trim();
            var rawParameter = parameter ?? string.Empty;

            switch (normalized)
            {
                case "quest.accept":
                    command = new DialogCommand(DialogCommandKind.QuestAccept, normalized, rawParameter, rawParameter: rawParameter);
                    return true;
                case "quest.submit":
                    command = new DialogCommand(DialogCommandKind.QuestSubmit, normalized, rawParameter, rawParameter: rawParameter);
                    return true;
                case "quest.progress":
                    ParseTargetAmount(rawParameter, out var progressTarget, out var progressAmount);
                    command = new DialogCommand(DialogCommandKind.QuestProgress, normalized, progressTarget, progressAmount, rawParameter: rawParameter);
                    return true;
                case "quest.collect":
                    command = new DialogCommand(DialogCommandKind.QuestEvent, normalized, rawParameter, eventType: QuestEvents.ItemCollected, rawParameter: rawParameter);
                    return true;
                case "quest.kill":
                    command = new DialogCommand(DialogCommandKind.QuestEvent, normalized, rawParameter, eventType: QuestEvents.EnemyKilled, rawParameter: rawParameter);
                    return true;
                case "dialog.flag":
                case "fact.set":
                    ParseAssignment(rawParameter, out var factId, out var value);
                    command = new DialogCommand(DialogCommandKind.FactSet, normalized, factId: factId, value: value, rawParameter: rawParameter);
                    return true;
                case "fact.add":
                    ParseTargetAmount(rawParameter, out var addFactId, out var addAmount);
                    command = new DialogCommand(DialogCommandKind.FactAdd, normalized, amount: addAmount, factId: addFactId, rawParameter: rawParameter);
                    return true;
                case "reward.grant":
                    command = new DialogCommand(DialogCommandKind.RewardGrant, normalized, rewardId: rawParameter, rawParameter: rawParameter);
                    return true;
                default:
                    return false;
            }
        }

        private static void ParseAssignment(string parameter, out string key, out string value)
        {
            if (string.IsNullOrEmpty(parameter))
            {
                key = string.Empty;
                value = "true";
                return;
            }

            var splitIndex = parameter.IndexOf('=');
            if (splitIndex < 0)
            {
                key = parameter;
                value = "true";
                return;
            }

            key = parameter.Substring(0, splitIndex);
            value = parameter.Substring(splitIndex + 1);
        }

        private static void ParseTargetAmount(string parameter, out string targetId, out int amount)
        {
            amount = 1;
            if (string.IsNullOrEmpty(parameter))
            {
                targetId = string.Empty;
                return;
            }

            var splitIndex = parameter.LastIndexOf(':');
            if (splitIndex < 0)
            {
                targetId = parameter;
                return;
            }

            targetId = parameter.Substring(0, splitIndex);
            if (!int.TryParse(parameter.Substring(splitIndex + 1), out amount) || amount <= 0)
            {
                amount = 1;
            }
        }
    }
}
