using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.BuffSystem;
using ZeroEngine.Data.Editor;
using ZeroEngine.StatSystem.Formula;
using Object = UnityEngine.Object;

namespace ZeroEngine.Data.Editor.Tests
{
    public sealed class DataConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsBrokenBuffAndFormulaConfig()
        {
            var buffA = ScriptableObject.CreateInstance<BuffData>();
            var buffB = ScriptableObject.CreateInstance<BuffData>();
            var formula = ScriptableObject.CreateInstance<MathFormula>();

            try
            {
                buffA.name = "BuffA";
                buffA.BuffId = "poison";
                buffA.Duration = -1f;
                buffA.MaxStacks = 0;
                buffA.TickInterval = 0f;
                buffA.StatModifiers.Add(null);

                buffB.name = "BuffB";
                buffB.BuffId = "poison";

                formula.name = "Formula";
                formula.Steps.Add(new OperationStep
                {
                    Operation = MathOperationType.Divide,
                    ProviderType = ValueProviderType.Constant,
                    ConstantValue = 0f
                });

                var issues = DataConfigValidator.Validate(new[] { buffA, buffB }, new[] { formula });

                AssertError(issues, "Buff ID 'poison' is duplicated.");
                AssertError(issues, "Buff duration cannot be negative.");
                AssertError(issues, "Buff max stacks must be positive.");
                AssertError(issues, "Buff tick interval must be positive.");
                AssertError(issues, "Buff stat modifier is empty.");
                AssertError(issues, "Formula cannot divide by a constant zero.");
            }
            finally
            {
                Object.DestroyImmediate(buffA);
                Object.DestroyImmediate(buffB);
                Object.DestroyImmediate(formula);
            }
        }

        private static void AssertError(IReadOnlyList<DataValidationIssue> issues, string expectedMessage)
        {
            Assert.That(
                issues.Any(issue => issue.Severity == DataValidationSeverity.Error && issue.Message == expectedMessage),
                Is.True,
                $"Expected validation error '{expectedMessage}', got:\n{string.Join("\n", issues.Select(issue => issue.ToString()))}");
        }
    }
}
