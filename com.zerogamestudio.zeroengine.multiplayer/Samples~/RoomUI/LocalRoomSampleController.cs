using System;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using ZeroEngine.Multiplayer.FishNet;
using ZeroEngine.Multiplayer.Local;

namespace ZeroEngine.Multiplayer.Samples
{
    public sealed class LocalRoomSampleController : MonoBehaviour, IMultiplayerGameAdapter
    {
        [SerializeField] private MultiplayerSessionConfig sessionConfig;
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private Transport transport;
        [SerializeField] private FishNetIdentityBridge identityBridge;
        [SerializeField] private FishNetConnectionDriver connectionDriver;
        [SerializeField] private MultiplayerBootstrap bootstrap;
        [SerializeField] private bool autoStartFromCommandLine = true;

        private LocalDevelopmentRoomOptions _options;
        private LocalDevelopmentRoomService _roomService;
        private CancellationTokenSource _lifetime;
        private string _status = "Idle";
        private bool _readyLogged;
        private bool _starting;

        private async void Start()
        {
            ResolveReferences();
            _lifetime = new CancellationTokenSource();
            if (autoStartFromCommandLine)
            {
                await StartFromCommandLineAsync(_lifetime.Token);
            }
        }

        private void OnDestroy()
        {
            if (_lifetime != null)
            {
                _lifetime.Cancel();
                _lifetime.Dispose();
                _lifetime = null;
            }

            if (connectionDriver != null)
            {
                connectionDriver.ConnectionEvent -= OnConnectionEvent;
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20f, 20f, 520f, 220f), GUI.skin.box);
            GUILayout.Label("ZeroEngine Multiplayer — LocalDirect Sample");
            GUILayout.Label("Status: " + _status);
            if (_options != null)
            {
                GUILayout.Label("Role: " + _options.Role);
                GUILayout.Label("Room: " + _options.RoomId.Value + "  " + _options.Address + ":" + _options.Port);
            }

            if (bootstrap != null && bootstrap.Coordinator != null)
            {
                GUILayout.Label("Phase: " + bootstrap.Coordinator.Phase);
                if (GUILayout.Button("Leave"))
                {
                    _ = bootstrap.Coordinator.LeaveAsync(CancellationToken.None);
                }
            }
            GUILayout.EndArea();
        }

        public CompatibilityDescriptor GetCompatibility()
        {
            return _options == null
                ? FallbackCompatibility()
                : _options.Compatibility;
        }

