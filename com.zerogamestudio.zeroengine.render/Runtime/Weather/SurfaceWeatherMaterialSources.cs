using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Render.Weather
{
    /// <summary>Lets another presentation owner identify the source behind a borrowed weather material.</summary>
    public static class SurfaceWeatherMaterialSources
    {
        private static readonly Dictionary<Material, Material> Sources = new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Sources.Clear();
        public static Material Resolve(Material material)
        {
            for (int i = 0; i < 16 && material != null && Sources.TryGetValue(material, out Material source); i++) material = source;
            return material;
        }
        public static void Register(Material borrowed, Material source)
        {
            if (borrowed == null || borrowed == source || Sources.ContainsKey(borrowed))
                throw new ArgumentException("A borrowed material requires one distinct source.");
            Sources.Add(borrowed, source);
        }
        public static void Unregister(Material borrowed) { if (!ReferenceEquals(borrowed, null)) Sources.Remove(borrowed); }
    }
}
