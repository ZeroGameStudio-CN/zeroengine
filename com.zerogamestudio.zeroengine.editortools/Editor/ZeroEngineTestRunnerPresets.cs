using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ZeroEngine.EditorTools
{
    public static class ZeroEngineTestRunnerPresets
    {
        private const string ProjectId = "ZERO_ENGINE";
        private const string ProjectTitle = "ZeroEngine Editor Tools";
        private const string TestGroup = "ZeroEngine Test Runner";
        private const string TestGroupDisplayName = "测试运行器";

        private static readonly string[] SourceGroupNames =
        {
            ".*(SourceTests|SourceGuardTests|PackageBoundaryTests|AssemblySourceTests|DrawerSourceTests|WindowSourceTests).*",
            ".*(DataAuthoringWindow_Source|RuntimeAssembly_DoesNot|EditorAssembly_DoesNot|DoesNotReference|RuntimeSource_HasNo).*"
        };

        public static readonly string[] FastEditModeAssemblies =
        {
            "ZeroEngine.Cinematic.Tests.Editor",
            "ZeroEngine.Economy.Tests.Editor",
            "ZeroEngine.Events.Tests.Editor",
            "ZeroEngine.Gameplay.Tests.Editor",
            "ZeroEngine.Input.Tests.Editor",
            "ZeroEngine.Narrative.Tests.Editor",
            "ZeroEngine.RPG.Tests.Editor",
            "ZeroEngine.Tests.Editor",
            "ZeroEngine.World.Tests.Editor"
        };

        public static readonly string[] SlowEditModeAssemblies =
        {
            "ZeroEngine.Combat.Editor.Tests",
            "ZeroEngine.DataToolkit.Editor.Tests",
            "ZeroEngine.EditorTools.Editor.Tests",
            "ZeroEngine.TCE.Tests.Editor",
            "ZeroEngine.UI.Tests.Editor"
        };

        public static readonly string[] FullEditModeAssemblies = FastEditModeAssemblies
            .Concat(SlowEditModeAssemblies)
            .ToArray();

        public static EditorToolTestRunnerTask SourceEditMode()
        {
            return new EditorToolTestRunnerTask(
                "zeroengine.source-editmode",
                "Source EditMode",
                EditorToolTestMode.EditMode,
                TestGroup,
                10,
                "运行源码、边界和引用守卫测试。",
                TestGroupDisplayName,
                groupNames: SourceGroupNames);
        }

        public static EditorToolTestRunnerTask FastEditMode()
        {
            return new EditorToolTestRunnerTask(
                "zeroengine.fast-editmode",
                "Fast EditMode",
                EditorToolTestMode.EditMode,
                TestGroup,
                20,
                "运行日常快速 EditMode gate，不包含显式慢治理程序集。",
                TestGroupDisplayName,
                assemblyNames: FastEditModeAssemblies);
        }

        public static EditorToolTestRunnerTask SlowEditMode()
        {
            return new EditorToolTestRunnerTask(
                "zeroengine.slow-editmode",
                "Slow EditMode",
                EditorToolTestMode.EditMode,
                TestGroup,
                30,
                "运行显式慢治理、源码守卫和重编辑器程序集。",
                TestGroupDisplayName,
                assemblyNames: SlowEditModeAssemblies);
        }

        public static EditorToolTestRunnerTask FullAuditEditMode()
        {
            return new EditorToolTestRunnerTask(
                "zeroengine.full-audit-editmode",
                "Full Audit EditMode",
                EditorToolTestMode.EditMode,
                TestGroup,
                40,
                "完整 EditMode 审计画像，keep-going，不作为 fail-fast 质量门。",
                TestGroupDisplayName,
                assemblyNames: FullEditModeAssemblies);
        }

        public static IReadOnlyList<ITestRunnerTask> QualityGateEditModeLayers()
        {
            return new ITestRunnerTask[]
            {
                SourceEditMode(),
                FastEditMode(),
                SlowEditMode()
            };
        }

        public static IReadOnlyList<ITestRunnerTask> ToolWindowTasks()
        {
            return new ITestRunnerTask[]
            {
                SourceEditMode(),
                FastEditMode(),
                SlowEditMode(),
                FullAuditEditMode()
            };
        }

        [EditorToolProjectProvider]
        public static EditorToolProjectProfile CreateProfile()
        {
            return new EditorToolProjectProfile(
                ProjectId,
                ProjectTitle,
                "ZGS/Editor Tools",
                "ZeroEngine 分层测试入口，避免日常验证默认 Run All。",
                testRunnerTasks: ToolWindowTasks());
        }
    }

    public static class ZeroEngineTestRunnerMenus
    {
        [MenuItem("ZGS/Test Runner/Source EditMode")]
        public static void RunSourceEditMode()
        {
            EditorToolTestRunner.Execute(ZeroEngineTestRunnerPresets.SourceEditMode());
        }

        [MenuItem("ZGS/Test Runner/Fast EditMode")]
        public static void RunFastEditMode()
        {
            EditorToolTestRunner.Execute(ZeroEngineTestRunnerPresets.FastEditMode());
        }

        [MenuItem("ZGS/Test Runner/Slow EditMode")]
        public static void RunSlowEditMode()
        {
            EditorToolTestRunner.Execute(ZeroEngineTestRunnerPresets.SlowEditMode());
        }

        [MenuItem("ZGS/Test Runner/Full Audit EditMode (Keep Going)")]
        public static void RunFullAuditEditMode()
        {
            EditorToolTestRunner.Execute(ZeroEngineTestRunnerPresets.FullAuditEditMode());
        }

        [MenuItem("ZGS/Test Runner/Full Quality Gate EditMode (Fail Fast)")]
        public static void RunFullQualityGateEditMode()
        {
            EditorToolTestRunnerSequence.ExecuteFailFast(
                "Full Quality Gate EditMode",
                ZeroEngineTestRunnerPresets.QualityGateEditModeLayers());
        }
    }

    public sealed class EditorToolTestRunnerSequence : ICallbacks
    {
        private static EditorToolTestRunnerSequence _activeSequence;

        private readonly Queue<ITestRunnerTask> _tasks;
        private readonly string _displayName;
        private readonly TestRunnerApi _api;
        private ITestRunnerTask _currentTask;
        private double _nextStartTime;

        private EditorToolTestRunnerSequence(string displayName, IEnumerable<ITestRunnerTask> tasks)
        {
            _displayName = string.IsNullOrWhiteSpace(displayName) ? "Test gate" : displayName;
            _tasks = new Queue<ITestRunnerTask>(tasks);
            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            TestRunnerApi.RegisterTestCallback(this);
        }

        public static EditorToolExecutionResult ExecuteFailFast(string displayName, IEnumerable<ITestRunnerTask> tasks)
        {
            if (_activeSequence != null)
            {
                return EditorToolExecutionResult.Error("A fail-fast test gate is already running.");
            }

            var taskList = tasks?.Where(task => task != null).ToArray() ?? Array.Empty<ITestRunnerTask>();
            if (taskList.Length == 0)
            {
                return EditorToolExecutionResult.Error("Fail-fast test gate has no layers.");
            }

            _activeSequence = new EditorToolTestRunnerSequence(displayName, taskList);
            _activeSequence.StartNext();
            return EditorToolExecutionResult.Success(
                $"Started '{displayName}'.",
                taskList.Select(task => task.DisplayName));
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            var failed = result == null || result.FailCount > 0;
            Debug.Log(
                $"Finished '{_currentTask?.DisplayName}' for '{_displayName}'. " +
                $"Result: {result?.ResultState ?? "missing"}, failures: {result?.FailCount ?? 0}.");

            if (failed)
            {
                Debug.LogError(
                    $"Stopped '{_displayName}' after '{_currentTask?.DisplayName}' failed. " +
                    $"Result: {result?.ResultState ?? "missing"}, failures: {result?.FailCount ?? 0}.");
                Finish();
                return;
            }

            if (_tasks.Count == 0)
            {
                Debug.Log($"Finished '{_displayName}' with all layers passing.");
                Finish();
                return;
            }

            _nextStartTime = EditorApplication.timeSinceStartup + 0.25d;
            EditorApplication.update += StartNextWhenReady;
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
        }

        private void StartNext()
        {
            if (_tasks.Count == 0)
            {
                Finish();
                return;
            }

            _currentTask = _tasks.Dequeue();
            var jobId = _api.Execute(new ExecutionSettings(_currentTask.CreateFilter()));
            Debug.Log($"Started '{_currentTask.DisplayName}' for '{_displayName}' ({jobId}).");
        }

        private void StartNextWhenReady()
        {
            if (EditorApplication.timeSinceStartup < _nextStartTime)
            {
                return;
            }

            EditorApplication.update -= StartNextWhenReady;
            StartNext();
        }

        private void Finish()
        {
            EditorApplication.update -= StartNextWhenReady;
            TestRunnerApi.UnregisterTestCallback(this);
            UnityEngine.Object.DestroyImmediate(_api);
            _activeSequence = null;
        }
    }
}
