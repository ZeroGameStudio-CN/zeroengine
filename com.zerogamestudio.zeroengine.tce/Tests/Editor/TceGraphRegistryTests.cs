using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceGraphRegistryTests
    {
        [Test]
        public void TryRegister_ValidGraph_CanBeRetrieved()
        {
            var registry = new TceGraphRegistry();
            TceGraph graph = CreateRuntimeGraph();

            bool registered = registry.TryRegister("valid_graph", graph, out TceValidationIssue issue);

            Assert.IsTrue(registered, issue.Message);
            Assert.IsTrue(registry.TryGet("valid_graph", out TceGraph registeredGraph));
            Assert.AreSame(graph, registeredGraph);
        }

        [Test]
        public void TryRegister_DuplicateGraphId_ReturnsIssue()
        {
            var registry = new TceGraphRegistry();
            Assert.IsTrue(registry.TryRegister("duplicate", CreateRuntimeGraph(), out _));

            bool registered = registry.TryRegister("duplicate", CreateRuntimeGraph(), out TceValidationIssue issue);

            Assert.IsFalse(registered);
            Assert.AreEqual(TceValidationCodes.DuplicateGraphId, issue.Code);
        }

        [Test]
        public void BatchImport_InvalidDocument_DoesNotBlockValidDocuments()
        {
            TceExternalGraphDocument first = CreateExternalDocument("first");
            TceExternalGraphDocument invalid = CreateExternalDocument("invalid");
            invalid.Effects.Add(new TceExternalGraphNode("not.allowed"));
            TceExternalGraphDocument second = CreateExternalDocument("second");
            var registry = new TceGraphRegistry();

            TceExternalGraphImportBatchResult result = TceExternalGraphImportBatch.Import(
                new[] { first, invalid, second },
                TceComponentRegistry.CreateDefault(),
                registry);

            Assert.AreEqual(3, result.Results.Count);
            Assert.That(result.Results.Count(item => item.Succeeded), Is.EqualTo(2));
            Assert.IsTrue(registry.TryGet("first", out _));
            Assert.IsFalse(registry.TryGet("invalid", out _));
            Assert.IsTrue(registry.TryGet("second", out _));
        }

        private static TceGraph CreateRuntimeGraph()
        {
            var graph = new TceGraph();
            graph.AddTrigger(new OnInstallTriggerData());
            graph.AddEffect(new DebugLogEffectData { Message = "accepted" });
            return graph;
        }

        private static TceExternalGraphDocument CreateExternalDocument(string graphId)
        {
            var document = new TceExternalGraphDocument
            {
                Format = TceGraphSchema.Format,
                SchemaVersion = TceGraphSchema.CurrentVersion,
                GraphId = graphId,
                DisplayName = graphId
            };

            document.Triggers.Add(new TceExternalGraphNode("zeroengine.tce.trigger.on_install"));
            document.Effects.Add(new TceExternalGraphNode(
                "zeroengine.tce.effect.debug_log",
                new Dictionary<string, object> { ["Message"] = "accepted" }));
            return document;
        }
    }
}
