namespace ZeroEngine.Qte
{
    // An immutable display value: views can read it without owning or advancing a session.
    public readonly struct QteSnapshot
    {
        public readonly QteState State;
        public readonly QteMode Mode;
        public readonly float Age, Progress, Load, Marker, Remaining, TargetCenter, TargetWidth;
        public readonly bool Engaged, Preparing, Surging, ShouldRelease;
        internal QteSnapshot(QteSession value)
        {
            State = value.State; Mode = value.Mode; Age = value.Age; Progress = value.Progress;
            Load = value.Load; Marker = value.Marker; Remaining = value.Remaining;
            TargetCenter = value.TargetCenter; TargetWidth = value.TargetWidth;
            Engaged = value.Engaged; Preparing = value.Preparing; Surging = value.Surging; ShouldRelease = value.ShouldRelease;
        }
    }
}
