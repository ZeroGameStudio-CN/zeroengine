using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionItemDefinition
    {
        public string DefinitionId;
        public int Width;
        public int Height;
        public bool CanRotate;
        public int MaxStack;
        public bool CanEmergencyExtract;

        // M1 R4 新增（additive，构造函数签名不变，避免动全部既有调用点）：
        // Value 是出售/兑换等经济系统的基准单价；DisplayNameKey/IconKey 是本地化/图标资源键，
        // 参照 POBPickup 的 nameKey 惯例；Tags 供内容筛选/UI 分组，非空即用、允许为空。
        public int Value;
        public ExtractionItemRarity Rarity;
        public string DisplayNameKey;
        public string IconKey;
        public string[] Tags;

        // M2 SW.1 新增（additive，同上原则）：Weight 是负重系统的单位重量基准（每份数量的重量，
        // 总重=Weight×Quantity 累加），默认 0 = 不参与负重计算，兼容未配置该字段的旧物品/mod。
        public int Weight;

        public ExtractionItemDefinition(string definitionId, int width, int height, bool canRotate, int maxStack)
            : this(definitionId, width, height, canRotate, maxStack, false)
        {
        }

        public ExtractionItemDefinition(
            string definitionId,
            int width,
            int height,
            bool canRotate,
            int maxStack,
            bool canEmergencyExtract)
        {
            DefinitionId = definitionId;
            Width = width;
            Height = height;
            CanRotate = canRotate;
            MaxStack = maxStack;
            CanEmergencyExtract = canEmergencyExtract;
        }
    }
}
