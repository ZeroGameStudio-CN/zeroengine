using System;
namespace ZeroEngine.Fishing
{
    [Serializable]
    public sealed class FishSwimSettings
    {
        public bool Enabled = true;
        public float PaceVariation = .4f;
        public float BurstMultiplier = 1.65f;
        public float NeighbourRadius = 1.4f;
        public float PersonalSpace = .55f;
        public float SteeringResponse = 2.4f;
        public float InteractionSpeedMultiplier = 1.8f;
        public float InteractionAcceleration = 3.5f;
        public float ReactionDelay = .15f;
        public float ReactionVariation = .5f;
        public float TurnSpeed = 4.5f;
    }
}
