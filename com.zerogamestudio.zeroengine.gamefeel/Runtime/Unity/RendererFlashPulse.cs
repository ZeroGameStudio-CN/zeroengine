using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.GameFeel.Unity
{
    /// <summary>Applies a caller-timed color pulse to renderer properties without replacing other block values.</summary>
    public sealed class RendererFlashPulse
    {
        private struct Channel
        {
            public Color Home;
            public Color Last;
        }

        private sealed class Slot
        {
            public Renderer Renderer;
            public Channel First;
            public Channel Second;
        }

        private readonly int first;
        private readonly int? second;
        private readonly List<Slot> slots = new();
        private readonly Stack<Slot> pool = new();
        private MaterialPropertyBlock block;
        private Color peak;
        private int version;

        public RendererFlashPulse(int firstColorProperty, int? secondColorProperty = null)
        {
            first = firstColorProperty;
            second = secondColorProperty == firstColorProperty ? null : secondColorProperty;
        }

        public int Begin(Color peakColor)
        {
            Clear();
            peak = peakColor;
            return unchecked(++version);
        }

        public void Include(Renderer renderer)
        {
            if (!renderer) return;
            foreach (var item in slots)
                if (item.Renderer == renderer) return;
            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Slot slot = pool.Count == 0 ? new Slot() : pool.Pop();
            slot.Renderer = renderer;
            slot.First = Capture(renderer, first);
            if (second.HasValue) slot.Second = Capture(renderer, second.Value);
            renderer.SetPropertyBlock(block);
            slots.Add(slot);
        }

        public void Sample(int pulse, float progress)
        {
            if (version != pulse) return;
            Apply(ImpactEnvelopes.FlashRemaining(progress));
        }

        public void Clear(int pulse)
        {
            if (version == pulse) Clear();
        }

        public void Clear()
        {
            Apply(0f);
            foreach (var slot in slots)
            {
                slot.Renderer = null;
                pool.Push(slot);
            }
            slots.Clear();
        }

        private Channel Capture(Renderer renderer, int property)
        {
            var material = renderer.sharedMaterial;
            Color home = block.HasColor(property) ? block.GetColor(property) :
                material && material.HasProperty(property) ? material.GetColor(property) : Color.white;
            block.SetColor(property, peak);
            return new Channel { Home = home, Last = peak };
        }

        private void Apply(float weight)
        {
            foreach (var slot in slots)
            {
                var renderer = slot.Renderer;
                if (!renderer) continue;
                renderer.GetPropertyBlock(block);
                bool changed = Set(first, ref slot.First, weight);
                if (second.HasValue) changed |= Set(second.Value, ref slot.Second, weight);
                if (changed) renderer.SetPropertyBlock(block);
            }
        }

        private bool Set(int property, ref Channel channel, float weight)
        {
            if (!block.HasColor(property) || block.GetColor(property) != channel.Last) return false;
            Color color = Color.LerpUnclamped(channel.Home, peak, weight);
            if (color == channel.Last) return false;
            block.SetColor(property, color);
            channel.Last = color;
            return true;
        }
    }
}
