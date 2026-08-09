using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Formula.Editor;
using UnityObject = UnityEngine.Object;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    public sealed class FormulaMigrationTests
    {
        [Test]
        public void ProviderIdRename_DryRunReportsImpactedStepsWithoutMutation()
        {
            var formula = CreateFormula(new[]
            {
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Provider("old.provider")),
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Provider("other.provider")),
            });

            try
            {
                var report = FormulaMigration.ProviderIdRename(
                    formula,
                    "old.provider",
                    "new.provider",
                    apply: false);

                Assert.That(report.Kind, Is.EqualTo(FormulaMigrationKind.ProviderIdRename));
                Assert.That(report.Applied, Is.False);
                Assert.That(report.Changes.Count, Is.EqualTo(1));
                Assert.That(report.Changes[0].StepIndex, Is.EqualTo(0));
                formula.TryGetStep(0, out var firstStep);
                Assert.That(firstStep.Source.ProviderId, Is.EqualTo("old.provider"));
            }
            finally
            {
                UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void ProviderIdRename_ApplyMutatesMatchingProviderOnly()
        {
            var formula = CreateFormula(new[]
            {
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Provider("old.provider")),
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Provider("other.provider")),
            });

            try
            {
                var report = FormulaMigration.ProviderIdRename(
                    formula,
                    "old.provider",
                    "new.provider",
                    apply: true);

                formula.TryGetStep(0, out var firstStep);
                formula.TryGetStep(1, out var secondStep);
                Assert.That(report.Applied, Is.True);
                Assert.That(firstStep.Source.ProviderId, Is.EqualTo("new.provider"));
                Assert.That(secondStep.Source.ProviderId, Is.EqualTo("other.provider"));
            }
            finally
            {
                UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void ParameterKeyRename_ApplyMutatesMatchingProviderParameterOnly()
        {
            var formula = CreateFormula(new[]
            {
                FormulaStep.Create(
                    FormulaOperationType.Add,
                    FormulaValueSource.Provider("stat.current", FormulaParameter.Int("oldKey", 1))),
                FormulaStep.Create(
                    FormulaOperationType.Add,
                    FormulaValueSource.Provider("other.provider", FormulaParameter.Int("oldKey", 1))),
            });

            try
            {
                var report = FormulaMigration.ParameterKeyRename(
                    formula,
                    "stat.current",
                    "oldKey",
                    "newKey",
                    apply: true);

                formula.TryGetStep(0, out var firstStep);
                formula.TryGetStep(1, out var secondStep);
                Assert.That(report.Kind, Is.EqualTo(FormulaMigrationKind.ParameterKeyRename));
                Assert.That(report.Changes.Count, Is.EqualTo(1));
                Assert.That(firstStep.Source.Parameters[0].Name, Is.EqualTo("newKey"));
                Assert.That(secondStep.Source.Parameters[0].Name, Is.EqualTo("oldKey"));
            }
            finally
            {
                UnityObject.DestroyImmediate(formula);
            }
        }

        [Test]
        public void MigrationReportExporter_ExportsJsonAndMarkdown()
        {
            var formula = CreateFormula(new[]
            {
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Provider("old.provider")),
            });

            try
            {
                var report = FormulaMigration.ProviderIdRename(
                    formula,
                    "old.provider",
                    "new.provider",
                    apply: false);

                var json = FormulaMigrationReportExporter.ToJson(report);
                var markdown = FormulaMigrationReportExporter.ToMarkdown(report);

                StringAssert.Contains("\"kind\":\"ProviderIdRename\"", json);
                StringAssert.Contains("\"changeCount\":1", json);
                StringAssert.Contains("# Formula Migration Report", markdown);
                StringAssert.Contains("old.provider", markdown);
                StringAssert.Contains("new.provider", markdown);
            }
            finally
            {
                UnityObject.DestroyImmediate(formula);
            }
        }

        private static FormulaAsset CreateFormula(IEnumerable<FormulaStep> steps)
        {
            var formula = ScriptableObject.CreateInstance<FormulaAsset>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FormulaAsset).GetField("initialValue", flags)?.SetValue(formula, 0f);
            typeof(FormulaAsset).GetField("steps", flags)?.SetValue(formula, new List<FormulaStep>(steps));
            return formula;
        }
    }
}
