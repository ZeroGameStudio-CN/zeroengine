using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    public interface IExtractionLootTableCatalog
    {
        bool TryGetLootTable(string tableId, out ExtractionLootTableDefinition table);
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionLootTableDefinition : ExtractionLootRollTable
    {
        public string TableId;

        // M2 SD2.2c：珍品掉落开关的显式标记，取代此前"表 id 带 -rare 后缀"的命名约定判定。
        // additive 字段默认 false，向后兼容不配这个字段的旧配置/mod。
        public bool IsRare;

        public bool IsValid => !string.IsNullOrEmpty(TableId) && HasValidEntry();

        public ExtractionLootTableDefinition(string tableId)
        {
            TableId = tableId;
        }

        private bool HasValidEntry()
        {
            if (Entries == null) return false;

            foreach (var entry in Entries)
            {
                if (entry != null && entry.IsValid) return true;
            }

            return false;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionStaticLootTableCatalog : IExtractionLootTableCatalog
    {
        public List<ExtractionLootTableDefinition> LootTables = new();

        public bool TryGetLootTable(string tableId, out ExtractionLootTableDefinition table)
        {
            table = null;
            if (string.IsNullOrEmpty(tableId)) return false;

            foreach (var candidate in LootTables)
            {
                if (candidate == null || !candidate.IsValid) continue;
                if (candidate.TableId != tableId) continue;

                table = candidate;
                return true;
            }

            return false;
        }
    }
}
