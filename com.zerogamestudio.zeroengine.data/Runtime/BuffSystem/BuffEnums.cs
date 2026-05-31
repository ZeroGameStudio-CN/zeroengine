namespace ZeroEngine.BuffSystem
{
    /// <summary>
    /// Buff 分类
    /// </summary>
    public enum BuffCategory
    {
        /// <summary>增益效果</summary>
        Buff,
        /// <summary>减益效果</summary>
        Debuff,
        /// <summary>中性效果</summary>
        Neutral
    }

    /// <summary>
    /// Buff 过期模式
    /// </summary>
    public enum BuffExpireMode
    {
        /// <summary>过期时移除所有层数</summary>
        RemoveAllStacks,
        /// <summary>过期时只移除一层</summary>
        RemoveOneStack
    }

    /// <summary>
    /// Buff 触发时机
    /// </summary>
    public enum BuffTriggerTiming
    {
        /// <summary>添加时触发</summary>
        Add,
        /// <summary>移除时触发</summary>
        Remove,
        /// <summary>达到最大层数时触发</summary>
        MaxStacks
    }

    /// <summary>
    /// Buff 事件类型 (v1.2.0+)
    /// </summary>
    public enum BuffEventType
    {
        Applied,    // 新 Buff 被施加
        Refreshed,  // 持续时间刷新
        Stacked,    // 层数增加
        Unstacked,  // 层数减少
        Expired,    // 持续时间到期
        Removed     // 被主动移除
    }

    /// <summary>
    /// Buff 堆叠模式 (v1.2.0+)
    /// </summary>
    public enum BuffStackMode
    {
        Stack,      // 累加层数（默认）
        Refresh,    // 只刷新持续时间，不增加层数
        Replace     // 替换为新的 Buff
    }
}
