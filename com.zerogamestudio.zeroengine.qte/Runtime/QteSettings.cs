using System;

namespace ZeroEngine.Qte
{
    public enum QteMode { TimingWindow = 0, HoldRelease = 1 }
    public enum QteState { Ready, Active, Succeeded, Failed, Cancelled }

    // Values describe one resolved challenge. A game resolves difficulty and randomness before Begin.
    [Serializable]
    public sealed class QteSettings
    {
        public float Duration = 2.2f;
        public float TargetCenter = .5f;
        public float TargetWidth = .2f;
        public float EngageTimeout = 3;
        public float CalmDuration = 1;
        public float TelegraphDuration = .35f;
        public float SurgeDuration = .55f;
        public float ForceMultiplier = 1;
        public float ProgressPerSecond = .46f;
        public float InitialLoad = .15f;
        public float CalmLoad = .18f;
        public float TelegraphLoad = .4f;
        public float SurgeLoad = 1.3f;
        public float ReleaseRate = .9f;
        public float SurgeProgressScale = .25f;
        public float ReleasedProgressLoss = .025f;
        public float ReleaseWarning = .82f;
        public float OverloadThreshold = .98f;
        public float OverloadGrace = .2f;

        internal QteSettings Snapshot()
        {
            var value = (QteSettings)MemberwiseClone();
            if (!Finite(value.Duration) || value.Duration <= 0 || !Finite(value.TargetCenter) ||
                !Finite(value.TargetWidth) || !Finite(value.InitialLoad) || value.TargetWidth < 0 || value.TargetWidth > 1 ||
                value.TargetCenter < 0 || value.TargetCenter > 1)
                throw new ArgumentException("Use finite duration and normalized timing target values.");
            return value;
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