        public Task<OperationResult> PrepareSessionAsync(
            MultiplayerSessionContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> SynchronizePeerAsync(
            MultiplayerPeer peer,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public Task<OperationResult> RestorePeerAsync(
            MultiplayerPeer peer,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(OperationResult.Success());
        }

        public void OnPeerDisconnected(MultiplayerPeer peer, TimeSpan gracePeriod)
        {
        }

        public void OnPeerReconnectExpired(MultiplayerPeer peer)
        {
        }

        public void OnSessionEnded(SessionEndReason reason)
        {
        }

        private async Task StartFromCommandLineAsync(CancellationToken cancellationToken)
        {
            if (_starting)
            {
                return;
            }

            _starting = true;
            OperationResult<LocalDevelopmentRoomOptions> parsed = LocalMultiplayerLaunchArguments.Parse(
                Environment.GetCommandLineArgs(),
                FallbackCompatibility(),
                sessionConfig == null ? "1" : sessionConfig.ProtocolVersion,
                sessionConfig == null ? (ushort)7770 : (ushort)sessionConfig.LocalPort);
            if (!parsed.Succeeded)
            {
                _status = parsed.MessageKey;
                Debug.LogError("[ZeroEngine.Multiplayer.Sample] " + _status, this);
                return;
            }

            _options = parsed.Value;
            if (sessionConfig == null || networkManager == null || transport == null ||
                identityBridge == null || connectionDriver == null || bootstrap == null)
            {
                _status = "Sample scene references are incomplete.";
                Debug.LogError("[ZeroEngine.Multiplayer.Sample] " + _status, this);
                return;
            }

            identityBridge.Configure(TransportMode.LocalDirect, _options.ExpectedRemoteUser.Id.Value);
            connectionDriver.Configure(
                networkManager,
                transport,
                identityBridge,
                TransportMode.LocalDirect,
                _options.Address,
                _options.Port);
            OperationResult driverSetup = connectionDriver.ValidateSetup();
            if (!driverSetup.Succeeded)
            {
                _status = driverSetup.MessageKey;
                Debug.LogError("[ZeroEngine.Multiplayer.Sample] " + _status, this);
                return;
            }

            _roomService = new LocalDevelopmentRoomService(_options);
            _roomService.AttachConnectionDriver(connectionDriver);
            bootstrap.Configure(sessionConfig, _roomService, connectionDriver, this);
            connectionDriver.ConnectionEvent += OnConnectionEvent;

            OperationResult initialized = await bootstrap.InitializeAsync(cancellationToken);
            if (!initialized.Succeeded)
            {
                _status = initialized.MessageKey;
                return;
            }

            OperationResult<RoomSnapshot> room = _options.Role == LocalMultiplayerRole.Host
                ? await bootstrap.Coordinator.CreateRoomAsync(cancellationToken)
                : await bootstrap.Coordinator.JoinRoomAsync(_options.RoomId, cancellationToken);
            if (!room.Succeeded)
            {
                _status = room.MessageKey;
                Debug.LogError("[ZeroEngine.Multiplayer.Sample] " + _status, this);
                return;
            }

            if (_options.Role == LocalMultiplayerRole.Client)
            {
                OperationResult synchronized = bootstrap.Coordinator.ConfirmLocalSynchronization(
                    room.Value.SessionId,
                    room.Value.SessionGeneration);
                if (!synchronized.Succeeded)
                {
                    _status = synchronized.MessageKey;
                    return;
                }
            }

            ObserveReady();
        }

        private async void OnConnectionEvent(ConnectionEvent connectionEvent)
        {
            if (_options == null || _options.Role != LocalMultiplayerRole.Host ||
                connectionEvent.Type != ConnectionEventType.PeerConnected ||
                bootstrap == null || bootstrap.Coordinator == null)
            {
                return;
            }

            try
            {
                PlatformUser peerUser = connectionEvent.UserId == _options.ExpectedRemoteUser.Id
                    ? _options.ExpectedRemoteUser
                    : new PlatformUser(connectionEvent.UserId, connectionEvent.UserId.Value);
                OperationResult synchronized = await bootstrap.Coordinator.SynchronizePeerAsync(
                    new MultiplayerPeer(peerUser, 1),
                    _lifetime == null ? CancellationToken.None : _lifetime.Token);
                if (!synchronized.Succeeded)
                {
                    _status = synchronized.MessageKey;
                    return;
                }

                ObserveReady();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async void ObserveReady()
        {
            if (_readyLogged || bootstrap == null || bootstrap.Coordinator == null ||
                bootstrap.Coordinator.Phase != SessionPhase.Ready)
            {
                return;
            }

            _readyLogged = true;
            _status = "Ready";
            Debug.Log("ZEROENGINE_M2_READY role=" + _options.Role.ToString().ToLowerInvariant(), this);
            if (_options.ExitOnReady)
            {
                await Task.Delay(1000);
                Application.Quit(0);
            }
        }

        private void ResolveReferences()
        {
            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }

            if (networkManager != null && transport == null && networkManager.TransportManager != null)
            {
                transport = networkManager.TransportManager.Transport;
            }

            if (identityBridge == null)
            {
                identityBridge = GetComponent<FishNetIdentityBridge>();
            }

            if (connectionDriver == null)
            {
                connectionDriver = GetComponent<FishNetConnectionDriver>();
            }

            if (bootstrap == null)
            {
                bootstrap = GetComponent<MultiplayerBootstrap>();
            }
        }

        private static CompatibilityDescriptor FallbackCompatibility()
        {
            return new CompatibilityDescriptor(
                "zeroengine-sample",
                "1",
                "sample",
                "development",
                "sample-room");
        }
    }
}
