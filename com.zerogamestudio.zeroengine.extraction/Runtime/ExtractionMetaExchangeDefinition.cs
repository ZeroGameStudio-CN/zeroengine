using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    // D10 单向兑换白名单条目：Extraction 物品(货币) → 主线 meta 资源，汇率 = ExtractionQuantity
    // 换 MainlineQuantity。Core 层禁止引用主线 ResourceType(程序集边界)，资源用字符串名承载，
    // 解析与"只允许 meta 通道"的强制都在 Adapter 侧 applier。
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionMetaExchangeDefinition
    {
        public string ExchangeId;
        public string ExtractionItemDefinitionId;
        public int ExtractionQuantity;
        public string MainlineResourceName;
        public int MainlineQuantity;

        public bool IsValid =>
            !string.IsNullOrEmpty(ExchangeId)
            && !string.IsNullOrEmpty(ExtractionItemDefinitionId)
            && ExtractionQuantity > 0
            && !string.IsNullOrEmpty(MainlineResourceName)
            && MainlineQuantity > 0;

        public ExtractionMetaExchangeDefinition(
            string exchangeId,
            string extractionItemDefinitionId,
            int extractionQuantity,
            string mainlineResourceName,
            int mainlineQuantity)
        {
            ExchangeId = exchangeId;
            ExtractionItemDefinitionId = extractionItemDefinitionId;
            ExtractionQuantity = extractionQuantity;
            MainlineResourceName = mainlineResourceName;
            MainlineQuantity = mainlineQuantity;
        }
    }
}
