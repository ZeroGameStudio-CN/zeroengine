using System;
using ZeroEngine;

namespace POB.Extraction
{
    /// <summary>Per-raid immutable-by-convention copy; null in old manifests means legacy selection.</summary>
    [Serializable]
    public sealed class ExtractionLootSelectionPolicy
    {
        public int Version = 2;
        public float[] RarityWeights = { 1, 1, 1, 1, 1, 1 };
        public float Luck;
        public float LuckScale = 300;
        public float LuckStrength = 1.5f;

        public bool IsValid
        {
            get
            {
                if (Version != 2 || RarityWeights == null || RarityWeights.Length != 6
                    || !WeightedSelection.IsFinite(Luck) || !WeightedSelection.IsFinite(LuckScale)
                    || LuckScale <= 0 || !WeightedSelection.IsFinite(LuckStrength) || LuckStrength < 0) return false;
                bool any = false;
                foreach (float weight in RarityWeights)
                {
                    if (!WeightedSelection.IsFinite(weight) || weight < 0) return false;
                    any |= weight > 0;
                }
                return any;
            }
        }

        public ExtractionLootSelectionPolicy Copy() => new()
        {
            Version = Version, Luck = Luck, LuckScale = LuckScale, LuckStrength = LuckStrength,
            RarityWeights = RarityWeights == null ? null : (float[])RarityWeights.Clone()
        };
    }
}
