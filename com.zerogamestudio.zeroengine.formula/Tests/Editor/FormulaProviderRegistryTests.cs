using NUnit.Framework;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    public sealed class FormulaProviderRegistryTests
    {
        [Test]
        public void TryEvaluate_WithRegisteredProvider_UsesProviderValue()
        {
            var formula = new FormulaRuntimeDefinition(
                "provider-test",
                1f,
                new[]
                {
                    FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Provider("test.value")),
                });

            var registry = new FormulaProviderRegistry();
            registry.Register(new TestProvider("test.value", 4f));

            var success = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                registry,
                out var value,
                out var report);

            Assert.IsTrue(success);
            Assert.AreEqual(5f, value, 0.0001f);
            Assert.IsFalse(report.HasErrors);
        }

        [Test]
        public void TryEvaluate_WithMissingProvider_FailsWithInvalidProviderDiagnostic()
        {
            var formula = new FormulaRuntimeDefinition(
                "missing-provider-test",
                1f,
                new[]
                {
                    FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Provider("missing.value")),
                });

            var success = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                out var value,
                out var report);

            Assert.IsFalse(success);
            Assert.AreEqual(0f, value, 0.0001f);
            Assert.IsTrue(report.HasErrors);
            Assert.That(report.Diagnostics[0].Code, Is.EqualTo(FormulaDiagnosticCode.InvalidProvider));
        }

        private sealed class TestProvider : IFormulaValueProvider
        {
            private readonly float value;

            public TestProvider(string id, float value)
            {
                Id = id;
                this.value = value;
            }

            public string Id { get; }

            public bool TryGetValue(
                FormulaProviderRequest request,
                IFormulaEvaluationContext context,
                out float result,
                FormulaDiagnosticSink diagnostics)
            {
                result = value;
                return true;
            }
        }
    }
}
