using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Render
{
    /// <summary>Independent source lifetimes with one rendered appearance per status kind.</summary>
    public sealed class BodyVisualHost : IDisposable
    {
        private const int MaximumConcurrent = 16;
        private const int SmallParticleBudget = BodyVisual.MaximumSmallParticles;
        private readonly Transform origin;
        private readonly Func<Renderer> body;
        private readonly Func<bool, IRenderPoseSource> sourceFactory;
        private readonly Func<float> clock;
        private readonly BodyVisualResources resources;
        private bool disposed, statusEffectsVisible = true;
        private VisualPalette palette;
        public BodyVisualHost(Transform origin, Func<Renderer> body,
            Func<bool, IRenderPoseSource> sourceFactory, Func<float> clock, BodyVisualResources resources)
        {
            this.origin = origin ? origin : throw new ArgumentNullException(nameof(origin));
            this.body = body ?? throw new ArgumentNullException(nameof(body));
            this.sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.resources = resources;
        }
        public void SetStatusEffectsVisible(bool visible) => statusEffectsVisible = visible;
        public void SetPalette(VisualPalette value) => palette = value;
        private bool IsAlive => !disposed && origin && origin.gameObject.activeInHierarchy;
        private bool clearing;
        private bool statusVisualsEnabled;
        private readonly List<Lease> active = new();
        private readonly Dictionary<BodyVisualKind, Group> groups = new();
        public int ActiveCount => active.Count;
        public int RenderedCount => groups.Count;

        private sealed class Group
        {
            public BodyVisual Visual;
            public BodyVisualSettings Settings;
            public Shader Shader;
            public float StartedAt;
            public float Presence, UpdatedAt;
            public Func<float> Clock;
            public bool Retiring;
            public Action ReleaseResources;

            public void Dispose()
            {
                Visual.Dispose();
                var release = ReleaseResources;
                ReleaseResources = null;
                release?.Invoke();
            }
        }

        public sealed class Lease : IDisposable
        {
            private BodyVisualHost owner;
            private bool disposed;
            internal BodyVisual Visual;
            internal readonly BodyVisualSettings Settings;
            internal readonly Shader Shader;
            internal readonly float StartedAt;
            internal readonly float Duration;
            internal readonly Func<float> Clock;
            internal readonly bool HasClockOverride;
            internal readonly Func<Action> RetainResources;
            public bool IsActive => owner != null && owner.active.Contains(this);
            public event Action Ended;

            internal Lease(BodyVisualHost owner, BodyVisualSettings settings, Shader shader, float startedAt, float duration,
                Func<Action> retainResources, Func<float> clock)
            {
                this.owner = owner;
                Settings = settings.Copy();
                Shader = shader;
                StartedAt = startedAt;
                Duration = duration;
                HasClockOverride = clock != null;
                Clock = clock ?? owner.clock;
                RetainResources = retainResources;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                var previous = owner;
                owner = null;
                if (previous != null) previous.Remove(this);
                var ended = Ended;
                Ended = null;
                ended?.Invoke();
            }
        }

        public Lease Play(BodyVisualSettings settings, float duration = 0f,
            Func<Action> retainResources = null, Func<float> clockOverride = null)
        {
            if (!IsAlive || !body() || settings == null || !settings.IsValid ||
                float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f || clearing || active.Count >= MaximumConcurrent) return null;
            var selectedShader = settings.AppearanceTemplate ? settings.AppearanceTemplate.shader :
                (int)settings.Kind >= (int)BodyVisualKind.Poisoned ? resources.Surface : resources.Body;
            if (!selectedShader || !selectedShader.isSupported || selectedShader.FindPropertyIndex("_BodyClock") < 0 ||
                selectedShader.FindPropertyIndex("_Mode") < 0) return null;
            var lease = new Lease(this, settings, selectedShader, (clockOverride ?? clock)(), duration, retainResources, clockOverride);
            active.Add(lease);
            if (!Refresh(settings.Kind, settings.Kind == BodyVisualKind.ShadowPulse)) { lease.Dispose(); return null; }
            Render();
            return lease.IsActive ? lease : null;
        }
        public void Advance()
        {
            if (!IsAlive || !body()) { Clear(); return; }
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (i >= active.Count) continue;
                var lease = active[i];
                float elapsed = Mathf.Max(0f, lease.Clock() - lease.StartedAt);
                bool singlePulseEnded = lease.Settings.Kind == BodyVisualKind.ShadowPulse && !lease.Settings.Repeat && elapsed >= lease.Settings.PulseDuration;
                if (singlePulseEnded || lease.Duration > 0f && elapsed >= lease.Duration) lease.Dispose();
            }
            Render();
        }

        private void Remove(Lease lease)
        {
            if (!active.Remove(lease)) return;
            if (!clearing && !Refresh(lease.Settings.Kind)) ClearKind(lease.Settings.Kind);
            if (!clearing) Render();
        }

        private bool Refresh(BodyVisualKind kind, bool restartPulse = false)
        {
            groups.TryGetValue(kind, out var group);
            if (IncludesStatus(kind) && !statusEffectsVisible)
            {
                // Hide immediately on the next render without ending a Buff/Ability's logical source.
                if (group != null) { groups.Remove(kind); group.Dispose(); }
                foreach (var lease in active)
                    if (lease.Settings.Kind == kind) lease.Visual = null;
                return true;
            }
            if (group != null) AdvancePresence(group);
            Lease selected = null;
            Lease independentClockSource = null;
            bool includeWeapons = false, smallParticles = false;
            foreach (var lease in active)
            {
                if (lease.Settings.Kind != kind) continue;
                includeWeapons |= lease.Settings.IncludeWeapons;
                smallParticles |= lease.Settings.SmallParticles;
                if (lease.HasClockOverride) independentClockSource = lease;
                // Highest intensity wins; equal intensity uses the most recently installed source.
                if (selected == null || lease.Settings.Intensity >= selected.Settings.Intensity) selected = lease;
            }
            if (selected == null)
            {
                if (group != null)
                {
                    if (kind == BodyVisualKind.ShadowPulse) { groups.Remove(kind); group.Dispose(); }
                    else group.Retiring = true;
                }
                return true;
            }
            var merged = selected.Settings.Copy();
            merged.IncludeWeapons = includeWeapons;
            merged.SmallParticles = smallParticles;
            var clock = (independentClockSource ?? selected).Clock;
            float now = clock();
            float elapsed = group == null || restartPulse ? 0f : Mathf.Max(0f, group.Clock() - group.StartedAt);
            float start = now - elapsed;
            if (group == null || restartPulse || group.Shader != selected.Shader || !SameAppearance(group.Settings, merged))
            {
                var visual = new BodyVisual(origin, sourceFactory(merged.IncludeWeapons), selected.Shader, merged, resources: resources);
                if (!visual.Update(elapsed, body()))
                {
                    visual.Dispose();
                    return false;
                }
                var retained = selected.RetainResources?.Invoke();
                float presence = group?.Presence ?? (kind == BodyVisualKind.ShadowPulse ? 1f : 0f);
                group?.Dispose();
                group = new Group { Visual = visual, Settings = merged, Shader = selected.Shader, StartedAt = start,
                    Presence = presence, UpdatedAt = now, Clock = clock, ReleaseResources = retained };
                groups[kind] = group;
            }
            // Preserve elapsed presentation when sources with different clocks share one appearance.
            group.Clock = clock;
            group.StartedAt = start;
            group.UpdatedAt = now;
            group.Retiring = false;
            foreach (var lease in active)
                if (lease.Settings.Kind == kind) lease.Visual = group.Visual;
            return true;
        }

        private void AdvancePresence(Group group)
        {
            float now = group.Clock();
            float delta = Mathf.Max(0f, now - group.UpdatedAt);
            group.UpdatedAt = now;
            // Presentation cadence belongs to each kind, not to another row of authoring fields.
            float duration = group.Settings.Kind switch
            {
                BodyVisualKind.Frozen => group.Retiring ? .42f : .65f,
                BodyVisualKind.Burning => group.Retiring ? .30f : .28f,
                BodyVisualKind.Poisoned => group.Retiring ? .48f : .5f,
                BodyVisualKind.Electrified => group.Retiring ? .18f : .12f,
                BodyVisualKind.Bleeding => group.Retiring ? .2f : .16f,
                BodyVisualKind.Weakened => group.Retiring ? .2f : .2f,
                BodyVisualKind.Resonating => group.Retiring ? .28f : .24f,
                BodyVisualKind.FrozenSolid => group.Retiring ? .48f : .65f,
                _ => .24f
            };
            group.Presence = Mathf.MoveTowards(group.Presence, group.Retiring ? 0f : 1f, delta / duration);
        }

        private void Render()
        {
            if (!IsAlive || !body()) { Clear(); return; }
            if (statusVisualsEnabled != statusEffectsVisible)
            {
                statusVisualsEnabled = statusEffectsVisible;
                for (int i = 0; i <= (int)BodyVisualKind.FrozenSolid; i++)
                {
                    var kind = (BodyVisualKind)i;
                    if (IncludesStatus(kind) && !Refresh(kind)) ClearKind(kind);
                }
            }
            int speckGroups = 0;
            foreach (var pair in groups)
            {
                AdvancePresence(pair.Value);
                if (pair.Value.Settings.HasSmallParticles) speckGroups++;
            }
            // Frost and solid ice share the coat; spatial accents keep their identity at any stack count.
            float ice = groups.TryGetValue(BodyVisualKind.FrozenSolid, out var solid) ? Mathf.SmoothStep(0f, 1f, solid.Presence) : 0f;
            bool hasIce = groups.ContainsKey(BodyVisualKind.Frozen) || solid != null;
            float coatOpacity = hasIce && groups.ContainsKey(BodyVisualKind.Glow) ? 1f - Mathf.Sqrt(.32f) : 1f;
            // Enumerate stable kinds, since an unsupported source can remove a group during this pass.
            for (int i = 0; i <= (int)BodyVisualKind.FrozenSolid; i++)
            {
                var kind = (BodyVisualKind)i;
                if (!groups.TryGetValue(kind, out var group)) continue;
                bool pulse = kind == BodyVisualKind.ShadowPulse;
                group.Visual.SetPalette(palette);
                group.Visual.SetPresence(pulse ? 1f : group.Presence, group.Retiring);
                float opacity = group.Settings.IsIce || kind == BodyVisualKind.Glow ? coatOpacity : 1f;
                if (kind == BodyVisualKind.Frozen) opacity *= 1f - ice;
                group.Visual.SetComposition(opacity,
                    groups.Count > 1 && !pulse ? SurfaceOrder(kind) * 4 : 0,
                    speckGroups > 0 ? SmallParticleBudget / speckGroups : SmallParticleBudget);
                if (!group.Visual.Update(Mathf.Max(0f, group.Clock() - group.StartedAt), body())) ClearKind(kind);
                else if (group.Retiring && group.Presence <= 0f && !group.Visual.HasSmallParticles)
                {
                    groups.Remove(kind);
                    group.Dispose();
                }
            }
        }

        private static int SurfaceOrder(BodyVisualKind kind) => kind switch
        {
            BodyVisualKind.Frozen => 0,
            BodyVisualKind.FrozenSolid => 1,
            BodyVisualKind.Poisoned => 2,
            BodyVisualKind.Burning => 3,
            BodyVisualKind.Bleeding => 4,
            BodyVisualKind.Glow => 5,
            BodyVisualKind.Electrified => 6,
            BodyVisualKind.Weakened => 7,
            BodyVisualKind.Resonating => 0,
            _ => 0
        };

        private static bool SameAppearance(BodyVisualSettings a, BodyVisualSettings b) =>
            a.Kind == b.Kind && a.AppearanceTemplate == b.AppearanceTemplate && a.Color == b.Color &&
            a.Intensity == b.Intensity && a.IncludeWeapons == b.IncludeWeapons && a.SmallParticles == b.SmallParticles &&
            a.PulseInterval == b.PulseInterval && a.PulseDuration == b.PulseDuration && a.Expansion == b.Expansion &&
            a.Repeat == b.Repeat && a.PatternScale == b.PatternScale && a.MotionSpeed == b.MotionSpeed;

        private void ClearKind(BodyVisualKind kind)
        {
            // Detach the whole kind before notifying clients. A callback may dispose another kind
            // or install a new source; neither must be mistaken for one of these retired leases.
            var retired = new List<Lease>();
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i].Settings.Kind == kind) { retired.Add(active[i]); active.RemoveAt(i); }
            if (groups.TryGetValue(kind, out var group)) { groups.Remove(kind); group.Dispose(); }
            foreach (var lease in retired) lease.Dispose();
        }

        public void Clear()
        {
            if (clearing) return;
            clearing = true;
            try
            {
                while (active.Count > 0) active[active.Count - 1].Dispose();
                foreach (var group in groups.Values) group.Dispose();
                groups.Clear();
            }
            finally { clearing = false; }
        }

        private static bool IncludesStatus(BodyVisualKind kind) => kind == BodyVisualKind.Frozen ||
            kind == BodyVisualKind.FrozenSolid || kind == BodyVisualKind.Burning || kind == BodyVisualKind.Poisoned ||
            kind == BodyVisualKind.Bleeding || kind == BodyVisualKind.Electrified;
        public void Dispose() { if (disposed) return; Clear(); disposed = true; }
    }
}
