using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Editor.Dashboard;
using ZeroEngine.EditorUI;

namespace ZeroEngine.Dashboard.Tests.Editor
{
    public sealed class DashboardActionRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            CountingProvider.Reset();
            TransitionProvider.Reset();
        }

        [Test]
        public void Build_DiscoversProviderWithoutConstructingIt()
        {
            DashboardCatalog catalog = BuildCatalog("tests.counting", "run");

            DashboardActionRegistry registry = DashboardActionRegistry.Build(catalog, new[] { typeof(CountingProvider) });

            Assert.AreEqual(0, CountingProvider.ConstructorCount);
            Assert.AreEqual(0, registry.Diagnostics.Count);
        }

        [Test]
        public void GetState_LazilyCreatesAndCachesProviderAction()
        {
            DashboardCatalog catalog = BuildCatalog("tests.counting", "run");
            DashboardEntry entry = catalog.Modules.Single().Entries.Single();
            DashboardActionRegistry registry = DashboardActionRegistry.Build(catalog, new[] { typeof(CountingProvider) });

            Assert.IsTrue(registry.TryGetState(entry, out EditorToolActionState first, out DashboardDiagnostic firstDiagnostic));
            Assert.IsTrue(registry.TryGetState(entry, out EditorToolActionState second, out DashboardDiagnostic secondDiagnostic));

            Assert.IsNull(firstDiagnostic);
            Assert.IsNull(secondDiagnostic);
            Assert.IsTrue(first.Enabled);
            Assert.IsTrue(second.Enabled);
            Assert.AreEqual(1, CountingProvider.ConstructorCount);
            Assert.AreEqual(1, CountingProvider.CreateCount);
        }

        [Test]
        public void Build_DuplicateProviderId_IsolatesBoundAction()
        {
            DashboardCatalog catalog = BuildCatalog("tests.duplicate", "run");

            DashboardActionRegistry registry = DashboardActionRegistry.Build(
                catalog,
                new[] { typeof(DuplicateProviderA), typeof(DuplicateProviderB) });

            Assert.That(registry.Diagnostics.Single().Code, Is.EqualTo("action-provider-duplicate"));
        }

        [Test]
        public void Execute_ProjectWrite_RechecksStateAfterConfirmation()
        {
            DashboardCatalog catalog = BuildCatalog(
                "tests.transition",
                "run",
                safety: "project-write",
                visibility: "advanced",
                confirmation: "确认写入项目？");
            DashboardEntry entry = catalog.Modules.Single().Entries.Single();
            DashboardActionRegistry registry = DashboardActionRegistry.Build(catalog, new[] { typeof(TransitionProvider) });
            var host = new FakeExecutionHost { ConfirmResult = true };
            EditorWindow owner = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                DashboardExecutionResult result = DashboardEntryExecutor.Execute(entry, registry, owner, host);

                Assert.AreEqual(DashboardExecutionStatus.Unavailable, result.Status);
                Assert.AreEqual("状态已变化。", result.Message);
                Assert.AreEqual(1, host.ConfirmCount);
                Assert.AreEqual(2, TransitionProvider.StateCount);
                Assert.AreEqual(0, TransitionProvider.ExecuteCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void Execute_SucceededProviderResult_IsReturned()
        {
            DashboardCatalog catalog = BuildCatalog("tests.counting", "run");
            DashboardEntry entry = catalog.Modules.Single().Entries.Single();
            DashboardActionRegistry registry = DashboardActionRegistry.Build(catalog, new[] { typeof(CountingProvider) });
            var host = new FakeExecutionHost();
            EditorWindow owner = ScriptableObject.CreateInstance<EditorWindow>();
            try
            {
                DashboardExecutionResult result = DashboardEntryExecutor.Execute(entry, registry, owner, host);

                Assert.AreEqual(DashboardExecutionStatus.Succeeded, result.Status);
                Assert.AreEqual("动作完成。", result.Message);
                Assert.AreEqual(1, CountingProvider.ExecuteCount);
                Assert.AreEqual(0, host.ExecuteMenuCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static DashboardCatalog BuildCatalog(
            string providerId,
            string actionId,
            string safety = "navigation",
            string visibility = "primary",
            string confirmation = null)
        {
            string confirmationField = confirmation == null
                ? string.Empty
                : ",\"confirmation\":\"" + confirmation + "\"";
            string json = "{" +
                          "\"schemaVersion\":2," +
                          "\"moduleId\":\"project.actions\"," +
                          "\"displayName\":\"动作\"," +
                          "\"description\":\"测试动作。\"," +
                          "\"scope\":\"universal\"," +
                          "\"entries\":[{" +
                          "\"id\":\"run\"," +
                          "\"displayName\":\"运行\"," +
                          "\"description\":\"运行测试动作。\"," +
                          "\"category\":\"diagnostics\"," +
                          "\"kind\":\"command\"," +
                          "\"order\":0," +
                          "\"safety\":\"" + safety + "\"," +
                          "\"availability\":\"always\"," +
                          "\"visibility\":\"" + visibility + "\"," +
                          "\"executionKind\":\"provider\"," +
                          "\"providerId\":\"" + providerId + "\"," +
                          "\"actionId\":\"" + actionId + "\"," +
                          "\"replaces\":[]" + confirmationField + "}]}";
            var source = new DashboardDescriptorSource(
                DashboardSourceKind.Project,
                "descriptor.json",
                Path.GetTempPath(),
                string.Empty,
                string.Empty,
                json,
                projectRootPath: Path.GetTempPath());
            DashboardCatalog catalog = DashboardCatalogBuilder.Build(
                new[] { source },
                Array.Empty<DashboardInstalledPackage>());
            Assert.That(catalog.Diagnostics.Where(item => item.Severity == DashboardDiagnosticSeverity.Error), Is.Empty);
            return catalog;
        }

        [EditorToolActionProvider("tests.counting")]
        public sealed class CountingProvider : IEditorToolActionProvider
        {
            internal static int ConstructorCount { get; private set; }
            internal static int CreateCount { get; private set; }
            internal static int ExecuteCount { get; private set; }

            public CountingProvider()
            {
                ConstructorCount++;
            }

            public IEditorToolAction CreateAction(string actionId)
            {
                CreateCount++;
                return actionId == "run" ? new CountingAction() : null;
            }

            internal static void Reset()
            {
                ConstructorCount = 0;
                CreateCount = 0;
                ExecuteCount = 0;
            }

            private sealed class CountingAction : IEditorToolAction
            {
                public EditorToolActionState GetState()
                {
                    return new EditorToolActionState(true);
                }

                public EditorToolActionResult Execute(EditorToolActionContext context)
                {
                    ExecuteCount++;
                    return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "动作完成。");
                }
            }
        }

        [EditorToolActionProvider("tests.duplicate")]
        public sealed class DuplicateProviderA : IEditorToolActionProvider
        {
            public IEditorToolAction CreateAction(string actionId) => null;
        }

        [EditorToolActionProvider("tests.duplicate")]
        public sealed class DuplicateProviderB : IEditorToolActionProvider
        {
            public IEditorToolAction CreateAction(string actionId) => null;
        }

        [EditorToolActionProvider("tests.transition")]
        public sealed class TransitionProvider : IEditorToolActionProvider
        {
            internal static int StateCount { get; private set; }
            internal static int ExecuteCount { get; private set; }

            public IEditorToolAction CreateAction(string actionId)
            {
                return actionId == "run" ? new TransitionAction() : null;
            }

            internal static void Reset()
            {
                StateCount = 0;
                ExecuteCount = 0;
            }

            private sealed class TransitionAction : IEditorToolAction
            {
                public EditorToolActionState GetState()
                {
                    StateCount++;
                    return StateCount == 1
                        ? new EditorToolActionState(true)
                        : new EditorToolActionState(false, disabledReason: "状态已变化。");
                }

                public EditorToolActionResult Execute(EditorToolActionContext context)
                {
                    ExecuteCount++;
                    return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "不应执行。");
                }
            }
        }

        private sealed class FakeExecutionHost : IDashboardExecutionHost
        {
            internal bool ConfirmResult { get; set; }
            internal int ConfirmCount { get; private set; }
            internal int ExecuteMenuCount { get; private set; }

            bool IDashboardExecutionHost.IsPlaying => false;

            public bool Confirm(DashboardEntry entry)
            {
                ConfirmCount++;
                return ConfirmResult;
            }

            public bool ExecuteMenuItem(string menuPath)
            {
                ExecuteMenuCount++;
                return true;
            }

            public void LogException(Exception exception)
            {
            }
        }
    }
}
