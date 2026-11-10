using System;
using System.Collections.Generic;
using System.Threading;

namespace ZeroEngine.GameFeel
{
    public enum FeedbackReservationDecision
    {
        New,
        Stronger,
        Duplicate,
        CapacityFallback,
        FrameBudgetExceeded,
        ReservationCapacityExceeded,
        InvalidKey,
        InvalidStrength,
        InvalidTime,
        ClockMovedBackwards,
    }

    public enum FeedbackReservationCommitResult
    {
        Committed,
        NotAccepted,
        StaleReservation,
        WrongCoordinator,
        InvalidTime,
    }

    /// <summary>
    /// An output admission that is still waiting for the consumer's synchronous output call.
    /// The consumer must call Complete after the output attempt; it must not retain this value
    /// across frames or asynchronous work.
    /// </summary>
    public readonly struct FeedbackReservation<TVariant>
        where TVariant : struct, IEquatable<TVariant>
    {
        private readonly long ownerId, generation, token;
        private readonly int pendingSlot, frameIndex;

        internal FeedbackReservation(
            long ownerId,
            long generation,
            long token,
            int pendingSlot,
            int frameIndex,
            TVariant variant,
            FeedbackKey key,
            double strength,
            double firstAcceptedAt,
            double previousStrength,
            FeedbackReservationDecision decision,
            bool tracked)
        {
            this.ownerId = ownerId;
            this.generation = generation;
            this.token = token;
            this.pendingSlot = pendingSlot;
            this.frameIndex = frameIndex;
            Variant = variant;
            Key = key;
            Strength = strength;
            FirstAcceptedAt = firstAcceptedAt;
            PreviousStrength = previousStrength;
            Decision = decision;
            IsTracked = tracked;
        }

        public TVariant Variant { get; }
        public FeedbackKey Key { get; }
        public double Strength { get; }
        public double FirstAcceptedAt { get; }
        public double PreviousStrength { get; }
        public FeedbackReservationDecision Decision { get; }
        public bool CanPlay => token != 0 && Decision is FeedbackReservationDecision.New
            or FeedbackReservationDecision.Stronger
            or FeedbackReservationDecision.CapacityFallback;
        public bool IsTracked { get; }
        public bool IsFallback => Decision == FeedbackReservationDecision.CapacityFallback;

        internal long OwnerId => ownerId;
        internal long Generation => generation;
        internal long Token => token;
        internal int PendingSlot => pendingSlot;
        internal int FrameIndex => frameIndex;
    }

