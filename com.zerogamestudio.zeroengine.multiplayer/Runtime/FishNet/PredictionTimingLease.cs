using System;
using FishNet.Managing;
using FishNet.Managing.Predicting;
using FishNet.Managing.Timing;
using UnityEngine;

namespace ZeroEngine.Multiplayer.FishNet
{
    // One owner for temporary prediction configuration; game policy supplies the desired values.
    public sealed class PredictionTimingLease : IDisposable
    {
        private readonly ushort tickRate;
        private readonly byte inputQueueLimit;
        private TimeManager timeOwner;
        private PredictionManager queueOwner, stateOwner;
        private ushort previousTickRate;
        private byte previousQueueLimit;
        private ReplicateStateOrder previousStateOrder;
        private PhysicsMode previousPhysicsMode;
        private float previousFixedDeltaTime;
        private bool timingAcquired;
        public PredictionTimingLease(ushort tickRate, byte inputQueueLimit)
        {
            if (tickRate == 0) throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (inputQueueLimit == 0) throw new ArgumentOutOfRangeException(nameof(inputQueueLimit));
            this.tickRate = tickRate; this.inputQueueLimit = inputQueueLimit;
        }
        public bool TryAcquire(NetworkManager manager, out string error)
        {
            if (timingAcquired) { error = string.Empty; return true; }
            if (!manager || !manager.TimeManager)
            { error = "FishNet TimeManager is unavailable for prediction."; return false; }
            if (!manager.TryGetComponent<PredictionManager>(out var prediction))
            { error = "FishNet PredictionManager is unavailable for prediction."; return false; }
            timeOwner = manager.TimeManager;
            previousTickRate = timeOwner.TickRate; previousPhysicsMode = timeOwner.PhysicsMode;
            previousFixedDeltaTime = Time.fixedDeltaTime;
            timeOwner.SetTickRate(tickRate); timeOwner.SetPhysicsMode(PhysicsMode.TimeManager);
            Time.fixedDeltaTime = (float)timeOwner.TickDelta;
            AcquireInputQueue(prediction); AcquireStateOrder(prediction);
            timingAcquired = true; error = string.Empty; return true;
        }
        public void AcquireInputQueue(PredictionManager owner)
        {
            if (!owner) throw new ArgumentNullException(nameof(owner));
            if (queueOwner == owner) return;
            ReleaseInputQueue(); previousQueueLimit = owner.GetMaximumServerReplicates();
            owner.SetMaximumServerReplicates(inputQueueLimit); queueOwner = owner;
        }
        public void ReleaseInputQueue()
        {
            var owner = queueOwner; queueOwner = null;
            if (owner && owner.GetMaximumServerReplicates() == inputQueueLimit)
                owner.SetMaximumServerReplicates(previousQueueLimit);
        }
        public void AcquireStateOrder(PredictionManager owner)
        {
            if (!owner) throw new ArgumentNullException(nameof(owner));
            if (stateOwner == owner) return;
            ReleaseStateOrder(); previousStateOrder = owner.StateOrder;
            owner.SetStateOrder(ReplicateStateOrder.Inserted); stateOwner = owner;
        }
        public void ReleaseStateOrder()
        {
            var owner = stateOwner; stateOwner = null;
            if (owner && owner.StateOrder == ReplicateStateOrder.Inserted)
                owner.SetStateOrder(previousStateOrder);
        }
        public void Dispose()
        {
            ReleaseStateOrder(); ReleaseInputQueue();
            if (!timingAcquired) return;
            if (timeOwner)
            { timeOwner.SetTickRate(previousTickRate); timeOwner.SetPhysicsMode(previousPhysicsMode); }
            Time.fixedDeltaTime = previousFixedDeltaTime;
            timeOwner = null; timingAcquired = false;
        }
    }
}
