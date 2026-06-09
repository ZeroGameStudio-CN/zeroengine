using System.Collections.Generic;

namespace ZeroEngine.TCE.Editor
{
    public readonly struct TcePreviewInput
    {
        public static readonly TcePreviewInput Default = new(1f);

        public TcePreviewInput(float numericValue)
        {
            NumericValue = numericValue;
        }

        public float NumericValue { get; }
    }

    public sealed class TcePreviewResult
    {
        public TcePreviewResult(bool executed, int executionCount, IReadOnlyList<string> logs, IReadOnlyList<TceValidationIssue> issues)
        {
            Executed = executed;
            ExecutionCount = executionCount;
            Logs = logs;
            Issues = issues;
        }

        public bool Executed { get; }
        public int ExecutionCount { get; }
        public IReadOnlyList<string> Logs { get; }
        public IReadOnlyList<TceValidationIssue> Issues { get; }
    }

    public static class TcePreviewRunner
    {
        public static TcePreviewResult Run(TceGraphAsset asset, TcePreviewInput input)
        {
            IReadOnlyList<TceValidationIssue> issues = TceGraphAssetValidator.Validate(asset);
            if (issues.Count > 0)
                return new TcePreviewResult(false, 0, new List<string>(), issues);

            var logs = new List<string>();
            int executionCount = 0;
            System.Action<string> previousLogHandler = TceLog.Handler;
            var runtime = new TceRuntime();

            TceLog.Handler = logs.Add;
            try
            {
                runtime.Executed += (_, _) => executionCount++;
                runtime.Install(new NumericValueSource(input.NumericValue), new PreviewActor(), asset.Graph);
            }
            finally
            {
                runtime.Uninstall();
                TceLog.Handler = previousLogHandler;
            }

            return new TcePreviewResult(executionCount > 0, executionCount, logs, issues);
        }

        private sealed class PreviewActor : ITceActor
        {
            public bool IsAlive => true;
            public float DomainTime => 0f;
            public object NativeObject => this;
        }
    }
}
