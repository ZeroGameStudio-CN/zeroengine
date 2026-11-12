using UnityEngine;
namespace ZeroEngine.Render
{
    /// <summary>Optional colour remapping only. Visibility, geometry and gameplay remain caller-owned.</summary>
    public readonly struct VisualPalette
    {
        private static readonly int ModeId = Shader.PropertyToID("_VisualPaletteMode");
        private static readonly int InkId = Shader.PropertyToID("_VisualPaletteInk");
        public bool Monochrome { get; }
        public Color Ink { get; }
        public VisualPalette(bool monochrome, Color ink) { Monochrome = monochrome; Ink = ink; }
        internal void Apply(Material material)
        {
            if (!material) return;
            if (material.HasProperty(ModeId)) material.SetFloat(ModeId, Monochrome ? 1f : 0f);
            if (material.HasProperty(InkId)) material.SetVector(InkId, new Vector4(Ink.r, Ink.g, Ink.b, Ink.a));
        }
    }
}
