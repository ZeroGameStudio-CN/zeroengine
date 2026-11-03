using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Render.Weather
{
    /// <summary>Read-only projection of a caller's world state. It owns no clock or save data.</summary>
    public sealed class SurfaceWeatherField
    {
        public const int Capacity = 32;
        private readonly List<SurfaceWeatherRegion> _regions = new(Capacity);
        public IReadOnlyList<SurfaceWeatherRegion> Regions => _regions;
        public SurfaceWeatherValue DefaultValue { get; set; }

        public void SetRegions(IReadOnlyList<SurfaceWeatherRegion> regions)
        {
            if (regions == null || regions.Count > Capacity) throw new ArgumentOutOfRangeException(nameof(regions));
            for (int i = 0; i < regions.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(regions[i].Id)) throw new ArgumentException("Uninitialized weather region.");
                for (int j = i + 1; j < regions.Count; j++)
                    if (regions[i].Id == regions[j].Id) throw new ArgumentException("Duplicate weather region.");
            }
            _regions.Clear();
            for (int i = 0; i < regions.Count; i++) _regions.Add(regions[i]);
            _regions.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        }

        public SurfaceWeatherValue Sample(Vector3 point)
        {
            float wet = 0f, snow = 0f, total = 0f;
            int priority = int.MinValue;
            foreach (SurfaceWeatherRegion region in _regions)
            {
                float weight = region.Weight(point);
                if (weight <= 0f || region.Priority < priority) continue;
                if (region.Priority > priority) { wet = snow = total = 0f; priority = region.Priority; }
                wet += region.Value.Wetness * weight;
                snow += region.Value.Snow * weight;
                total += weight;
            }
            return total > 0f ? new SurfaceWeatherValue(Mathf.Clamp01(wet / total), Mathf.Clamp01(snow / total)) : DefaultValue;
        }
    }
}
