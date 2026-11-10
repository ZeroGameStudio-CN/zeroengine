using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Render
{
    public sealed class RendererOpacityBinding : IDisposable
    {
        private readonly Action<Renderer, Material> onUnsupported;
        private readonly Renderer renderer;
        private readonly SpriteRenderer sprite;
        private readonly LineRenderer line;
        private readonly TrailRenderer trail;
        private readonly ParticleSystem particle;
        private readonly List<MaterialChannel> materials = new();
        private Color originalColor, appliedColor;
        private Gradient originalGradient, appliedGradient;
        private ParticleSystem.MinMaxGradient originalStartColor;
        private bool applied, hidden, hiddenWas;
        private bool useParticleColor;
        private readonly List<Material> cachedMaterials = new(), currentMaterials = new();
        private readonly List<Shader> cachedShaders = new();
        public Renderer Renderer => renderer;

        public RendererOpacityBinding(Renderer renderer, Action<Renderer, Material> onUnsupported = null)
        {
            this.renderer = renderer; this.onUnsupported = onUnsupported;
            sprite = renderer as SpriteRenderer;
            line = renderer as LineRenderer;
            trail = renderer as TrailRenderer;
            if (renderer is ParticleSystemRenderer) particle = renderer.GetComponent<ParticleSystem>();
            // Material channels are only needed by a partial fade. Full and hidden
            // effects can stay on the pool's cached binding without querying materials.
        }

        private void RefreshMaterials()
        {
            if (sprite || !renderer) return;
            renderer.GetSharedMaterials(currentMaterials);
            bool changed = currentMaterials.Count != cachedMaterials.Count;
            for (int i = 0; !changed && i < currentMaterials.Count; i++)
                changed = currentMaterials[i] != cachedMaterials[i] ||
                    (currentMaterials[i] && currentMaterials[i].shader != cachedShaders[i]);
            if (!changed && cachedMaterials.Count != 0) return;
            using var lease = UnityEngine.Pool.ListPool<MaterialChannel>.Get(out var old);
            old.AddRange(materials);
            materials.Clear();
            for (int index = 0; index < currentMaterials.Count; index++)
            {
                int slot = currentMaterials.Count == 1 ? -1 : index;
                MaterialChannel previous = null;
                foreach (var candidate in old) if (candidate.Index == slot) { previous = candidate; break; }
                var channel = new MaterialChannel(renderer, currentMaterials[index], slot, previous, onUnsupported);
                if (channel.Supported) materials.Add(channel);
                else onUnsupported?.Invoke(renderer, currentMaterials[index]);
            }
            cachedMaterials.Clear(); cachedMaterials.AddRange(currentMaterials);
            cachedShaders.Clear();
            foreach (var material in currentMaterials) cachedShaders.Add(material ? material.shader : null);
            useParticleColor = particle && materials.Count == 0;
        }

        public void Apply(float multiplier)
        {
            multiplier = float.IsNaN(multiplier) || float.IsInfinity(multiplier) ? 1f : Mathf.Clamp01(multiplier);
            Restore();
            if (!renderer) return;
            if (multiplier >= 1f)
            {
                // A previous fade can leave a neutral MPB value. Keep it following
                // animated material input; untouched bindings still do no material work.
                if (materials.Count != 0)
                {
                    RefreshMaterials();
                    foreach (var channel in materials) channel.FollowMaterial();
                }
                return;
            }
            applied = true;
            if (multiplier <= 0f)
            {
                hiddenWas = renderer.forceRenderingOff;
                renderer.forceRenderingOff = true;
                hidden = true;
                return;
            }
            RefreshMaterials();
            if (sprite)
            {
                originalColor = sprite.color;
                appliedColor = originalColor;
                appliedColor.a *= multiplier;
                sprite.color = appliedColor;
            }
            else if (materials.Count != 0)
            {
                foreach (var channel in materials) channel.Apply(multiplier);
            }
            else if (line || trail)
            {
                originalGradient = line ? line.colorGradient : trail.colorGradient;
                appliedGradient = ScaleGradient(originalGradient, multiplier);
                if (line) line.colorGradient = appliedGradient;
                else trail.colorGradient = appliedGradient;
            }
            else if (useParticleColor)
            {
                var main = particle.main;
                originalStartColor = main.startColor;
                main.startColor = ScaleStartColor(originalStartColor, multiplier);
            }
        }

        public void Restore()
        {
            if (!applied) return;
            applied = false;
            if (!renderer) return;
            if (hidden)
            {
                if (renderer.forceRenderingOff) renderer.forceRenderingOff = hiddenWas;
                hidden = false;
                return;
            }
            if (sprite)
            {
                var current = sprite.color;
                if (Mathf.Approximately(current.a, appliedColor.a))
                { current.a = originalColor.a; sprite.color = current; }
            }
            else if (materials.Count != 0)
            {
                foreach (var channel in materials) channel.Restore();
            }
            else if (line || trail)
            {
                var current = line ? line.colorGradient : trail.colorGradient;
                if (!SameAlpha(current, appliedGradient)) return;
                current.SetKeys(current.colorKeys, originalGradient.alphaKeys);
                if (line) line.colorGradient = current;
                else trail.colorGradient = current;
            }
            else if (useParticleColor && particle)
            {
                var main = particle.main;
                main.startColor = originalStartColor;
            }
        }

        public void Dispose() => Restore();

        private static Gradient ScaleGradient(Gradient source, float multiplier)
        {
            var result = new Gradient { mode = source.mode };
            var alpha = source.alphaKeys;
            for (int i = 0; i < alpha.Length; i++) alpha[i].alpha *= multiplier;
            result.SetKeys(source.colorKeys, alpha);
            return result;
        }

        private static bool SameAlpha(Gradient left, Gradient right)
        {
            var a = left.alphaKeys; var b = right.alphaKeys;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!Mathf.Approximately(a[i].alpha, b[i].alpha) || a[i].time != b[i].time) return false;
            return true;
        }

        private static ParticleSystem.MinMaxGradient ScaleStartColor(ParticleSystem.MinMaxGradient source, float multiplier)
        {
            var result = source;
            if (source.mode is ParticleSystemGradientMode.Color)
            { var color = source.color; color.a *= multiplier; result.color = color; }
            else if (source.mode is ParticleSystemGradientMode.TwoColors)
            {
                var min = source.colorMin; var max = source.colorMax;
                min.a *= multiplier; max.a *= multiplier;
                result.colorMin = min; result.colorMax = max;
            }
            else if (source.mode is ParticleSystemGradientMode.TwoGradients)
            { result.gradientMin = ScaleGradient(source.gradientMin, multiplier); result.gradientMax = ScaleGradient(source.gradientMax, multiplier); }
            else result.gradient = ScaleGradient(source.gradient, multiplier);
            return result;
        }
    }

    internal sealed class MaterialChannel
    {
        private static readonly int[] Colors = { Shader.PropertyToID("_BaseColor"), Shader.PropertyToID("_Color"),
            Shader.PropertyToID("_TintColor"), Shader.PropertyToID("_Tint") };
        private static readonly int[] Scalars = { Shader.PropertyToID("_Opacity"), Shader.PropertyToID("_Alpha"),
            Shader.PropertyToID("_AlphaMultiplier") };
        private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        private readonly Renderer renderer;
        private readonly Material material;
        private readonly int index, property;
        private readonly bool color, scaleRgb, multiply;
        private readonly MaterialPropertyBlock block = new();
        private Color authoredColor, outputColor;
        private float authoredFloat, outputFloat;
        private bool applied;
        private bool materialSource, injected;
        private Color neutralColor;
        private float neutralFloat;
        internal bool Supported => property != 0;
        internal int Index => index;

        internal MaterialChannel(Renderer renderer, Material material, int index, MaterialChannel previous = null, Action<Renderer, Material> onUnsupported = null)
        {
            this.renderer = renderer; this.material = material; this.index = index;
            if (!material || !material.shader) return;
            // Dedicated opacity controls are authoritative when exposed. Some shader families
            // use their global color for RGB only (for example AllIn1), ignoring its alpha.
            foreach (int id in Scalars)
                if (material.HasFloat(id)) { property = id; break; }
            if (property == 0)
                foreach (int id in Colors)
                    if (material.HasColor(id)) { property = id; color = true; break; }
            if (material.HasFloat(SrcBlend) && material.HasFloat(DstBlend))
            {
                int src = Mathf.RoundToInt(material.GetFloat(SrcBlend));
                int dst = Mathf.RoundToInt(material.GetFloat(DstBlend));
                scaleRgb = src == (int)UnityEngine.Rendering.BlendMode.One && dst == (int)UnityEngine.Rendering.BlendMode.One;
                multiply = src == (int)UnityEngine.Rendering.BlendMode.DstColor && dst == 0;
            }
            else scaleRgb = material.IsKeywordEnabled("_CFXR_ADDITIVE");
            if (material.GetTag("RenderType", false) == "TransparentCutout")
                onUnsupported?.Invoke(renderer, material);
            ReadBlock();
            materialSource = color ? !block.HasColor(property) : !block.HasFloat(property);
            if (previous != null && previous.property == property && previous.materialSource)
            {
                materialSource = true; injected = previous.injected;
                neutralColor = previous.neutralColor; neutralFloat = previous.neutralFloat;
            }
        }

        internal void Apply(float multiplier)
        {
            if (!renderer || !material) return;
            ReadBlock();
            // Validate a previously injected neutral value using this same read.
            // A partial fade immediately writes its new output, so a separate
            // FollowMaterial read/write would be redundant here.
            if (materialSource && injected)
            {
                bool exists = color ? block.HasColor(property) : block.HasFloat(property);
                if (!exists) injected = false;
                else if (color ? !SameColor(block.GetColor(property), neutralColor)
                    : !Mathf.Approximately(block.GetFloat(property), neutralFloat)) materialSource = false;
            }
            if (color)
            {
                authoredColor = materialSource || !block.HasColor(property) ? material.GetColor(property) : block.GetColor(property);
                outputColor = multiply ? Color.Lerp(Color.white, authoredColor, multiplier) : authoredColor;
                if (!multiply)
                {
                    if (scaleRgb) { outputColor.r *= multiplier; outputColor.g *= multiplier; outputColor.b *= multiplier; }
                    // Pure additive output ignores destination alpha: scale RGB only so
                    // shaders that premultiply their own alpha do not square the user's factor.
                    else outputColor.a *= multiplier;
                }
                block.SetColor(property, outputColor);
            }
            else
            {
                authoredFloat = materialSource || !block.HasFloat(property) ? material.GetFloat(property) : block.GetFloat(property);
                outputFloat = authoredFloat * multiplier;
                block.SetFloat(property, outputFloat);
            }
            WriteBlock();
            injected |= materialSource;
            applied = true;
        }

        internal void Restore()
        {
            if (!applied) return;
            applied = false;
            if (!renderer) return;
            ReadBlock();
            if (materialSource && material)
            {
                bool owned = color ? SameColor(block.GetColor(property), outputColor)
                    : Mathf.Approximately(block.GetFloat(property), outputFloat);
                if (owned)
                {
                    if (color) { neutralColor = material.GetColor(property); block.SetColor(property, neutralColor); }
                    else { neutralFloat = material.GetFloat(property); block.SetFloat(property, neutralFloat); }
                    WriteBlock();
                    return;
                }
                materialSource = false; // another writer now owns this property
            }
            if (color)
            {
                var current = block.GetColor(property);
                // Other writers can change RGB while opacity is active; restore only channels still owned here.
                if (Mathf.Approximately(current.a, outputColor.a)) current.a = authoredColor.a;
                if (scaleRgb || multiply)
                {
                    if (Mathf.Approximately(current.r, outputColor.r)) current.r = authoredColor.r;
                    if (Mathf.Approximately(current.g, outputColor.g)) current.g = authoredColor.g;
                    if (Mathf.Approximately(current.b, outputColor.b)) current.b = authoredColor.b;
                }
                block.SetColor(property, current);
            }
            else if (Mathf.Approximately(block.GetFloat(property), outputFloat)) block.SetFloat(property, authoredFloat);
            WriteBlock();
        }

        // Unity cannot remove one MPB property. Keep an injected neutral value synchronized
        // with its actual material source; never turn a temporary override into a frozen fade.
        internal void FollowMaterial()
        {
            if (!materialSource || !injected || !renderer || !material) return;
            ReadBlock();
            bool exists = color ? block.HasColor(property) : block.HasFloat(property);
            if (!exists) { injected = false; return; }
            bool owned = color ? SameColor(block.GetColor(property), neutralColor)
                : Mathf.Approximately(block.GetFloat(property), neutralFloat);
            if (!owned) { materialSource = false; return; }
            if (color)
            {
                var current = material.GetColor(property);
                if (SameColor(current, neutralColor)) return;
                neutralColor = current; block.SetColor(property, current);
            }
            else
            {
                float current = material.GetFloat(property);
                if (Mathf.Approximately(current, neutralFloat)) return;
                neutralFloat = current; block.SetFloat(property, current);
            }
            WriteBlock();
        }

        private static bool SameColor(Color left, Color right) => Mathf.Approximately(left.r, right.r) &&
            Mathf.Approximately(left.g, right.g) && Mathf.Approximately(left.b, right.b) && Mathf.Approximately(left.a, right.a);

        private void ReadBlock()
        {
            if (index < 0) renderer.GetPropertyBlock(block);
            else
            {
                renderer.GetPropertyBlock(block, index);
                if (block.isEmpty) renderer.GetPropertyBlock(block);
            }
        }

        private void WriteBlock()
        {
            if (index < 0) renderer.SetPropertyBlock(block);
            else renderer.SetPropertyBlock(block, index);
        }
    }
}
