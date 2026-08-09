using System;
using System.Collections.Generic;

namespace ZeroEngine.TCE
{
    [Serializable]
    public sealed class TceExternalGraphDocument
    {
        public string Format { get; set; } = TceGraphSchema.Format;
        public int SchemaVersion { get; set; } = TceGraphSchema.CurrentVersion;
        public string GraphId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<TceExternalGraphNode> Triggers { get; } = new();
        public List<TceExternalGraphNode> Conditions { get; } = new();
        public List<TceExternalGraphNode> Effects { get; } = new();
    }

    [Serializable]
    public sealed class TceExternalGraphNode
    {
        public TceExternalGraphNode()
            : this(string.Empty)
        {
        }

        public TceExternalGraphNode(string componentId)
            : this(componentId, null)
        {
        }

        public TceExternalGraphNode(string componentId, IDictionary<string, object> fields)
        {
            ComponentId = componentId ?? string.Empty;
            Fields = fields == null
                ? new Dictionary<string, object>(StringComparer.Ordinal)
                : new Dictionary<string, object>(fields, StringComparer.Ordinal);
        }

        public string ComponentId { get; set; }
        public Dictionary<string, object> Fields { get; }
    }
}
