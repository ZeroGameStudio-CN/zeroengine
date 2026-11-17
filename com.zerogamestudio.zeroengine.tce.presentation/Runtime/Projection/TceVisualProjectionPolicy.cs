using System;
using UnityEngine;

namespace ZeroEngine.TCE.Presentation
{
    /// <summary>Host technology policy. Producers are removed before a display is activated.</summary>
    public sealed class TceVisualProjectionPolicy
    {
        public Func<Component, bool> SourceOnly { get; set; }
        public Func<MonoBehaviour, bool> RenderData { get; set; }
        public Func<MeshFilter, bool> GeneratedMesh { get; set; }
        public Action<GameObject, GameObject> ConfigureDisplay { get; set; }
    }
}
