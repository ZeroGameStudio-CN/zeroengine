using System;
using Steamworks;
using UnityEngine;

namespace ZeroEngine.Multiplayer.Steam
{
    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    public sealed class SteamRuntimeOwner : MonoBehaviour, ISteamRuntime
    {
        [SerializeField] private bool initializeOnAwake = true;
        [SerializeField] private bool persistAcrossScenes = true;
        [SerializeField] private bool restartAppIfNecessary = true;
        [SerializeField] private uint appId = 480;

        private static SteamRuntimeOwner _activeOwner;

        private bool _initialized;
        private bool _ownsRuntime;
        private string _unavailableReasonKey = "multiplayer.steam.not_initialized";
        private PlatformUser _localUser;

        public static SteamRuntimeOwner ActiveOwner => _activeOwner;
        public bool IsAvailable => _initialized && _ownsRuntime;
        public string UnavailableReasonKey => IsAvailable ? string.Empty : _unavailableReasonKey;
        public PlatformUser LocalUser => _localUser;
        public bool OwnsRuntime => _ownsRuntime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _activeOwner = null;
        }

        private void Awake()
        {
            if (_activeOwner != null && _activeOwner != this)
            {
                _unavailableReasonKey = "multiplayer.steam.duplicate_runtime_owner";
                enabled = false;
                Debug.LogError(
                    "[ZeroEngine.Multiplayer] A second SteamRuntimeOwner was rejected. Steam may only be initialized and pumped by one owner.",
                    this);
                return;
            }

            _activeOwner = this;
            _ownsRuntime = true;
            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }

            if (initializeOnAwake)
            {
                OperationResult result = EnsureInitialized();
                if (!result.Succeeded)
                {
                    Debug.LogWarning(
                        "[ZeroEngine.Multiplayer] Steam runtime unavailable: " + result.MessageKey,
                        this);
                }
            }
        }

        private void Update()
        {
            if (_initialized && _ownsRuntime)
            {
                SteamAPI.RunCallbacks();
            }
        }

        private void OnDestroy()
        {
            if (_activeOwner != this)
            {
                return;
            }

            if (_initialized && _ownsRuntime)
            {
                SteamAPI.Shutdown();
            }

            _initialized = false;
            _ownsRuntime = false;
            _activeOwner = null;
        }

        public OperationResult EnsureInitialized()
        {
            if (_initialized && _ownsRuntime)
            {
                return OperationResult.Success();
            }

            if (_activeOwner != this || !_ownsRuntime)
            {
                _unavailableReasonKey = "multiplayer.steam.runtime_not_owner";
                return OperationResult.Failure(
                    MultiplayerErrorCode.PlatformUnavailable,
                    _unavailableReasonKey);
            }

            try
            {
                if (!Packsize.Test() || !DllCheck.Test())
                {
                    _unavailableReasonKey = "multiplayer.steam.binary_validation_failed";
                    return OperationResult.Failure(
                        MultiplayerErrorCode.PlatformUnavailable,
                        _unavailableReasonKey);
                }

                if (!Application.isEditor && restartAppIfNecessary &&
                    SteamAPI.RestartAppIfNecessary(new AppId_t(appId)))
                {
                    _unavailableReasonKey = "multiplayer.steam.restart_requested";
                    return OperationResult.Failure(
                        MultiplayerErrorCode.PlatformUnavailable,
                        _unavailableReasonKey);
                }

                if (!SteamAPI.Init())
                {
                    _unavailableReasonKey = "multiplayer.steam.initialization_failed";
                    return OperationResult.Failure(
                        MultiplayerErrorCode.PlatformUnavailable,
                        _unavailableReasonKey);
                }

                CSteamID steamId = SteamUser.GetSteamID();
                if (!steamId.IsValid())
                {
                    SteamAPI.Shutdown();
                    _unavailableReasonKey = "multiplayer.steam.local_user_invalid";
                    return OperationResult.Failure(
                        MultiplayerErrorCode.PlatformUnavailable,
                        _unavailableReasonKey);
                }

                _initialized = true;
                _localUser = new PlatformUser(
                    new PlatformUserId(steamId.m_SteamID.ToString()),
                    SteamFriends.GetPersonaName());
                _unavailableReasonKey = string.Empty;
                return OperationResult.Success();
            }
            catch (DllNotFoundException exception)
            {
                _unavailableReasonKey = "multiplayer.steam.library_missing";
                return OperationResult.Failure(
                    MultiplayerErrorCode.PlatformUnavailable,
                    _unavailableReasonKey,
                    exception.GetType().Name);
            }
            catch (Exception exception)
            {
                _unavailableReasonKey = "multiplayer.steam.initialization_exception";
                return OperationResult.Failure(
                    MultiplayerErrorCode.PlatformUnavailable,
                    _unavailableReasonKey,
                    exception.GetType().Name);
            }
        }
    }
}
