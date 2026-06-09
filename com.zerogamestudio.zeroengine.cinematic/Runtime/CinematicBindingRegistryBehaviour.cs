using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Cinematic
{
    public sealed class CinematicBindingRegistryBehaviour : MonoBehaviour
    {
        private readonly CinematicBindingRegistry _registry = new();

        public CinematicBindingRegistry Registry => _registry;

        public void Register(string bindingKey, Object binding)
        {
            _registry.Register(bindingKey, binding);
        }

        public bool Unregister(string bindingKey, Object binding)
        {
            return _registry.Unregister(bindingKey, binding);
        }

        public bool TryResolve(string bindingKey, out Object binding)
        {
            return _registry.TryResolve(bindingKey, out binding);
        }

        public IReadOnlyList<CinematicValidationIssue> Validate()
        {
            return _registry.Validate();
        }
    }
}
