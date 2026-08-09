using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    // M1 SD.3：一局 raid 结束时的物品变化清单——成功时是"这次带回 Stash 的物品"，失败时是
    // "这次丢进 Lost 的物品"(直接复用 ExtractionSettlementService 内部早就算好、之前被调用方
    // out _ 丢掉的那份数据)。同时作为 OnRaidEnded 的事件载荷和 ExtractionRuntimeService 内部
    // "上一局战报"缓存的唯一数据形状，避免两处各维护一份。
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionRaidSettlementReport
    {
        public bool Success;
        public string Reason;
        public List<string> ItemInstanceIds = new();

        public ExtractionRaidSettlementReport(bool success, string reason, IEnumerable<string> itemInstanceIds)
        {
            Success = success;
            Reason = reason;
            if (itemInstanceIds != null)
                ItemInstanceIds.AddRange(itemInstanceIds);
        }
    }
}
