namespace ZeroEngine.StatSystem.Core
{
    /// <summary>
    /// Per-stat invalidation and calculation diagnostics. Hosts may retain their
    /// existing cached fields. Mutable inputs must explicitly invalidate; this
    /// token never scans a collection or assumes publicly mutable inputs are frozen.
    /// </summary>
    public struct StatCalculationVersion
    {
        public uint InputVersion { get; private set; }
        public uint CalculatedVersion { get; private set; }
        public ulong CalculationCount { get; private set; }
        public void Invalidate() { unchecked { InputVersion++; } }
        public void RecordCalculation()
        {
            CalculatedVersion = InputVersion;
            unchecked { CalculationCount++; }
        }
    }
}
