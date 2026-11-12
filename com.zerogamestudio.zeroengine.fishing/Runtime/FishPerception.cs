using System;
using System.Numerics;
namespace ZeroEngine.Fishing
{
    [Serializable]
    public sealed class FishSenseSettings
    {
        public float SightRange = 4;
        public float HearingRange = 3;
        public float ViewDotThreshold = -.25f;
        public ulong PreferredLureMask;
        public ulong AvoidedLureMask;
    }

    public readonly struct LureStimulus
    {
        public readonly Vector2 Position;
        public readonly bool Submerged;
        public readonly float Visibility, Noise;
        public readonly ulong TypeMask;
        public LureStimulus(Vector2 position, bool submerged, float visibility = 1, float noise = 0, ulong typeMask = 0)
        { Position = position; Submerged = submerged; Visibility = visibility; Noise = noise; TypeMask = typeMask; }
    }

    public readonly struct FishInterest
    {
        public readonly float Strength;
        public readonly bool Seen, Heard;
        public bool Interested => Strength > 0;
        public FishInterest(float strength, bool seen, bool heard) { Strength = strength; Seen = seen; Heard = heard; }
    }

    public static class FishPerception
    {
        // The host supplies water/occlusion facts. No hidden raycasts or game-specific lure identifiers.
        public static FishInterest Inspect(Vector2 position, Vector2 forward, FishSenseSettings settings, LureStimulus lure, bool occluded = false)
        {
            if (settings == null || !lure.Submerged || !FishMath.Finite(lure.Visibility) || !FishMath.Finite(lure.Noise) ||
                (lure.TypeMask & settings.AvoidedLureMask) != 0) return default;
            var gap = lure.Position - position;
            float distance = gap.Length();
            float sight = Math.Max(0, settings.SightRange) * Math.Max(0, lure.Visibility);
            float hearing = Math.Max(0, settings.HearingRange) * Math.Max(0, lure.Noise);
            bool seen = !occluded && distance <= sight && sight > 0 &&
                (distance < .001f || forward.LengthSquared() < .001f || Vector2.Dot(FishMath.SafeDirection(forward), FishMath.SafeDirection(gap)) >= settings.ViewDotThreshold);
            bool heard = distance <= hearing && hearing > 0;
            float strength = Math.Max(seen ? 1 - distance / Math.Max(.001f, sight) : 0, heard ? (1 - distance / Math.Max(.001f, hearing)) * .7f : 0);
            if ((lure.TypeMask & settings.PreferredLureMask) != 0) strength *= 1.5f;
            return new FishInterest(FishMath.Clamp01(strength), seen, heard);
        }

        // Threat roles come from species data, not rarity. This changes steering, never stock or rewards.
        public static Vector2 AvoidThreat(Vector2 position, Vector2 threat, float fearRadius, float strength = 1)
        {
            var away = position - threat;
            float distance = away.Length();
            if (!FishMath.Finite(fearRadius) || fearRadius <= 0 || distance >= fearRadius || distance < .001f) return Vector2.Zero;
            return away / distance * (1 - distance / fearRadius) * Math.Max(0, strength);
        }
    }
}
