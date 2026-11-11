using UnityEngine;
namespace ZeroEngine.Render.Urp
{
    public static class BloodVisualResourcesLoader
    {
        public static BloodVisualResources Load(ParticleSystem authoredSpray=null) => new(
            Resources.Load<Shader>("ZeroEngine/Blood/SurfaceBlood"),
            Resources.Load<Shader>("ZeroEngine/Blood/SpatialBlood"),authoredSpray);
    }
}
