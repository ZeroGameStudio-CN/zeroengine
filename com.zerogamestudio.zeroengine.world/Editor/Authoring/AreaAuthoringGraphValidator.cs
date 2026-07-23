using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.World.Authoring
{
    public readonly struct AreaAuthoringPortalLocation
    {
        public AreaAuthoringPortalLocation(string portalId, string areaId)
        {
            PortalId = portalId;
            AreaId = areaId;
        }

        public string PortalId { get; }
        public string AreaId { get; }
    }

    public readonly struct AreaAuthoringPortalConnection
    {
        public AreaAuthoringPortalConnection(string fromPortalId, string toPortalId)
        {
            FromPortalId = fromPortalId;
            ToPortalId = toPortalId;
        }

        public string FromPortalId { get; }
        public string ToPortalId { get; }
    }

    public static class AreaAuthoringGraphValidator
    {
        public static IReadOnlyList<AreaAuthoringIssue> ValidatePortalGraph(
            IEnumerable<string> knownAreaIds,
            IEnumerable<AreaAuthoringPortalLocation> portalLocations,
            IEnumerable<AreaAuthoringPortalConnection> portalConnections,
            string assetPath)
        {
            var issues = new List<AreaAuthoringIssue>();
            var knownAreas = new HashSet<string>((knownAreaIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)));
            var knownPortalIds = new HashSet<string>();

            foreach (var location in portalLocations ?? Enumerable.Empty<AreaAuthoringPortalLocation>())
            {
                if (string.IsNullOrWhiteSpace(location.PortalId))
                {
                    issues.Add(Error("PORTAL_ID_EMPTY", "Portal locations must use stable portal ids.", assetPath));
                    continue;
                }

                if (!knownPortalIds.Add(location.PortalId))
                {
                    issues.Add(Error("PORTAL_ID_DUPLICATE", $"Duplicate portal id '{location.PortalId}' found.", assetPath, location.PortalId));
                }

                if (!knownAreas.Contains(location.AreaId))
                {
                    issues.Add(Error("PORTAL_LOCATION_UNKNOWN_AREA", $"Portal {location.PortalId} points to unknown area '{location.AreaId}'.", assetPath, location.PortalId));
                }
            }

            foreach (var connection in portalConnections ?? Enumerable.Empty<AreaAuthoringPortalConnection>())
            {
                if (!knownPortalIds.Contains(connection.FromPortalId))
                {
                    issues.Add(Error("PORTAL_CONNECTION_ENDPOINT_MISSING", $"Connection from portal '{connection.FromPortalId}' is not indexed.", assetPath, connection.FromPortalId));
                }

                if (!knownPortalIds.Contains(connection.ToPortalId))
                {
                    issues.Add(Error("PORTAL_CONNECTION_ENDPOINT_MISSING", $"Connection to portal '{connection.ToPortalId}' is not indexed.", assetPath, connection.ToPortalId));
                }
            }

            return issues;
        }

        private static AreaAuthoringIssue Error(string code, string message, string assetPath = null, string contextId = null)
        {
            return new AreaAuthoringIssue(AreaAuthoringIssueSeverity.Error, code, message, assetPath, contextId);
        }
    }
}
