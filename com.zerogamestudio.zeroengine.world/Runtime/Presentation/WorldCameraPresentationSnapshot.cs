using System;

namespace ZeroEngine.World.Presentation
{
    public readonly struct WorldCameraPresentationSnapshot : IEquatable<WorldCameraPresentationSnapshot>
    {
        private readonly string _profileId;
        private readonly string _followTargetId;
        private readonly string _lookTargetId;
        private readonly string _blendInProfileId;
        private readonly string _blendOutProfileId;
        private readonly string _scopeId;
        private readonly string _sourceId;
        private readonly string _reason;

        internal WorldCameraPresentationSnapshot(
            long tokenId,
            WorldCameraPresentationRequest request)
        {
            IsActive = true;
            TokenId = tokenId;
            _profileId = request.ProfileId;
            _followTargetId = request.FollowTargetId;
            _lookTargetId = request.LookTargetId;
            _blendInProfileId = request.BlendInProfileId;
            _blendOutProfileId = request.BlendOutProfileId;
            _scopeId = request.ScopeId;
            _sourceId = request.SourceId;
            _reason = request.Reason;
            Priority = request.Priority;
        }

        public bool IsActive { get; }
        public long TokenId { get; }
        public string ProfileId => _profileId ?? string.Empty;
        public string FollowTargetId => _followTargetId ?? string.Empty;
        public string LookTargetId => _lookTargetId ?? string.Empty;
        public string BlendInProfileId => _blendInProfileId ?? string.Empty;
        public string BlendOutProfileId => _blendOutProfileId ?? string.Empty;
        public string ScopeId => _scopeId ?? string.Empty;
        public string SourceId => _sourceId ?? string.Empty;
        public string Reason => _reason ?? string.Empty;
        public int Priority { get; }

        public bool Equals(WorldCameraPresentationSnapshot other)
        {
            return IsActive == other.IsActive
                   && TokenId == other.TokenId
                   && string.Equals(ProfileId, other.ProfileId, StringComparison.Ordinal)
                   && string.Equals(FollowTargetId, other.FollowTargetId, StringComparison.Ordinal)
                   && string.Equals(LookTargetId, other.LookTargetId, StringComparison.Ordinal)
                   && string.Equals(BlendInProfileId, other.BlendInProfileId, StringComparison.Ordinal)
                   && string.Equals(BlendOutProfileId, other.BlendOutProfileId, StringComparison.Ordinal)
                   && string.Equals(ScopeId, other.ScopeId, StringComparison.Ordinal)
                   && string.Equals(SourceId, other.SourceId, StringComparison.Ordinal)
                   && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
                   && Priority == other.Priority;
        }

        public override bool Equals(object obj)
        {
            return obj is WorldCameraPresentationSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(IsActive);
            hashCode.Add(TokenId);
            hashCode.Add(ProfileId, StringComparer.Ordinal);
            hashCode.Add(FollowTargetId, StringComparer.Ordinal);
            hashCode.Add(LookTargetId, StringComparer.Ordinal);
            hashCode.Add(BlendInProfileId, StringComparer.Ordinal);
            hashCode.Add(BlendOutProfileId, StringComparer.Ordinal);
            hashCode.Add(ScopeId, StringComparer.Ordinal);
            hashCode.Add(SourceId, StringComparer.Ordinal);
            hashCode.Add(Reason, StringComparer.Ordinal);
            hashCode.Add(Priority);
            return hashCode.ToHashCode();
        }

        public static bool operator ==(
            WorldCameraPresentationSnapshot left,
            WorldCameraPresentationSnapshot right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            WorldCameraPresentationSnapshot left,
            WorldCameraPresentationSnapshot right)
        {
            return !left.Equals(right);
        }
    }
}
