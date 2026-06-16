using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.TCE;

public sealed class ModGraphImportExample : MonoBehaviour
{
    [SerializeField] private string graphId = "burning_hit";
    [SerializeField] private string message = "Burning Hit accepted.";

    private readonly TceGraphRegistry graphRegistry = new();

    public bool TryImport(out TceGraph graph)
    {
        TceExternalGraphDocument document = CreateDocument();
        TceExternalGraphImportBatchResult result = TceExternalGraphImportBatch.Import(
            new[] { document },
            TceComponentRegistry.CreateDefault(),
            graphRegistry);

        graph = null;
        return result.Results.Count == 1 &&
            result.Results[0].Succeeded &&
            graphRegistry.TryGet(graphId, out graph);
    }

    private TceExternalGraphDocument CreateDocument()
    {
        var document = new TceExternalGraphDocument
        {
            Format = TceGraphSchema.Format,
            SchemaVersion = TceGraphSchema.CurrentVersion,
            GraphId = graphId,
            DisplayName = "Burning Hit"
        };

        document.Triggers.Add(new TceExternalGraphNode("zeroengine.tce.trigger.on_install"));
        document.Effects.Add(new TceExternalGraphNode(
            "zeroengine.tce.effect.debug_log",
            new Dictionary<string, object> { ["Message"] = message }));
        return document;
    }
}
