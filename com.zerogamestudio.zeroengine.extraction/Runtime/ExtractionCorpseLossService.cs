using System.Collections.Generic;

namespace POB.Extraction
{
    // M2 SD2.2a：尸袋档"宽松"档的拆分算法。Default/Hardcore 不需要算法(全丢或全保留，调用方直接用
    // 既有的全量 id 列表)，只有 Lenient 需要"挑哪一半丢"——按物品价值升序排序，价值最低的
    // lossFraction 比例(向下取整，宽松档尽量少丢)记为丢失，价值最高的那部分记为保留。
    // 复合排序键(Value, OriginalIndex)让同价值物品按传入顺序稳定排列，结果可复现，不依赖
    // List.Sort 本身是否稳定的实现细节。
    public static class ExtractionCorpseLossService
    {
        public static void SplitLenientTierItems(
            List<(string InstanceId, int Value)> items,
            float lossFraction,
            List<string> lostItemIds,
            List<string> keptItemIds)
        {
            lostItemIds.Clear();
            keptItemIds.Clear();
            if (items == null || items.Count == 0) return;

            var indexed = new List<(string InstanceId, int Value, int OriginalIndex)>(items.Count);
            for (int i = 0; i < items.Count; i++)
                indexed.Add((items[i].InstanceId, items[i].Value, i));

            indexed.Sort((a, b) =>
            {
                int valueCompare = a.Value.CompareTo(b.Value);
                return valueCompare != 0 ? valueCompare : a.OriginalIndex.CompareTo(b.OriginalIndex);
            });

            int lossCount = (int)(indexed.Count * lossFraction);
            for (int i = 0; i < indexed.Count; i++)
            {
                if (i < lossCount) lostItemIds.Add(indexed[i].InstanceId);
                else keptItemIds.Add(indexed[i].InstanceId);
            }
        }
    }
}
