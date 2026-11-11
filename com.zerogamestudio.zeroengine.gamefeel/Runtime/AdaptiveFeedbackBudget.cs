using System;

namespace ZeroEngine.GameFeel
{
    public enum AdaptiveFeedbackReason { None, Crowding, FrameTime, Recovering, UnrelatedSlowdown }

    /// <summary>Caller-clocked optional-output budget. Never changes the player's ceiling or
    /// existing outputs. Observe root lifetimes, not individual particles. Single-threaded.</summary>
    public sealed class AdaptiveFeedbackBudget
    {
        private readonly double sampleSeconds, warmupSeconds, overloadSeconds, recoverySeconds;
        private readonly int crowdingLimit;
        private readonly double spawnRateLimit;
        private float scale = 1f;
        private bool configured, enabled, warming = true, probing, unrelatedSlowdown;
        private double warmUntil, windowStart, frameSum, overloadFor, stableFor, lastAdjustment;
        private double probeBaseline, probeUntil, targetSeconds;
        private int frames, starts;
        public int Epoch { get; private set; } = 1;
        public int Revision { get; private set; }
        public int ActiveEffects { get; private set; }
        public long Samples { get; private set; }
        public float Scale => scale;
        public bool Enabled => enabled;
        public CombatPresentationSettings Player { get; private set; } = new(1f);
        public VisualEffectSettings Effects { get; private set; } = new CombatPresentationSettings(1f).Effects;
        public AdaptiveFeedbackReason Reason { get; private set; }
        public double LastFrameSeconds { get; private set; }
        public double LastSpawnRate { get; private set; }

        public AdaptiveFeedbackBudget(double sampleSeconds = .5, double warmupSeconds = 2,
            double overloadSeconds = 1.5, double recoverySeconds = 4,
            int crowdingLimit = 80, double spawnRateLimit = 60)
        {
            if (!Positive(sampleSeconds) || !Finite(warmupSeconds) || warmupSeconds < 0 ||
                !Positive(overloadSeconds) || !Positive(recoverySeconds) || crowdingLimit < 1 ||
                !Positive(spawnRateLimit)) throw new ArgumentOutOfRangeException(nameof(sampleSeconds));
            this.sampleSeconds = sampleSeconds; this.warmupSeconds = warmupSeconds;
            this.overloadSeconds = overloadSeconds; this.recoverySeconds = recoverySeconds;
            this.crowdingLimit = crowdingLimit; this.spawnRateLimit = spawnRateLimit;
        }

        public void Configure(float ceiling, bool automatic)
        {
            ceiling = CombatPresentationSettings.NormalizeDensity(ceiling);
            bool active = automatic && ceiling > 0f;
            if (configured && Player.Density == ceiling && enabled == active) return;
            Player = new CombatPresentationSettings(ceiling); enabled = active; configured = true;
            Reset();
        }

        public void Reset()
        {
            Epoch = Epoch == int.MaxValue ? 1 : Epoch + 1;
            ActiveEffects = starts = frames = 0; Samples = 0;
            warming = true; probing = unrelatedSlowdown = false;
            warmUntil = windowStart = frameSum = overloadFor = stableFor = targetSeconds = 0;
            lastAdjustment = double.NegativeInfinity;
            LastFrameSeconds = LastSpawnRate = 0; Reason = AdaptiveFeedbackReason.None;
            scale = 1f; ResolveEffects();
        }

        public void EffectStarted()
        {
            if (!enabled) return;
            if (ActiveEffects < int.MaxValue) ActiveEffects++;
            if (starts < int.MaxValue) starts++;
        }

        public void EffectStopped()
        { if (enabled && ActiveEffects > 0) ActiveEffects--; }

