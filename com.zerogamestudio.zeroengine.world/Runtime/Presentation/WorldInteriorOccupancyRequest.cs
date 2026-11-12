using System;

namespace ZeroEngine.World.Presentation
{
    public sealed class WorldInteriorOccupancyRequest
    {
        public WorldInteriorOccupancyRequest(
            string subjectId,
            string interiorId,
            string sourceId,
            string reason,
            int priority)
        {
            SubjectId = NormalizeRequiredId(subjectId, nameof(subjectId));
            InteriorId = NormalizeRequiredId(interiorId, nameof(interiorId));
            SourceId = NormalizeRequiredId(sourceId, nameof(sourceId));
            Reason = reason?.Trim() ?? string.Empty;
            Priority = priority;
        }

        public string SubjectId { get; }
        public string InteriorId { get; }
        public string SourceId { get; }
        public string Reason { get; }
        public int Priority { get; }

        private static string NormalizeRequiredId(string value, string parameterName)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                throw new ArgumentException("A non-empty occupancy identifier is required.", parameterName);
            }

            return normalized;
        }
    }
}
