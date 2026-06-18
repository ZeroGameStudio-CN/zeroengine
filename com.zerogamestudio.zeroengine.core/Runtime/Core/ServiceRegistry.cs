using System;
using System.Collections.Generic;

#nullable enable

namespace ZeroEngine.Core
{
    public static class ServiceRegistry
    {
        private static readonly Dictionary<Type, object> Services = new();
        private static readonly object SyncRoot = new();

        public static void Register<TService>(TService service) where TService : class
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            lock (SyncRoot)
            {
                Services[typeof(TService)] = service;
            }
        }

        public static bool TryResolve<TService>(out TService? service) where TService : class
        {
            lock (SyncRoot)
            {
                if (Services.TryGetValue(typeof(TService), out var registered))
                {
                    service = registered as TService;
                    return service != null;
                }
            }

            service = null;
            return false;
        }

        public static TService? ResolveOrNull<TService>() where TService : class
        {
            return TryResolve<TService>(out var service) ? service : null;
        }

        public static bool Unregister<TService>(TService? service = null) where TService : class
        {
            lock (SyncRoot)
            {
                var serviceType = typeof(TService);
                if (!Services.TryGetValue(serviceType, out var registered))
                {
                    return false;
                }

                if (service != null && !ReferenceEquals(registered, service))
                {
                    return false;
                }

                Services.Remove(serviceType);
                return true;
            }
        }

        public static IDisposable OverrideForTests<TService>(TService service) where TService : class
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            lock (SyncRoot)
            {
                var serviceType = typeof(TService);
                var hadPrevious = Services.TryGetValue(serviceType, out var previous);
                Services[serviceType] = service;
                return new OverrideScope(serviceType, hadPrevious, hadPrevious ? previous! : null!);
            }
        }

        public static void ClearForTests()
        {
            lock (SyncRoot)
            {
                Services.Clear();
            }
        }

        private sealed class OverrideScope : IDisposable
        {
            private readonly Type _serviceType;
            private readonly bool _hadPrevious;
            private readonly object _previous;
            private bool _disposed;

            public OverrideScope(Type serviceType, bool hadPrevious, object previous)
            {
                _serviceType = serviceType;
                _hadPrevious = hadPrevious;
                _previous = previous;
            }

            public void Dispose()
            {
                lock (SyncRoot)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    if (_hadPrevious)
                    {
                        Services[_serviceType] = _previous;
                    }
                    else
                    {
                        Services.Remove(_serviceType);
                    }

                    _disposed = true;
                }
            }
        }
    }
}
