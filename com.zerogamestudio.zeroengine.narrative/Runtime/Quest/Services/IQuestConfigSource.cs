using System.Collections.Generic;

namespace ZeroEngine.Quest
{
    public interface IQuestConfigSource
    {
        IReadOnlyList<QuestConfigSO> LoadConfigs();
    }
}
