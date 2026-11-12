using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZeroEngine.World.WorldGraph
{
    [CreateAssetMenu(fileName = "WorldGraphConnectionNetwork", menuName = "ZeroEngine/World/World Graph Connection Network")]
    public sealed class WorldGraphConnectionNetworkSO : ScriptableObject
    {
        [SerializeField] private List<WorldGraphConnectionDefinition> _connections = new();

        public IReadOnlyList<WorldGraphConnectionDefinition> Connections => _connections;

        public bool TryFindByBoundary(
            string sourceWorldGraphId,
            string sourceCellId,
            string sourceBoundaryId,
            out WorldGraphConnectionDefinition connection)
        {
            connection = _connections.FirstOrDefault(candidate =>
                candidate != null
                && candidate.SourceWorldGraphId == sourceWorldGraphId
                && candidate.SourceCellId == sourceCellId
                && candidate.SourceBoundaryId == sourceBoundaryId);
            return connection != null;
        }

        public bool TryFindByConnectionId(
            string connectionId,
            out WorldGraphConnectionDefinition connection)
        {
            connection = _connections.FirstOrDefault(candidate =>
                candidate != null
                && candidate.ConnectionId == connectionId);
            return connection != null;
        }

#if UNITY_EDITOR
        public void ConfigureForTests(IEnumerable<WorldGraphConnectionDefinition> connections)
        {
            _connections = connections?.ToList() ?? new List<WorldGraphConnectionDefinition>();
        }
#endif
    }
}
