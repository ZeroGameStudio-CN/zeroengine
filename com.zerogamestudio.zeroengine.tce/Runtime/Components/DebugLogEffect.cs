using System;
using UnityEngine;

namespace ZeroEngine.TCE
{
    public static class TceLog
    {
        public static Action<string> Handler = Debug.Log;

        public static void Log(string message)
        {
            Handler?.Invoke(message);
        }
    }

    public sealed class DebugLogEffect : TceEffect<DebugLogEffectData>
    {
        public override void Execute(ITceActor target, object source)
        {
            TceLog.Log(Data.Message);
        }
    }

    [Serializable]
    [TceComponentDoc(TceComponentDocCategory.Effect, "zeroengine.tce.effect.debug_log", "Debug Log", "Writes a message through the TCE log hook.", "Use this effect in tests, examples, and adapter smoke checks. Production gameplay should prefer project-specific effects.")]
    public sealed class DebugLogEffectData : TceEffectData<DebugLogEffect>
    {
        [TceFieldDoc("Log message emitted when the effect runs.")]
        public string Message = string.Empty;
    }
}
