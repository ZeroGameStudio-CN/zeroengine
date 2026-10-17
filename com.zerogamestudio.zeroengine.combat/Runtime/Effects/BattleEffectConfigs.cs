using System;
using UnityEngine;

namespace ZeroEngine.Combat.Effects
{
    /// <summary>
    /// Transform shake effect parameters.
    /// </summary>
    [Serializable]
    public class ShakeEffectConfig
    {
        public float Duration = 0.12f;
        public float Strength = 0.2f;
        public int Vibrato = 10;
        public float Randomness = 90f;
        public bool FadeOut = true;

        public ShakeEffectConfig()
        {
        }

        public ShakeEffectConfig(float duration, float strength, int vibrato)
        {
            Duration = duration;
            Strength = strength;
            Vibrato = vibrato;
        }

        public ShakeEffectConfig Scaled(float factor)
        {
            return new ShakeEffectConfig
            {
                Duration = Duration,
                Strength = Strength * factor,
                Vibrato = Vibrato,
                Randomness = Randomness,
                FadeOut = FadeOut
            };
        }
    }

    /// <summary>
    /// Renderer flash effect parameters.
    /// </summary>
    [Serializable]
    public class FlashEffectConfig
    {
        public Color FlashColor = Color.white;
        public float FlashDuration = 0.08f;
        public float RecoverDuration = 0.15f;

        public FlashEffectConfig()
        {
        }

        public FlashEffectConfig(Color flashColor, float flashDuration, float recoverDuration)
        {
            FlashColor = flashColor;
            FlashDuration = flashDuration;
            RecoverDuration = recoverDuration;
        }
    }

    /// <summary>
    /// Scale pulse effect parameters.
    /// </summary>
    [Serializable]
    public class ScalePulseConfig
    {
        public float ShrinkScale = 0.85f;
        public float BurstScale = 1.2f;
        public float ShrinkDuration = 0.08f;
        public float BurstDuration = 0.12f;
        public float RecoverDuration = 0.16f;

        public ScalePulseConfig()
        {
        }

        public ScalePulseConfig(float shrinkScale, float burstScale)
        {
            ShrinkScale = shrinkScale;
            BurstScale = burstScale;
        }
    }

    /// <summary>
    /// Knockback effect parameters.
    /// </summary>
    [Serializable]
    public class KnockbackConfig
    {
        public bool Enabled = false;
        public float Distance = 0.25f;
        public float Duration = 0.12f;
        public float BounceDuration = 0.1f;
    }

    /// <summary>
    /// Runtime spawn effect parameters.
    /// </summary>
    [Serializable]
    public class SpawnEffectConfig
    {
        public GameObject Prefab;
        public Vector3 Offset = Vector3.zero;
        public Vector3 Scale = Vector3.one;
        public Color Color = Color.white;
        public int ParticleCount = 8;
        public float Duration = 0.5f;

        public bool HasPrefab => Prefab != null;
    }

    /// <summary>
    /// Combined hit reaction parameters.
    /// </summary>
    [Serializable]
    public class HitReactionConfig
    {
        public bool Enabled = true;
        public ShakeEffectConfig Shake = new();
        public FlashEffectConfig Flash = new();
        public SpawnEffectConfig HitParticle = new();
        public KnockbackConfig Knockback = new();

        public static HitReactionConfig Light => new()
        {
            Shake = new ShakeEffectConfig(0.08f, 0.12f, 8),
            Flash = new FlashEffectConfig(Color.white, 0.05f, 0.12f),
            HitParticle = new SpawnEffectConfig { ParticleCount = 4, Duration = 0.35f },
            Knockback = new KnockbackConfig { Enabled = false }
        };

        public static HitReactionConfig Normal => new()
        {
            Shake = new ShakeEffectConfig(0.12f, 0.2f, 10),
            Flash = new FlashEffectConfig(Color.white, 0.08f, 0.15f),
            HitParticle = new SpawnEffectConfig { ParticleCount = 8, Duration = 0.5f },
            Knockback = new KnockbackConfig { Enabled = false }
        };

        public static HitReactionConfig Heavy => new()
        {
            Shake = new ShakeEffectConfig(0.18f, 0.32f, 14),
            Flash = new FlashEffectConfig(new Color(1f, 0.85f, 0.3f), 0.1f, 0.18f),
            HitParticle = new SpawnEffectConfig
            {
                Color = new Color(1f, 0.75f, 0.2f),
                ParticleCount = 16,
                Duration = 0.65f
            },
            Knockback = new KnockbackConfig
            {
                Enabled = true,
                Distance = 0.35f,
                Duration = 0.14f,
                BounceDuration = 0.1f
            }
        };
    }

    /// <summary>
    /// Combined skill cast effect parameters.
    /// </summary>
    [Serializable]
    public class SkillCastEffectConfig
    {
        public bool Enabled = true;
        public Color GlowColor = Color.white;
        public bool EnableRotation = true;
        public float RotationAngle = 360f;
        public float RotationDuration = 0.25f;
        public ScalePulseConfig ScalePulse = new();
        public SpawnEffectConfig CastParticle = new();
        public bool EnableGroundWave = true;
        public Color WaveColor = new(1f, 1f, 1f, 0.4f);
        public float WaveMaxScale = 3f;
        public float WaveDuration = 0.4f;

        public static SkillCastEffectConfig Fire => new()
        {
            GlowColor = new Color(1f, 0.4f, 0.1f),
            CastParticle = new SpawnEffectConfig
            {
                Color = new Color(1f, 0.5f, 0.1f),
                ParticleCount = 16,
                Duration = 0.6f
            },
            WaveColor = new Color(1f, 0.4f, 0.1f, 0.5f)
        };

        public static SkillCastEffectConfig Ice => new()
        {
            GlowColor = new Color(0.5f, 0.8f, 1f),
            CastParticle = new SpawnEffectConfig
            {
                Color = new Color(0.6f, 0.85f, 1f),
                ParticleCount = 14,
                Duration = 0.7f
            },
            WaveColor = new Color(0.5f, 0.8f, 1f, 0.5f)
        };

        public static SkillCastEffectConfig Heal => new()
        {
            GlowColor = new Color(0.3f, 1f, 0.5f),
            EnableRotation = false,
            ScalePulse = new ScalePulseConfig(0.85f, 1.2f),
            CastParticle = new SpawnEffectConfig
            {
                Color = new Color(0.4f, 1f, 0.6f),
                ParticleCount = 10,
                Duration = 0.8f
            },
            WaveColor = new Color(0.3f, 1f, 0.5f, 0.4f)
        };

        public static SkillCastEffectConfig Physical => new()
        {
            GlowColor = new Color(1f, 0.7f, 0.3f),
            EnableRotation = false,
            ScalePulse = new ScalePulseConfig(0.6f, 1.5f),
            CastParticle = new SpawnEffectConfig
            {
                Color = new Color(1f, 0.8f, 0.4f),
                ParticleCount = 8,
                Duration = 0.4f
            },
            EnableGroundWave = false
        };

        public static SkillCastEffectConfig Dark => new()
        {
            GlowColor = new Color(0.6f, 0.2f, 0.8f),
            CastParticle = new SpawnEffectConfig
            {
                Color = new Color(0.5f, 0.1f, 0.7f),
                ParticleCount = 14,
                Duration = 0.7f
            },
            WaveColor = new Color(0.5f, 0.15f, 0.7f, 0.5f)
        };
    }
}
