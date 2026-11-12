using System;

namespace ZeroEngine.World.Presentation
{
    public sealed class WorldCameraPresentationRequest
    {
        public WorldCameraPresentationRequest(
            string profileId,
            string followTargetId,
            string lookTargetId,
            string blendInProfileId,
            string blendOutProfileId,
            string scopeId,
            string sourceId,
            string reason,
            int priority)
        {
            ProfileId = NormalizeRequiredId(profileId, nameof(profileId));
            FollowTargetId = NormalizeRequiredId(followTargetId, nameof(followTargetId));
            LookTargetId = lookTargetId?.Trim() ?? string.Empty;
            BlendInProfileId = NormalizeRequiredId(blendInProfileId, nameof(blendInProfileId));
            BlendOutProfileId = NormalizeRequiredId(blendOutProfileId, nameof(blendOutProfileId));
            ScopeId = NormalizeRequiredId(scopeId, nameof(scopeId));
            SourceId = NormalizeRequiredId(sourceId, nameof(sourceId));
            Reason = reason?.Trim() ?? string.Empty;
            Priority = priority;
        }

        public string ProfileId { get; }
        public string FollowTargetId { get; }
        public string LookTargetId { get; }
        public string BlendInProfileId { get; }
        public string BlendOutProfileId { get; }
        public string ScopeId { get; }
        public string SourceId { get; }
        public string Reason { get; }
        public int Priority { get; }

        private static string NormalizeRequiredId(string value, string parameterName)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                throw new ArgumentException("A non-empty presentation identifier is required.", parameterName);
            }

            return normalized;
        }
    }
}
