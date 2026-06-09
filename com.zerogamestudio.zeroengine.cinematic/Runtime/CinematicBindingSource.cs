using UnityEngine;

namespace ZeroEngine.Cinematic
{
    public sealed class CinematicBindingSource : MonoBehaviour
    {
        [SerializeField] private CinematicBindingRegistryBehaviour _registry;
        [SerializeField] private string _bindingKey;
        [SerializeField] private Object _binding;

        private Object Binding => _binding != null ? _binding : gameObject;

        public void SetRegistry(CinematicBindingRegistryBehaviour registry)
        {
            if (isActiveAndEnabled)
            {
                _registry?.Unregister(_bindingKey, Binding);
            }

            _registry = registry;
            RegisterIfActive();
        }

        private void OnEnable()
        {
            if (_registry == null)
            {
                _registry = FindFirstObjectByType<CinematicBindingRegistryBehaviour>(FindObjectsInactive.Include);
            }

            RegisterIfActive();
        }

        private void OnDisable()
        {
            _registry?.Unregister(_bindingKey, Binding);
        }

        private void RegisterIfActive()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            _registry?.Register(_bindingKey, Binding);
        }
    }
}
