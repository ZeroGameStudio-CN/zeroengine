using System;
using UnityEngine;
using ZeroEngine.Interaction.Core;

namespace ZeroEngine.Interaction.Unity
{
    [DisallowMultipleComponent]
    public sealed class InteractionWorldBehaviour : MonoBehaviour
    {
        private InteractionWorld runtime;
        public InteractionWorld Runtime => isActiveAndEnabled ? runtime : null;
        public event Action RuntimeChanged;
        private void OnEnable() { runtime = new InteractionWorld(); RuntimeChanged?.Invoke(); }
        private void OnDisable()
        {
            var previous = runtime;
            runtime = null;
            try { previous?.Dispose(); }
            finally { RuntimeChanged?.Invoke(); }
        }
    }
}
