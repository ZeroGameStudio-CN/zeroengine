using UnityEngine;
using ZeroEngine.Utils;

namespace ZeroEngine.Core
{
    internal static class SingletonRuntimeState
    {
        internal static bool ApplicationIsQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            ApplicationIsQuitting = false;
        }
    }

    /// <summary>
    /// Generic Singleton Base Class (Non-Persistent)
    /// </summary>
    public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T _instance;
        private static readonly object _lock = new();
        private static bool _hasWarnedOnQuit;

        public static T Instance
        {
            get
            {
                if (SingletonRuntimeState.ApplicationIsQuitting)
                {
                    if (_instance != null) return _instance;
                    if (!_hasWarnedOnQuit)
                    {
                        ZeroLog.Warning(ZeroLog.Modules.Core, $"Singleton '{typeof(T).Name}' already destroyed. Returning null.");
                        _hasWarnedOnQuit = true;
                    }
                    return null;
                }

                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = FindFirstObjectByType<T>();

                        if (_instance == null)
                        {
                            GameObject singletonObject = new GameObject();
                            _instance = singletonObject.AddComponent<T>();
                            singletonObject.name = $"[{typeof(T)}]";
                        }
                    }

                    return _instance;
                }
            }
        }

        protected virtual void Awake()
        {
            _hasWarnedOnQuit = false;
            SingletonRuntimeState.ApplicationIsQuitting = false;
            if (_instance == null)
            {
                _instance = this as T;
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        protected virtual void OnApplicationQuit()
        {
            SingletonRuntimeState.ApplicationIsQuitting = true;
        }
    }

    /// <summary>
    /// Alias for Singleton (for compatibility with common naming conventions)
    /// </summary>
    public class MonoSingleton<T> : Singleton<T> where T : MonoBehaviour { }

    /// <summary>
    /// Persistent Singleton Base Class (Cross-Scene)
    /// </summary>
    public class PersistentSingleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T _instance;
        private static readonly object _lock = new();

        public static T Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = FindFirstObjectByType<T>();

                        if (_instance == null)
                        {
                            GameObject singletonObject = new GameObject();
                            _instance = singletonObject.AddComponent<T>();
                            singletonObject.name = $"[{typeof(T)}]";
                                if (Application.isPlaying)
                                {
                                    DontDestroyOnLoad(singletonObject);
                                }
                        }
                    }

                    return _instance;
                }
            }
        }

        protected virtual void Awake()
        {
            if (_instance == null)
            {
                _instance = this as T;
                if (transform.parent != null)
                {
                    transform.SetParent(null, false);
                }

                if (Application.isPlaying)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }
    }
}
