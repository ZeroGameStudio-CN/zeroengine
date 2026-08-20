using System;

namespace ZeroEngine.World.WorldGraph
{
    public readonly struct WorldGraphHandoffResult
    {
        public WorldGraphHandoffResult(
            WorldGraphRuntimeSessionStatus status,
            WorldGraphConnectionDefinition connection = null,
            WorldGraphRuntimeSessionResult targetLoadResult = default,
            WorldGraphRuntimeSessionResult sourceUnloadResult = default,
            Exception exception = null)
        {
            Status = status;
            Connection = connection;
            TargetLoadResult = targetLoadResult;
            SourceUnloadResult = sourceUnloadResult;
            Exception = exception;
        }

        public WorldGraphRuntimeSessionStatus Status { get; }
        public WorldGraphConnectionDefinition Connection { get; }
        public WorldGraphRuntimeSessionResult TargetLoadResult { get; }
        public WorldGraphRuntimeSessionResult SourceUnloadResult { get; }
        public Exception Exception { get; }
        public bool Succeeded => Status == WorldGraphRuntimeSessionStatus.HandoffCompleted;
    }
}
