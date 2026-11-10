using UnityEngine;

namespace ZeroEngine.Activity.Unity
{
    public sealed class ActivityObserver : MonoBehaviour
    {
        private ActivityWorldDriver world;
        [Min(0)] public float InfluenceRadius;
        public void Configure(ActivityWorldDriver owner)
        {
            if (world) world.Unregister(this);
            world = owner;
            if (world && isActiveAndEnabled) world.Register(this);
        }
        private void OnEnable() { if (world) world.Register(this); }
        private void OnDisable() { if (world) world.Unregister(this); }
    }
}
