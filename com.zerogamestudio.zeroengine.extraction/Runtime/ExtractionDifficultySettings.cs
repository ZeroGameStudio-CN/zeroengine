using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    // M2 SD2.1：玩家当前选择的难度档位（profile 字段）。三个字段的"零值"都必须对应现状行为——
    // CorpseLossTier.Default=0、EnemyDamageTier.Normal=0、RareLootDisabled=false——这样旧存档
    // 缺这个字段时，不论 JsonUtility 是保留字段初始值还是回落到类型默认值，结果都是现状行为，
    // 满足"滑块各档存档兼容"验收项。
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionDifficultySettings
    {
        public ExtractionCorpseLossTier CorpseLossTier;
        public ExtractionEnemyDamageTier EnemyDamageTier;
        public bool RareLootDisabled;
    }
}
