using UnityEngine;
namespace ZeroEngine.Render.Urp
{
    public static class BodyVisualResourcesLoader
    {
        public const string BodyPath = "ZeroEngine/BodyVisuals/EntityBodyVisual";
        public const string SurfacePath = "ZeroEngine/BodyVisuals/EntityBodySurface";
        public const string SpecksPath = "ZeroEngine/BodyVisuals/EntityBodySpecks";
        public static BodyVisualResources Load() => new BodyVisualResources(
            Resources.Load<Shader>(BodyPath), Resources.Load<Shader>(SurfacePath), Resources.Load<Shader>(SpecksPath));
    }
}