        public void ObserveFrame(double now, double elapsed, double desiredFrameSeconds, bool eligible)
        {
            if (!enabled) return;
            if (!Finite(now) || now < 0) return;
            if (!eligible || !Positive(elapsed) || elapsed > .25 || !Positive(desiredFrameSeconds))
            { RestartObservation(now, Math.Min(warmupSeconds, 1)); return; }
            desiredFrameSeconds = Math.Max(1d / 240d, Math.Min(.1d, desiredFrameSeconds));
            if (targetSeconds != 0 && Math.Abs(desiredFrameSeconds / targetSeconds - 1d) > .1d)
            { targetSeconds = desiredFrameSeconds; RestartObservation(now, warmupSeconds); return; }
            targetSeconds = desiredFrameSeconds;
            if (warming || now < windowStart)
            { warming = false; RestartObservation(now, warmupSeconds); return; }
            if (now < warmUntil) return;
            frameSum += elapsed; frames++;
            double span = now - windowStart;
            if (span < sampleSeconds) return;
            // The warm-up interval is never included in a rate or frame-cost sample.
            if (windowStart < warmUntil)
            { windowStart = now; frameSum = 0; frames = starts = 0; return; }
            LastFrameSeconds = frameSum / Math.Max(1, frames);
            LastSpawnRate = starts / span; Samples++;
            windowStart = now; frameSum = 0; frames = starts = 0;
            Evaluate(now, span);
        }

        private void RestartObservation(double now, double warmup)
        {
            windowStart = now; warmUntil = now + warmup;
            frameSum = overloadFor = stableFor = 0; frames = starts = 0;
            probing = false;
        }

        private void Evaluate(double now, double span)
        {
            bool crowded = ActiveEffects > crowdingLimit || LastSpawnRate > spawnRateLimit;
            bool sparse = ActiveEffects < crowdingLimit * .6 && LastSpawnRate < spawnRateLimit * .6;
            bool healthy = LastFrameSeconds <= targetSeconds * 1.08;
            bool slow = LastFrameSeconds > targetSeconds * 1.18 &&
                (ActiveEffects > 4 || LastSpawnRate > 8);

            if (probing && now >= probeUntil)
            {
                // One measured reduction is enough to test attribution. A non-beneficial
                // reduction cannot repeatedly erase effects to chase AI/physics/loading cost.
                if (LastFrameSeconds >= probeBaseline * .96)
                { unrelatedSlowdown = true; Reason = AdaptiveFeedbackReason.UnrelatedSlowdown; }
                probing = false;
            }
            bool performancePressure = slow && !unrelatedSlowdown && !probing;
            if (crowded || performancePressure)
            {
                overloadFor += Math.Min(span, sampleSeconds * 2); stableFor = 0;
                if (overloadFor >= overloadSeconds && now - lastAdjustment >= 1)
                {
                    float next = Math.Max(.35f, scale - .1f);
                    if (next < scale)
                    {
                        if (performancePressure && !crowded)
                        { probing = true; probeBaseline = LastFrameSeconds; probeUntil = now + 1.5; }
                        SetScale(next); lastAdjustment = now;
                        Reason = crowded ? AdaptiveFeedbackReason.Crowding : AdaptiveFeedbackReason.FrameTime;
                    }
                    overloadFor = 0;
                }
                return;
            }
            overloadFor = 0;
            if (sparse && (healthy || unrelatedSlowdown)) stableFor += Math.Min(span, sampleSeconds * 2);
            else stableFor = 0;
            if (stableFor >= recoverySeconds && now - lastAdjustment >= 1.5)
            {
                if (healthy) unrelatedSlowdown = false;
                if (scale < 1f)
                {
                    SetScale(Math.Min(1f, scale + .025f)); lastAdjustment = now;
                    Reason = AdaptiveFeedbackReason.Recovering;
                }
                else Reason = AdaptiveFeedbackReason.None;
            }
        }

        private void SetScale(float value)
        { if (scale != value) { scale = value; ResolveEffects(); } }

        private void ResolveEffects()
        {
            var original = Player.Effects;
            Effects = !enabled || scale >= 1f ? original : new VisualEffectSettings(
                Math.Max(1, (int)Math.Floor(original.FrameLimit * scale)),
                Math.Max(1, (int)Math.Floor(original.WindowLimit * scale)),
                Math.Max(1, (int)Math.Floor(original.ActiveLimit * scale)),
                original.WindowSeconds, Math.Min(.6d, original.MergeSeconds / scale));
            Revision++;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Positive(double value) => Finite(value) && value > 0;
    }
}