    /// <summary>
    /// Generic consumer-owned admission for feedback that has a variant/scope identity.
    /// It combines the package's identity ledger with bounded per-frame budget and re-entry
    /// reservations. The output, clock source, priority units and lifecycle owner stay outside.
    /// </summary>
    public sealed class FeedbackReservationCoordinator<TVariant>
        where TVariant : struct, IEquatable<TVariant>
    {
        private sealed class VariantState
        {
            internal TVariant Variant;
            internal double LastAccepted;
            internal int PendingCount;
            internal FeedbackCoordinator Ledger;
        }

        private struct PendingState
        {
            internal bool Occupied;
            internal TVariant Variant;
            internal FeedbackKey Key;
            internal double Strength;
            internal long Token;
        }

        private static long nextOwnerId;
        private readonly long ownerId;
        private readonly VariantState[] variants;
        private readonly PendingState[] pending;
        private readonly int identityCapacity;
        private readonly double retentionSeconds;
        private long generation = 1;
        private long nextToken;
        private double lastObservedTime = double.NegativeInfinity;
        private int frameIndex = -1;
        private int frameAccepted;
        private int framePending;
        private int inFlight;

        public FeedbackReservationCoordinator(
            int variantCapacity,
            int identityCapacity,
            int reservationCapacity,
            double retentionSeconds)
        {
            if (variantCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(variantCapacity));
            if (identityCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(identityCapacity));
            if (reservationCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(reservationCapacity));
            if (!Finite(retentionSeconds) || retentionSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(retentionSeconds));

            ownerId = Interlocked.Increment(ref nextOwnerId);
            if (ownerId <= 0) throw new InvalidOperationException("Feedback reservation identity space exhausted.");
            variants = new VariantState[variantCapacity];
            pending = new PendingState[reservationCapacity];
            this.identityCapacity = identityCapacity;
            this.retentionSeconds = retentionSeconds;
        }

        public int VariantCapacity => variants.Length;
        public int ReservationCapacity => pending.Length;
        public int ActiveVariantCount { get; private set; }
        public int InFlightCount => inFlight;
        public int FrameAcceptedCount => frameAccepted;
        public int FramePendingCount => framePending;
        public FeedbackReservationDecision LastDecision { get; private set; }

        public bool TryBegin(
            TVariant variant,
            FeedbackKey key,
            double strength,
            double now,
            int frame,
            int frameBudget,
            bool important,
            out FeedbackReservation<TVariant> reservation)
        {
            reservation = default;
            if (!key.IsValid) return Reject(FeedbackReservationDecision.InvalidKey);
            if (!Finite(strength) || strength < 0) return Reject(FeedbackReservationDecision.InvalidStrength);
            if (!Finite(now) || now < 0) return Reject(FeedbackReservationDecision.InvalidTime);

            ObserveClock(now);
            AdvanceFrame(frame);
            int pendingSlot = FindPendingSlot(variant, key, strength);
            if (pendingSlot == -1) return Reject(FeedbackReservationDecision.Duplicate);
            if (pendingSlot == -2) return Reject(FeedbackReservationDecision.ReservationCapacityExceeded);

            int variantSlot = FindVariant(variant, now);
            bool tracked = variantSlot >= 0;
            FeedbackReservationDecision decision = FeedbackReservationDecision.CapacityFallback;
            double firstAcceptedAt = now;
            double previousStrength = 0;
            if (tracked)
            {
                FeedbackEvaluation evaluation = variants[variantSlot].Ledger.Evaluate(key, strength, now);
                if (evaluation.Decision == FeedbackDecision.Duplicate)
                    return Reject(FeedbackReservationDecision.Duplicate);
                if (evaluation.Decision == FeedbackDecision.New || evaluation.Decision == FeedbackDecision.Stronger)
                {
                    decision = evaluation.Decision == FeedbackDecision.New
                        ? FeedbackReservationDecision.New
                        : FeedbackReservationDecision.Stronger;
                    firstAcceptedAt = evaluation.FirstAcceptedAt;
                    previousStrength = evaluation.PreviousStrength;
                }
                else
                {
                    tracked = false;
                }
            }

            if (!important && frameBudget > 0 && frameAccepted + framePending >= frameBudget)
                return Reject(FeedbackReservationDecision.FrameBudgetExceeded);

            long token = ++nextToken;
            pending[pendingSlot] = new PendingState
            {
                Occupied = true,
                Variant = variant,
                Key = key,
                Strength = strength,
                Token = token,
            };
            if (tracked) variants[variantSlot].PendingCount++;
            inFlight++;
            framePending++;
            reservation = new FeedbackReservation<TVariant>(
                ownerId,
                generation,
                token,
                pendingSlot,
                frame,
                variant,
                key,
                strength,
                firstAcceptedAt,
                previousStrength,
                decision,
                tracked);
            LastDecision = decision;
            return true;
        }

        public FeedbackReservationCommitResult Complete(
            FeedbackReservation<TVariant> reservation,
            bool outputAccepted,
            double now)
        {
            if (!reservation.CanPlay) return FeedbackReservationCommitResult.NotAccepted;
            if (reservation.OwnerId != ownerId) return FeedbackReservationCommitResult.WrongCoordinator;
            if (reservation.Generation != generation) return FeedbackReservationCommitResult.StaleReservation;
            if (!Finite(now) || now < 0) return FeedbackReservationCommitResult.InvalidTime;
            if (reservation.PendingSlot < 0 || reservation.PendingSlot >= pending.Length)
                return FeedbackReservationCommitResult.StaleReservation;

            ObserveClock(now);
            ref PendingState pendingState = ref pending[reservation.PendingSlot];
            if (!pendingState.Occupied || pendingState.Token != reservation.Token)
                return FeedbackReservationCommitResult.StaleReservation;
            pendingState = default;
            inFlight--;
            if (reservation.FrameIndex == frameIndex) framePending--;

            VariantState state = null;
            if (reservation.IsTracked)
            {
                int variantSlot = FindExistingVariant(reservation.Variant);
                if (variantSlot < 0) return FeedbackReservationCommitResult.StaleReservation;
                state = variants[variantSlot];
                state.PendingCount--;
            }
            if (!outputAccepted) return FeedbackReservationCommitResult.Committed;
            if (reservation.FrameIndex == frameIndex) frameAccepted++;
            if (!reservation.IsTracked) return FeedbackReservationCommitResult.Committed;
            FeedbackEvaluation evaluation = state.Ledger.Evaluate(reservation.Key, reservation.Strength, now);
            if (!evaluation.CanPlay)
                return FeedbackReservationCommitResult.StaleReservation;
            FeedbackCommitResult result = state.Ledger.Commit(
                evaluation,
                now + retentionSeconds,
                out _);
            if (result != FeedbackCommitResult.Committed)
                return FeedbackReservationCommitResult.StaleReservation;
            state.LastAccepted = now;
            return FeedbackReservationCommitResult.Committed;
        }

        public void Reset()
        {
            Array.Clear(variants, 0, variants.Length);
            Array.Clear(pending, 0, pending.Length);
            generation = checked(generation + 1);
            lastObservedTime = double.NegativeInfinity;
            frameIndex = -1;
            frameAccepted = framePending = inFlight = 0;
            ActiveVariantCount = 0;
            LastDecision = default;
        }

        private int FindVariant(TVariant variant, double now)
        {
            int free = -1;
            for (int i = 0; i < variants.Length; i++)
            {
                VariantState state = variants[i];
                if (state == null)
                {
                    if (free < 0) free = i;
                    continue;
                }
                if (state.Variant.Equals(variant)) return i;
                if (state.PendingCount == 0 && now - state.LastAccepted >= retentionSeconds)
                {
                    variants[i] = null;
                    ActiveVariantCount--;
                    if (free < 0) free = i;
                }
            }
            if (free < 0) return -1;
            variants[free] = new VariantState
            {
                Variant = variant,
                LastAccepted = now,
                Ledger = new FeedbackCoordinator(identityCapacity),
            };
            ActiveVariantCount++;
            return free >= 0 ? free : -2;
        }

        private int FindExistingVariant(TVariant variant)
        {
            for (int i = 0; i < variants.Length; i++)
                if (variants[i] != null && variants[i].Variant.Equals(variant)) return i;
            return -1;
        }

        private int FindPendingSlot(TVariant variant, FeedbackKey key, double strength)
        {
            int free = -1;
            for (int i = 0; i < pending.Length; i++)
            {
                PendingState state = pending[i];
                if (!state.Occupied)
                {
                    if (free < 0) free = i;
                    continue;
                }
                if (state.Variant.Equals(variant) && state.Key == key && state.Strength >= strength)
                    return -1;
            }
            return free >= 0 ? free : -2;
        }

        private void ObserveClock(double now)
        {
            if (now < lastObservedTime) Reset();
            lastObservedTime = now;
        }

        private void AdvanceFrame(int frame)
        {
            if (frame == frameIndex) return;
            frameIndex = frame;
            frameAccepted = framePending = 0;
        }

        private bool Reject(FeedbackReservationDecision decision)
        {
            LastDecision = decision;
            return false;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
