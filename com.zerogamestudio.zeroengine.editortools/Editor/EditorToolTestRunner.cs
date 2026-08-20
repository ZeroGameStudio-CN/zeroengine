using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ZeroEngine.EditorTools
{
    public static class EditorToolTestRunner
    {
        public static EditorToolExecutionResult Execute(ITestRunnerTask task)
        {
            if (task == null)
            {
                return EditorToolExecutionResult.Error("Test runner task is null.");
            }

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var jobId = api.Execute(new ExecutionSettings(task.CreateFilter()));
            Debug.Log($"Started editor tool test run '{task.DisplayName}' ({jobId}).");
            return EditorToolExecutionResult.Success($"Started '{task.DisplayName}' ({jobId}).");
        }
    }
}
