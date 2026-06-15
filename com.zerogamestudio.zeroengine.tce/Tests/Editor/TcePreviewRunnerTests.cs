using NUnit.Framework;
using ZeroEngine.TCE.Editor;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TcePreviewRunnerTests
    {
        [Test]
        public void Run_BlocksInvalidGraph()
        {
            var asset = UnityEngine.ScriptableObject.CreateInstance<TceGraphAsset>();

            TcePreviewResult result = TcePreviewRunner.Run(asset, TcePreviewInput.Default);

            Assert.IsFalse(result.Executed);
            Assert.That(result.Issues.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Run_ExecutesValidInstallGraph()
        {
            var asset = UnityEngine.ScriptableObject.CreateInstance<TceGraphAsset>();
            asset.Graph.AddTrigger(new OnInstallTriggerData());
            asset.Graph.AddCondition(new NumericSourceConditionData { RequiredValue = 1f });
            asset.Graph.AddEffect(new DebugLogEffectData { Message = "preview" });

            TcePreviewResult result = TcePreviewRunner.Run(asset, new TcePreviewInput(2f));

            Assert.IsTrue(result.Executed);
            Assert.AreEqual(1, result.ExecutionCount);
            Assert.That(result.Logs, Does.Contain("preview"));
        }

        [Test]
        public void Run_RestoresPreviousLogHandler()
        {
            var asset = UnityEngine.ScriptableObject.CreateInstance<TceGraphAsset>();
            asset.Graph.AddTrigger(new OnInstallTriggerData());
            asset.Graph.AddEffect(new DebugLogEffectData { Message = "preview" });

            System.Action<string> previous = _ => { };
            TceLog.Handler = previous;

            try
            {
                TcePreviewRunner.Run(asset, TcePreviewInput.Default);

                Assert.AreSame(previous, TceLog.Handler);
            }
            finally
            {
                TceLog.Handler = UnityEngine.Debug.Log;
            }
        }

        [Test]
        public void Summary_ExecutedPreview_IncludesExecutionCountAndLogs()
        {
            var result = new TcePreviewResult(true, 2, new[] { "a", "b" }, new TceValidationIssue[0]);

            StringAssert.Contains("executed=True", result.Summary);
            StringAssert.Contains("count=2", result.Summary);
            StringAssert.Contains("logs=2", result.Summary);
        }

        [Test]
        public void BuildSummary_KnownComponent_UsesCatalogMetadata()
        {
            string summary = TceNodeInspector.BuildSummary(new DebugLogEffectData());

            StringAssert.Contains("Debug Log", summary);
            StringAssert.Contains("Effect", summary);
        }

        [Test]
        public void BuildSummary_NullComponent_AsksForSelection()
        {
            Assert.AreEqual("Select a component.", TceNodeInspector.BuildSummary(null));
        }
    }
}
