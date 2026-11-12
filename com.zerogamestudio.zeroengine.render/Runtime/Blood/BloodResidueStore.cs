using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace ZeroEngine.Render
{
    // Room-owned settled cosmetic meshes. No particle lease, physics body, collider,
    // per-piece Update or saved state survives the short-lived death emitter.
    [DisallowMultipleComponent]
    public sealed class BloodResidueStore : MonoBehaviour
    {
        public const int Limit = 60;
        private const float RetirementSeconds = 1.4f;
        private static readonly ProfilerMarker Marker = new(ProfilerCategory.Scripts, "ZeroEngine.DeathBlood.Residue");
        private BloodVisualContext context;
        private readonly Queue<Entry> retained = new();
        private readonly List<Entry> retiring = new();
        private readonly Stack<Pair> pool = new();
        private MaterialPropertyBlock properties;
        private int ownerTransition;
        private int recycled;
        public static int RetainedCount(BloodVisualContext value) => value.Residue ? value.Residue.retained.Count : 0;
        public static int RetiringCount(BloodVisualContext value) => value.Residue ? value.Residue.retiring.Count : 0;
        public static int PooledCount(BloodVisualContext value) => value.Residue ? value.Residue.pool.Count : 0;
        public static int RecycledCount(BloodVisualContext value) => value.Residue ? value.Residue.recycled : 0;

        private struct Pair
        {
            public Transform Body, Rim;
            public MeshRenderer BodyRenderer, RimRenderer;
        }
        private sealed class Entry
        {
            public Pair Visual;
            public Color RawTint, Shadow;
            public Vector3 Position;
            public bool Bone;
            public float Age, Sink;
        }

        public static bool TryTake(BloodVisualContext context, out Transform body, out MeshRenderer renderer, out Transform rim, out MeshRenderer rimRenderer)
        {
            body = rim = null; renderer = rimRenderer = null;
            var current=context.Residue;
            if (!current) return false;
            while (current.pool.Count > 0)
            {
                var pair = current.pool.Pop();
                if (!pair.Body || !pair.Rim) continue;
                body = pair.Body; renderer = pair.BodyRenderer; rim = pair.Rim; rimRenderer = pair.RimRenderer;
                return true;
            }
            return false;
        }

        public static bool Retain(BloodVisualContext context, Transform body, MeshRenderer renderer, Transform rim, MeshRenderer rimRenderer, Color rawTint, bool bone)
        {
            if (!body || !rim || !context.WorldEnabled || !context.DebrisEnabled || !context.RetainResidue) return false;
            var current=context.Residue;
            if (!current)
            {
                var root = new GameObject("Settled death flesh and bone") { hideFlags = HideFlags.DontSave };
                current = root.AddComponent<BloodResidueStore>();
                current.properties??=new MaterialPropertyBlock();
                current.context=context;context.Residue=current;
                current.ownerTransition=context.ScopeVersion;context.PaletteChanged+=current.PaletteChanged;
                if(context.ResidueParent)root.transform.SetParent(context.ResidueParent,true);
            }
            body.SetParent(current.transform, true); rim.SetParent(current.transform, true);
            rimRenderer.GetPropertyBlock(current.properties);
            var entry = new Entry { Visual = new Pair { Body = body, BodyRenderer = renderer, Rim = rim, RimRenderer = rimRenderer },
                RawTint = rawTint, Bone = bone, Position = body.position,
                Sink = Mathf.Max(.06f, renderer.bounds.size.y * 1.15f), Shadow = current.properties.GetColor("_BaseColor") };
            current.retained.Enqueue(entry);
            while (current.retained.Count > Limit)
            {
                current.retiring.Add(current.retained.Dequeue());
                // Normal admission allows at most sixty arriving pieces in one burst window.
                // Keep even externally forced preview churn bounded.
                if (current.retiring.Count > Limit)
                { current.Recycle(current.retiring[0]); current.retiring.RemoveAt(0); }
            }
            return true;
        }

        public static void Clear(BloodVisualContext context)
        {
            var current=context.Residue;
            if (!current) { context.Residue = null; return; }
            var root = current.gameObject;
            context.Residue = null;
            root.SetActive(false);
            if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
        }

        private void Awake() => properties = new MaterialPropertyBlock();
        private void OnDisable(){if(context!=null)context.PaletteChanged-=PaletteChanged;}
        private void OnDestroy(){if(context!=null && context.Residue==this)context.Residue=null;}

        private void Update()
        {
            using var scope = Marker.Auto();
            if(context==null)return;
            if (!context.WorldEnabled || ownerTransition != context.ScopeVersion) { Clear(context); return; }
            float delta = context.DeltaTime();
            if (delta <= 0f || retiring.Count == 0) return;
            for (int i = retiring.Count - 1; i >= 0; i--)
            {
                var entry = retiring[i]; entry.Age += delta;
                if (entry.Age >= RetirementSeconds || !entry.Visual.Body)
                { Recycle(entry); retiring.RemoveAt(i); continue; }
                Apply(entry);
            }
        }

        private void PaletteChanged()
        {
            foreach (var entry in retained) Apply(entry);
            foreach (var entry in retiring) Apply(entry);
        }

        private void Apply(Entry entry)
        {
            if (!entry.Visual.Body) return;
            float t = Mathf.Clamp01(entry.Age / RetirementSeconds);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.25f, 1f, t));
            entry.Visual.Body.position = entry.Position + Vector3.down * (entry.Sink * Mathf.SmoothStep(0f, 1f, t));
            var color = entry.Bone ? entry.RawTint : context.RemapBlood(entry.RawTint);
            color.a = fade;
            entry.Visual.BodyRenderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", color); entry.Visual.BodyRenderer.SetPropertyBlock(properties);
            var shadow = entry.Shadow; shadow.a *= fade;
            entry.Visual.RimRenderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", shadow); entry.Visual.RimRenderer.SetPropertyBlock(properties);
        }

        private void Recycle(Entry entry)
        {
            recycled++;
            var pair = entry.Visual;
            if (!pair.Body || !pair.Rim) return;
            pair.Body.gameObject.SetActive(false); pair.Rim.gameObject.SetActive(false);
            if (pool.Count < Limit) pool.Push(pair);
            else { Destroy(pair.Body.gameObject); Destroy(pair.Rim.gameObject); }
        }
    }
}
