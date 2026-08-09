using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceGraphValidatorTests
    {
        [Test]
        public void Validate_NullGraph_ReturnsError()
        {
            IReadOnlyList<TceValidationIssue> issues = TceGraphValidator.Validate(null);

            AssertHasIssue(issues, TceValidationCodes.NullGraph, "graph");
        }

        [Test]
        public void Validate_GraphWithoutTrigger_ReturnsMissingTriggerError()
        {
            var graph = new TceGraph();
            graph.AddEffect(new ValidEffectData());

            IReadOnlyList<TceValidationIssue> issues = TceGraphValidator.Validate(graph);

            AssertHasIssue(issues, TceValidationCodes.MissingTrigger, "triggers");
        }

        [Test]
        public void Validate_GraphWithoutEffect_ReturnsMissingEffectError()
        {
            var graph = new TceGraph();
            graph.AddTrigger(new ValidTriggerData());

            IReadOnlyList<TceValidationIssue> issues = TceGraphValidator.Validate(graph);

            AssertHasIssue(issues, TceValidationCodes.MissingEffect, "effects");
        }

        [Test]
        public void Validate_NullComponentData_ReturnsNullComponentError()
        {
            var graph = new TceGraph();
            AddRawTrigger(graph, null);
            graph.AddEffect(new ValidEffectData());

            IReadOnlyList<TceValidationIssue> issues = TceGraphValidator.Validate(graph);

            AssertHasIssue(issues, TceValidationCodes.NullComponent, "triggers[0]");
        }

        [Test]
        public void Validate_RuntimeTypeMismatch_ReturnsRuntimeTypeError()
        {
            var graph = new TceGraph();
            graph.AddTrigger(new MismatchedTriggerData());
            graph.AddEffect(new ValidEffectData());

            IReadOnlyList<TceValidationIssue> issues = TceGraphValidator.Validate(graph);

            AssertHasIssue(issues, TceValidationCodes.RuntimeTypeMismatch, "triggers[0]");
        }

        [Test]
        public void Validate_ComponentSpecificInvalidField_ReturnsComponentIssue()
        {
            var graph = new TceGraph();
            graph.AddTrigger(new ValidTriggerData());
            graph.AddCondition(new ValidatingConditionData { Invalid = true });
            graph.AddEffect(new ValidEffectData());

            IReadOnlyList<TceValidationIssue> issues = TceGraphValidator.Validate(graph);

            AssertHasIssue(issues, TceValidationCodes.InvalidField, "conditions[0].Invalid");
        }

        private static void AssertHasIssue(IReadOnlyList<TceValidationIssue> issues, string code, string path)
        {
            foreach (TceValidationIssue issue in issues)
            {
                if (issue.Code == code && issue.Path == path && issue.Severity == TceValidationSeverity.Error)
                    return;
            }

            Assert.Fail($"Expected validation issue {code} at {path}.");
        }

        private static void AddRawTrigger(TceGraph graph, TceTriggerData data)
        {
            FieldInfo field = typeof(TceGraph).GetField("triggers", BindingFlags.Instance | BindingFlags.NonPublic);
            var triggers = (List<TceTriggerData>)field.GetValue(graph);
            triggers.Add(data);
        }

        private sealed class ValidTriggerData : TceTriggerData<ValidTrigger>
        {
        }

        private sealed class ValidTrigger : TceTrigger<ValidTriggerData>
        {
        }

        private sealed class ValidEffectData : TceEffectData<ValidEffect>
        {
        }

        private sealed class ValidEffect : TceEffect<ValidEffectData>
        {
        }

        private sealed class MismatchedTriggerData : TceTriggerData
        {
            public override Type RuntimeType => typeof(ValidEffect);
        }

        private sealed class ValidatingConditionData : TceConditionData<ValidatingCondition>, ITceComponentDataValidator
        {
            public bool Invalid;

            public void Validate(TceComponentValidationContext context, List<TceValidationIssue> issues)
            {
                if (Invalid)
                    issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, $"{context.Path}.Invalid", "Invalid test field."));
            }
        }

        private sealed class ValidatingCondition : TceCondition<ValidatingConditionData>
        {
        }
    }
}
