using System;
using System.Collections.Generic;

namespace ZeroEngine.Relationship
{
    public enum RelationshipActionKind { Talk, Gift, Points, Event }

    public readonly struct RelationshipCommandContext
    {
        public RelationshipCommandContext(Guid epoch, string ownerId, long revision, string date, string requestId)
        { Epoch = epoch; OwnerId = ownerId; Revision = revision; Date = date; RequestId = requestId; }
        public Guid Epoch { get; }
        public string OwnerId { get; }
        public long Revision { get; }
        public string Date { get; }
        public string RequestId { get; }
    }

    [Serializable]
    public sealed class RelationshipActionReceipt
    {
        public string RequestId, OwnerId, NpcId, ItemId, EventId, Date;
        public RelationshipActionKind Kind;
        public long FromRevision;
        public int RequestedPoints, PreviousPoints, CurrentPoints;
        public GiftPreference Preference;
        public bool Replayed;
        public RelationshipActionReceipt Clone() => (RelationshipActionReceipt)MemberwiseClone();
    }

    [Serializable]
    public sealed class RelationshipStateSnapshot
    {
        public int SchemaVersion;
        public string OwnerId, Date;
        public long Revision;
        public Dictionary<string, RelationshipProgress> ProgressData;
        public RelationshipActionReceipt LastReceipt;
    }
}
