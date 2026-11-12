using System;

namespace ZeroEngine.Crafting
{
    /// <summary>Saved escrow/output data uses stable item IDs, never Unity object references.</summary>
    [Serializable]
    public sealed class CraftingItemAmount
    {
        public string ItemId;
        public int Amount;

        public CraftingItemAmount Clone() => new CraftingItemAmount { ItemId = ItemId, Amount = Amount };
    }

    [Serializable]
    public sealed class CraftingReceipt
    {
        public string RequestId;
        public string RecipeId;
        public string WorkbenchId;
        public int BatchCount;
        public CraftingResult Result;
        public bool Pending;

        public CraftingReceipt Clone() => (CraftingReceipt)MemberwiseClone();
    }
}
