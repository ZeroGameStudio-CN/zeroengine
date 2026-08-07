namespace POB.Extraction
{
    // M2 SD2.1：敌伤档；Normal(0) = ×1，是现状行为，必须保持枚举序号 0，让旧存档反序列化后
    // 默认落在这一档而不改变已有玩家体感。
    public enum ExtractionEnemyDamageTier
    {
        Normal = 0,
        Half = 1,
        Hard = 2
    }
}
