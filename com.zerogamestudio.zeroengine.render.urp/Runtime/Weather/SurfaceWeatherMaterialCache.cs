using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Render.Weather;

namespace ZeroEngine.Render.Urp.Weather
{
    internal sealed class SurfaceWeatherMaterialCache : IDisposable
    {
        private sealed class Borrow { public Material Material; public int Users; }
        private readonly Dictionary<Material, Borrow> _borrowed = new();
        private readonly HashSet<Shader> _reported = new();
        private readonly List<string> _unsupported = new();
        private readonly SurfaceWeatherSceneProfileSO _profile;
        public IReadOnlyList<string> Unsupported => _unsupported;
        public int OwnedMaterials => _borrowed.Count;

        public SurfaceWeatherMaterialCache(SurfaceWeatherSceneProfileSO profile) => _profile = profile;

        public Material Acquire(Material source)
        {
            if (source == null) return source;
            if (_profile.IsNonReceiver(source)) return source;
            if (_borrowed.TryGetValue(source, out Borrow existing)) { existing.Users++; return existing.Material; }
            bool native = source.HasProperty("_ZeroSceneWeatherReceiver");
            Shader shader = native ? source.shader : source.shader.name == "Universal Render Pipeline/Lit" ? _profile.LitShader : null;
            if (shader == null)
            {
                ReportUnsupported(source);
                return source;
            }
            var material = new Material(source) { shader = shader, hideFlags = HideFlags.DontSave };
            material.renderQueue = source.renderQueue;
            material.SetFloat("_ZeroSceneWeatherReceiver", 1f);
            SurfaceWeatherMaterialSources.Register(material, source);
            _borrowed.Add(source, new Borrow { Material = material, Users = 1 });
            return material;
        }

        public void ReportUnsupported(Material source)
        {
            if (source != null && _reported.Add(source.shader))
                _unsupported.Add($"{source.name} ({source.shader.name})");
        }

        public void Release(Material source)
        {
            if (ReferenceEquals(source, null) || !_borrowed.TryGetValue(source, out Borrow borrow) || --borrow.Users > 0) return;
            SurfaceWeatherMaterialSources.Unregister(borrow.Material);
            DestroyOwned(borrow.Material);
            _borrowed.Remove(source);
        }

        internal static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        public void Dispose()
        {
            foreach (Borrow borrow in _borrowed.Values)
            { SurfaceWeatherMaterialSources.Unregister(borrow.Material); DestroyOwned(borrow.Material); }
            _borrowed.Clear();
        }
    }
}
