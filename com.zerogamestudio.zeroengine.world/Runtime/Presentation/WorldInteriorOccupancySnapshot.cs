using System;

namespace ZeroEngine.World.Presentation
{
    public readonly struct WorldInteriorOccupancySnapshot : IEquatable<WorldInteriorOccupancySnapshot>
    {
        private readonly string _subjectId;
        private readonly string _interiorId;
        private readonly string _sourceId;
        private readonly string _reason;

        internal WorldInteriorOccupancySnapshot(
            long tokenId,
            WorldInteriorOccupancyRequest request)
        {
            IsOccupied = true;
            TokenId = tokenId;
            _subjectId = request.SubjectId;
            _interiorId = request.InteriorId;
            _sourceId = request.SourceId;
            _reason = request.Reason;
            Priority = request.Priority;
        }

        private WorldInteriorOccupancySnapshot(string subjectId)
        {
            IsOccupied = false;
            TokenId = 0;
            _subjectId = subjectId;
            _interiorId = string.Empty;
            _sourceId = string.Empty;
            _reason = string.Empty;
            Priority = 0;
        }

        public bool IsOccupied { get; }
        public long TokenId { get; }
        public string SubjectId => _subjectId ?? string.Empty;
        public string InteriorId => _interiorId ?? string.Empty;
        public string SourceId => _sourceId ?? string.Empty;
        public string Reason => _reason ?? string.Empty;
        public int Priority { get; }

        internal static WorldInteriorOccupancySnapshot Inactive(string subjectId)
        {
            return new WorldInteriorOccupancySnapshot(subjectId);
        }

        public bool Equals(WorldInteriorOccupancySnapshot other)
        {
            return IsOccupied == other.IsOccupied
                   && TokenId == other.TokenId
                   && string.Equals(SubjectId, other.SubjectId, StringComparison.Ordinal)
                   && string.Equals(InteriorId, other.InteriorId, StringComparison.Ordinal)
                   && string.Equals(SourceId, other.SourceId, StringComparison.Ordinal)
                   && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
                   && Priority == other.Priority;
        }

        public override bool Equals(object obj)
        {
            return obj is WorldInteriorOccupancySnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                IsOccupied,
                TokenId,
                SubjectId,
                InteriorId,
                SourceId,
                Reason,
                Priority);
        }

        public static bool operator ==(
            WorldInteriorOccupancySnapshot left,
            WorldInteriorOccupancySnapshot right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            WorldInteriorOccupancySnapshot left,
            WorldInteriorOccupancySnapshot right)
        {
            return !left.Equals(right);
        }
    }
}
