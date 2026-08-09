using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    public sealed class FormulaEvaluatorTests
    {
        private readonly List<Object> createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in createdObjects)
            {
                if (obj)
                    Object.DestroyImmediate(obj);
            }

            createdObjects.Clear();
        }

        [Test]
        public void TryEvaluate_WithArithmeticSteps_ReturnsStepTrace()
        {
            var formula = CreateFormula(10f,
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Constant(5f)),
                FormulaStep.Create(FormulaOperationType.Subtract, FormulaValueSource.Constant(3f)),
                FormulaStep.Create(FormulaOperationType.Multiply, FormulaValueSource.Constant(2f)),
                FormulaStep.Create(FormulaOperationType.Divide, FormulaValueSource.Constant(4f)),
                FormulaStep.Create(FormulaOperationType.MultiplyFactor, FormulaValueSource.Constant(0.5f)));

            var success = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                out var value,
                out var report);

            Assert.IsTrue(success);
            Assert.AreEqual(9f, value, 0.0001f);
            Assert.IsFalse(report.HasErrors);
            Assert.AreEqual(5, report.Steps.Count);
            Assert.AreEqual(FormulaOperationType.MultiplyFactor, report.Steps[4].Operation);
        }

        [Test]
        public void TryEvaluate_WithDivideByZero_KeepsPreviousResultAndWarns()
        {
            var formula = CreateFormula(10f,
                FormulaStep.Create(FormulaOperationType.Divide, FormulaValueSource.Constant(0f)));

            var success = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                out var value,
                out var report);

            Assert.IsTrue(success);
            Assert.AreEqual(10f, value, 0.0001f);
            Assert.IsFalse(report.HasErrors);
            Assert.IsTrue(report.HasWarnings);
            Assert.That(report.Diagnostics[0].Code, Is.EqualTo(FormulaDiagnosticCode.DivideByZero));
        }

        [Test]
        public void TryEvaluate_WithNullStep_FailsWithFallback()
        {
            var formula = CreateFormula(10f, new FormulaStep[] { null });

            var success = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                out var value,
                out var report);

            Assert.IsFalse(success);
            Assert.AreEqual(0f, value, 0.0001f);
            Assert.IsFalse(report.Succeeded);
            Assert.That(report.Diagnostics.Any(d => d.Code == FormulaDiagnosticCode.InvalidOperation), Is.True);
        }

        [Test]
        public void TryEvaluate_WithNestedFormula_ReturnsNestedResultAndChildReport()
        {
            var nested = CreateFormula(3f,
                FormulaStep.Create(FormulaOperationType.Multiply, FormulaValueSource.Constant(4f)));
            var formula = CreateFormula(2f,
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Nested(nested)));

            var success = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                out var value,
                out var report);

            Assert.IsTrue(success);
            Assert.AreEqual(14f, value, 0.0001f);
            Assert.AreEqual(1, report.ChildReports.Count);
            Assert.AreEqual(nested.FormulaName, report.ChildReports[0].FormulaName);
        }

        [Test]
        public void TryEvaluate_WithCircularReference_FailsWithoutThrowing()
        {
            var formula = CreateFormula(1f);
            formula.Initialize(1f, new[]
            {
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Nested(formula)),
            });

            var success = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                out var value,
                out var report);

            Assert.IsFalse(success);
            Assert.AreEqual(0f, value, 0.0001f);
            Assert.IsFalse(report.Succeeded);
            Assert.That(report.Diagnostics.Any(d => d.Code == FormulaDiagnosticCode.CircularReference), Is.True);
        }

        [Test]
        public void TryEvaluate_WithRandomIntegerAndNoSource_FailsWithDiagnostic()
        {
            var formula = CreateFormula(0f,
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.RandomInteger(3, 7)));

            var success = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                out var value,
                out var report);

            Assert.IsFalse(success);
            Assert.AreEqual(0f, value);
            Assert.That(report.Diagnostics.Any(d => d.Code == FormulaDiagnosticCode.MissingRandomSource), Is.True);
        }

        [Test]
        public void TryEvaluate_WithFixedRandomRange_StillConsumesOneSample()
        {
            var formula = CreateFormula(1f,
                FormulaStep.Create(FormulaOperationType.Multiply, FormulaValueSource.RandomInteger(7, 7)));
            var random = new RecordingRandomSource(7);

            var success = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                random,
                out var value,
                out var report);

            Assert.IsTrue(success, string.Join("\n", report.Diagnostics));
            Assert.AreEqual(7f, value);
            Assert.AreEqual(1, random.CallCount);
            Assert.AreEqual(7, random.MinValues[0]);
            Assert.AreEqual(7, random.MaxValues[0]);
            StringAssert.Contains("[7, 7] => 7", report.Steps[0].SourceLabel);
        }

        [Test]
        public void TryEvaluate_WithInvalidRandomRange_FailsWithoutConsumingSample()
        {
            var formula = CreateFormula(0f,
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.RandomInteger(8, 7)));
            var random = new RecordingRandomSource(7);

            var success = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                random,
                out var value,
                out var report);

            Assert.IsFalse(success);
            Assert.AreEqual(0f, value);
            Assert.AreEqual(0, random.CallCount);
            Assert.That(report.Diagnostics.Any(d => d.Code == FormulaDiagnosticCode.InvalidRandomRange), Is.True);
        }

        [Test]
        public void TryEvaluate_WithIntMaxRandomBoundary_FailsWithoutConsumingSample()
        {
            var formula = CreateFormula(0f,
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.RandomInteger(0, int.MaxValue)));
            var random = new RecordingRandomSource(0);

            var success = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                random,
                out _,
                out var report);

            Assert.IsFalse(success);
            Assert.AreEqual(0, random.CallCount);
            Assert.That(report.Diagnostics.Any(d => d.Code == FormulaDiagnosticCode.InvalidRandomRange), Is.True);
        }

        [Test]
        public void FormulaValueSourceType_AppendsRandomIntegerWithoutChangingExistingValues()
        {
            Assert.AreEqual(0, (int)FormulaValueSourceType.Constant);
            Assert.AreEqual(1, (int)FormulaValueSourceType.Provider);
            Assert.AreEqual(2, (int)FormulaValueSourceType.NestedFormula);
            Assert.AreEqual(3, (int)FormulaValueSourceType.RandomInteger);
        }

        [Test]
        public void TryEvaluate_WithNestedRandomSources_SharesSourceInEvaluationOrder()
        {
            var nested = CreateFormula(0f,
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.RandomInteger(1, 10)));
            var formula = CreateFormula(0f,
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Nested(nested)),
                FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.RandomInteger(20, 30)));
            var random = new RecordingRandomSource(4, 25);

            var success = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                random,
                out var value,
                out var report);

            Assert.IsTrue(success, string.Join("\n", report.Diagnostics));
            Assert.AreEqual(29f, value);
            CollectionAssert.AreEqual(new[] { 1, 20 }, random.MinValues);
            CollectionAssert.AreEqual(new[] { 10, 30 }, random.MaxValues);
        }

        [Test]
        public void SystemFormulaRandomSource_MatchesSystemRandomNextSequence()
        {
            var expectedRandom = new System.Random(12345);
            var actualRandom = new System.Random(12345);
            var source = new SystemFormulaRandomSource(actualRandom);

            Assert.AreEqual(expectedRandom.Next(10, 21), source.NextIntInclusive(10, 20));
            expectedRandom.Next(7, 8);
            Assert.AreEqual(7, source.NextIntInclusive(7, 7));
            Assert.AreEqual(expectedRandom.Next(), actualRandom.Next());
        }

        [TestCase(1.2f, FormulaRoundingMode.Floor, 1)]
        [TestCase(-1.2f, FormulaRoundingMode.Floor, -2)]
        [TestCase(1.2f, FormulaRoundingMode.Ceil, 2)]
        [TestCase(-1.2f, FormulaRoundingMode.Ceil, -1)]
        [TestCase(1.5f, FormulaRoundingMode.Round, 2)]
        [TestCase(-1.5f, FormulaRoundingMode.Round, -2)]
        [TestCase(1.9f, FormulaRoundingMode.Truncate, 1)]
        [TestCase(-1.9f, FormulaRoundingMode.Truncate, -1)]
        public void ToInt_WithMode_ReturnsExpectedValue(float input, FormulaRoundingMode mode, int expected)
        {
            Assert.AreEqual(expected, FormulaValueUtility.ToInt(input, mode));
        }

        [Test]
        public void EvaluateToInt_WithNullFormula_ReturnsFallbackAndMarksReportFailed()
        {
            var value = FormulaValueUtility.EvaluateToInt(
                null,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                FormulaRoundingMode.Floor,
                7,
                out var report);

            Assert.AreEqual(7, value);
            Assert.AreEqual(7, report.Result);
            Assert.IsFalse(report.Succeeded);
            Assert.IsTrue(report.HasErrors);
            Assert.That(report.Diagnostics[0].Code, Is.EqualTo(FormulaDiagnosticCode.NullFormula));
        }

        private TestFormula CreateFormula(float initialValue, params FormulaStep[] steps)
        {
            var formula = ScriptableObject.CreateInstance<TestFormula>();
            createdObjects.Add(formula);
            formula.Initialize(initialValue, steps);
            return formula;
        }

        private sealed class TestFormula : ScriptableObject, IFormulaDefinition
        {
            private FormulaStep[] steps;

            public string FormulaName => string.IsNullOrEmpty(name) ? "<test formula>" : name;
            public float InitialValue { get; private set; }
            public int StepCount => steps?.Length ?? 0;

            public void Initialize(float initialValue, FormulaStep[] formulaSteps)
            {
                InitialValue = initialValue;
                steps = formulaSteps ?? new FormulaStep[0];
            }

            public bool TryGetStep(int index, out FormulaStep step)
            {
                if (steps == null || index < 0 || index >= steps.Length)
                {
                    step = null;
                    return false;
                }

                step = steps[index];
                return true;
            }
        }

        private sealed class RecordingRandomSource : IFormulaRandomSource
        {
            private readonly Queue<int> values;

            public RecordingRandomSource(params int[] values)
            {
                this.values = new Queue<int>(values);
            }

            public int CallCount { get; private set; }
            public List<int> MinValues { get; } = new();
            public List<int> MaxValues { get; } = new();

            public int NextIntInclusive(int minInclusive, int maxInclusive)
            {
                CallCount++;
                MinValues.Add(minInclusive);
                MaxValues.Add(maxInclusive);
                return values.Dequeue();
            }
        }
    }
}
