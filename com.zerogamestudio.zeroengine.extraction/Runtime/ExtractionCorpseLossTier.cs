namespace POB.Extraction
{
    // M2 SD2.1：尸袋档；Default(0) 是现状行为(raid背包+装备全丢，secure容器保留)，必须保持
    // 枚举序号 0，让旧存档反序列化后默认落在这一档而不改变已有玩家体感。
    public enum ExtractionCorpseLossTier
    {
        Default = 0,
        Lenient = 1,
        Hardcore = 2
    }
}
