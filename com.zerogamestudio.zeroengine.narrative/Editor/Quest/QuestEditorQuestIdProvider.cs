using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace ZeroEngine.Quest.Editor
{
    [InitializeOnLoad]
    public static class QuestEditorQuestIdProvider
    {
        private static readonly List<string> CachedQuestIds = new();
        private static readonly HashSet<string> CachedQuestIdSet = new(StringComparer.Ordinal);
        private static bool cacheDirty = true;

        static QuestEditorQuestIdProvider()
        {
            EditorApplication.projectChanged += MarkDirty;
            QuestStringDropdownProviderRegistry.Register(QuestStringDropdownKind.QuestId, GetQuestIds);
        }

        public static IReadOnlyList<string> GetQuestIds()
        {
            EnsureCache();
            return CachedQuestIds;
        }

        public static bool ContainsQuestId(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return false;

            EnsureCache();
            return CachedQuestIdSet.Contains(questId.Trim());
        }

        public static void Refresh()
        {
            Refresh(QuestConfigValidator.LoadQuestAssets());
        }

        public static void Refresh(IEnumerable<QuestConfigSO> quests)
        {
            CachedQuestIds.Clear();
            CachedQuestIdSet.Clear();

            if (quests != null)
            {
                foreach (var questId in quests
                             .Where(quest => quest != null && !string.IsNullOrWhiteSpace(quest.questId))
                             .Select(quest => quest.questId.Trim())
                             .Distinct(StringComparer.Ordinal)
                             .OrderBy(id => id, StringComparer.Ordinal))
                {
                    CachedQuestIds.Add(questId);
                    CachedQuestIdSet.Add(questId);
                }
            }

            cacheDirty = false;
            QuestStringDropdownProviderRegistry.Refresh(QuestStringDropdownKind.QuestId);
        }

        private static void EnsureCache()
        {
            if (!cacheDirty)
                return;

            Refresh();
        }

        private static void MarkDirty()
        {
            cacheDirty = true;
        }
    }
}
