using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Dialog;

namespace ZeroEngine.Narrative.Tests.Dialog
{
    [TestFixture]
    public sealed class DialogGraphValidatorTests
    {
        [Test]
        public void Validate_EmptyGraph_ReportsMissingStartAndEnd()
        {
            var graph = ScriptableObject.CreateInstance<DialogGraphSO>();
            try
            {
                var issues = DialogGraphValidator.Validate(graph, DialogGraphValidationOptions.Default);

                Assert.That(issues, Has.Some.Matches<DialogGraphValidationIssue>(issue => issue.Code == DialogGraphValidationCodes.MissingStartNode));
                Assert.That(issues, Has.Some.Matches<DialogGraphValidationIssue>(issue => issue.Code == DialogGraphValidationCodes.MissingEndNode));
            }
            finally
            {
                Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void Validate_BrokenOutputConnection_ReportsNodeAndTarget()
        {
            var graph = ScriptableObject.CreateInstance<DialogGraphSO>();
            try
            {
                graph.Nodes.Add(new DialogStartNode { OutputNodeId = "Missing_Node" });
                graph.Nodes.Add(new DialogEndNode());

                var issues = DialogGraphValidator.Validate(graph, DialogGraphValidationOptions.Default);

                Assert.That(issues, Has.Some.Matches<DialogGraphValidationIssue>(issue =>
                    issue.Code == DialogGraphValidationCodes.BrokenOutputConnection &&
                    issue.NodeId == "Start" &&
                    issue.TargetNodeId == "Missing_Node"));
            }
            finally
            {
                Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void Validate_UnknownCallbackCommand_ReportsCommandId()
        {
            var graph = ScriptableObject.CreateInstance<DialogGraphSO>();
            try
            {
                graph.Nodes.Add(new DialogStartNode { OutputNodeId = "Callback_1" });
                graph.Nodes.Add(new DialogCallbackNode
                {
                    Id = "Callback_1",
                    CallbackId = "unknown.command",
                    OutputNodeId = "End"
                });
                graph.Nodes.Add(new DialogEndNode());

                var issues = DialogGraphValidator.Validate(graph, DialogGraphValidationOptions.Default);

                Assert.That(issues, Has.Some.Matches<DialogGraphValidationIssue>(issue =>
                    issue.Code == DialogGraphValidationCodes.UnknownCommandId &&
                    issue.NodeId == "Callback_1" &&
                    issue.CommandId == "unknown.command"));
                Assert.IsFalse(issues.Any(issue => issue.Severity == DialogGraphValidationSeverity.Error));
            }
            finally
            {
                Object.DestroyImmediate(graph);
            }
        }
    }
}
