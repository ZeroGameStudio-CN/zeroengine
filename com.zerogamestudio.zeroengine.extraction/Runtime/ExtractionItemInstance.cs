using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Flags]
    public enum ExtractionItemInstanceFlags
    {
        None = 0,
        CanDrop = 1 << 0,
        CanSell = 1 << 1,
        DropOnDeath = 1 << 2,
        PolicyInitialized = 1 << 3,
        RaidBound = 1 << 4,
        DestroyOnSettlement = 1 << 5
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionItemInstance
    {
        public string InstanceId;
        public string DefinitionId;
        public int Quantity;
        public string SourceKind;
        public string SourceId;
        public int CurrentDurability;
        public int EnhancementLevel;
        public int ForgeTier;
        public List<string> AffixIds = new();
        public ExtractionItemInstanceFlags Flags = DefaultLegacyFlags;

        public const ExtractionItemInstanceFlags DefaultLegacyFlags =
            ExtractionItemInstanceFlags.CanDrop
            | ExtractionItemInstanceFlags.CanSell
            | ExtractionItemInstanceFlags.DropOnDeath
            | ExtractionItemInstanceFlags.PolicyInitialized;

        public ExtractionItemInstance(string instanceId, string definitionId, int quantity)
            : this(instanceId, definitionId, quantity, null, null)
        {
        }

        public ExtractionItemInstance(
            string instanceId,
            string definitionId,
            int quantity,
            string sourceKind,
            string sourceId)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Quantity = quantity;
            SourceKind = sourceKind;
            SourceId = sourceId;
        }

        internal ExtractionItemInstance(ExtractionItemInstance source)
            : this(
                source.InstanceId,
                source.DefinitionId,
                source.Quantity,
                source.SourceKind,
                source.SourceId)
        {
            CurrentDurability = source.CurrentDurability;
            EnhancementLevel = source.EnhancementLevel;
            ForgeTier = source.ForgeTier;
            Flags = source.Flags;
            AffixIds = source.AffixIds != null
                ? new List<string>(source.AffixIds)
                : new List<string>();
            EnsureInitialized();
        }

        public bool HasFlag(ExtractionItemInstanceFlags flag)
        {
            return (Flags & flag) == flag;
        }

        internal void EnsureInitialized()
        {
            AffixIds ??= new List<string>();
            if (!HasFlag(ExtractionItemInstanceFlags.PolicyInitialized))
                Flags |= DefaultLegacyFlags;
        }
    }
}
