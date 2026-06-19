using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.UI;
using ZeroEngine.UI.Editor;
using ZeroEngine.UI.Toast;
using Object = UnityEngine.Object;

namespace ZeroEngine.UI.Tests.Editor
{
    public sealed class UIConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsBrokenUIViewAndToastConfig()
        {
            var database = ScriptableObject.CreateInstance<UIViewDatabase>();
            var toast = ScriptableObject.CreateInstance<ToastSettings>();

            try
            {
                database.views.Add(new UIViewEntry { viewName = "Inventory", animationDuration = 0f });
                database.views.Add(new UIViewEntry { viewName = "Inventory", animationDuration = 0.2f });

                typeof(ToastSettings)
                    .GetField("styles", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(toast, new List<ToastStyle>
                    {
                        new ToastStyle { Severity = ToastSeverity.Info, Duration = 0f },
                        new ToastStyle { Severity = ToastSeverity.Info, Duration = 1f }
                    });

                var issues = UIConfigValidator.Validate(new[] { database }, new[] { toast });

                AssertError(issues, "Duplicate UI view name 'Inventory'.");
                AssertError(issues, "UI view animation duration must be positive.");
                AssertError(issues, "Duplicate toast style severity 'Info'.");
                AssertError(issues, "Toast style duration must be positive.");
            }
            finally
            {
                Object.DestroyImmediate(database);
                Object.DestroyImmediate(toast);
            }
        }

        private static void AssertError(IReadOnlyList<UIValidationIssue> issues, string expectedMessage)
        {
            Assert.That(issues.Any(issue => issue.Severity == UIValidationSeverity.Error && issue.Message == expectedMessage), Is.True);
        }
    }
}
