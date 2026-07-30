using System;
using System.Threading;
using UnityEngine;
using ZeroEngine.Core;

namespace ZeroEngine.PlayerSettings
{
    /// <summary>
    /// Project bootstrap point. Consumer projects provide the catalog, store and appliers
    /// in CreateService; initialization completes before Ready becomes true.
    /// </summary>
    public abstract class SettingsBootstrap : MonoBehaviour
    {
        private CancellationTokenSource _lifetime;

        public bool Ready { get; private set; }
        public SettingsInitializeResult InitializeResult { get; private set; }

        protected abstract SettingsService CreateService();

        protected virtual async void Awake()
        {
            _lifetime = new CancellationTokenSource();
            var service = CreateService();
            if (service == null)
            {
                Debug.LogError("[SettingsBootstrap] CreateService returned null.");
                return;
            }

            InitializeResult = await service.InitializeAsync(_lifetime.Token);
            if (!InitializeResult.Success)
            {
                Debug.LogError("[SettingsBootstrap] Initialization failed.");
                return;
            }

            ServiceRegistry.Register<ISettingsService>(service);
            Ready = true;
        }

        protected virtual void OnDestroy()
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            if (ServiceRegistry.ResolveOrNull<ISettingsService>() is { } service)
            {
                ServiceRegistry.Unregister(service);
            }
        }
    }
}
