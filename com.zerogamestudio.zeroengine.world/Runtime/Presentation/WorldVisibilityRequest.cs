using System;

namespace ZeroEngine.World.Presentation
{
    public sealed class WorldVisibilityRequest
    {
        public WorldVisibilityRequest(
            string targetId,
            string channelId,
            string sourceId,
            string reason,
            float visibility)
        {
            TargetId = NormalizeRequiredId(targetId, nameof(targetId));
            ChannelId = NormalizeRequiredId(channelId, nameof(channelId));
            SourceId = NormalizeRequiredId(sourceId, nameof(sourceId));
            if (float.IsNaN(visibility)
                || float.IsInfinity(visibility)
                || visibility < 0f
                || visibility > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(visibility),
                    visibility,
                    "Visibility must be finite and between zero and one.");
            }

            Reason = reason?.Trim() ?? string.Empty;
            Visibility = visibility;
        }

        public string TargetId { get; }
        public string ChannelId { get; }
        public string SourceId { get; }
        public string Reason { get; }
        public float Visibility { get; }

        private static string NormalizeRequiredId(string value, string parameterName)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                throw new ArgumentException("A non-empty visibility identifier is required.", parameterName);
            }

            return normalized;
        }
    }
}
