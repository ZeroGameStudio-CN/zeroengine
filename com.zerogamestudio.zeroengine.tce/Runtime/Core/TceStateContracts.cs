using System;

namespace ZeroEngine.TCE
{
    /// <summary>Runtime state only. A host adapter owns scope, stable keys, snapshots and persistence.</summary>
    public interface ITceStateStore
    {
        long ReadNumber(string key, long fallback = 0);
        string ReadText(string key, string fallback = null);
        void WriteNumber(string key, long value);
        void WriteText(string key, string value);
    }

    /// <summary>Optional install source or actor capability; TCE has no dependency on a save backend.</summary>
    public interface ITceStateSource
    {
        ITceStateStore State { get; }
    }
}
