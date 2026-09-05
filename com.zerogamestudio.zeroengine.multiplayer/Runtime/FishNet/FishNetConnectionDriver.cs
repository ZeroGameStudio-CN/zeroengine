using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using UnityEngine;

namespace ZeroEngine.Multiplayer.FishNet
{
    public sealed class FishNetConnectionDriver : MonoBehaviour, INetworkConnectionDriver
    {
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private Transport transport;
        [SerializeField] private Transport routedTransport;
        [SerializeField] private FishNetIdentityBridge identityBridge;
        [SerializeField] private TransportMode transportMode = TransportMode.LocalDirect;
        [SerializeField] private string localAddress = "127.0.0.1";
        [SerializeField] private ushort port = 7770;

        private readonly Dictionary<int, PlatformUserId> _remoteUsers = new Dictionary<int, PlatformUserId>();
        private bool _subscribed;
        private bool _intentionalStop;
        private bool _hostLocalClientStarting;
        private bool _wasClientActive;
        private bool _wasServerActive;
        private PlatformUserId _activeHostId;
        private IRemoteConnectionAuthorizer _remoteAuthorizer;
        private int _multipassTransportIndex = -1;

        public ConnectionPhase Phase { get; private set; } = ConnectionPhase.Stopped;
        public bool IsServer => networkManager != null && networkManager.IsServerStarted;
        public bool IsClient => networkManager != null && networkManager.IsClientStarted;
        public TransportMode TransportMode => transportMode;
        public Transport ActiveTransport => transport;
        public Transport RoutedTransport => routedTransport;
        public string LocalAddress => localAddress ?? string.Empty;
        public ushort Port => port;
        public bool HasRemoteAuthorizer => _remoteAuthorizer != null;

        public event Action<ConnectionEvent> ConnectionEvent;

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(
            NetworkManager manager,
            Transport selectedTransport,
            FishNetIdentityBridge selectedIdentityBridge,
            TransportMode mode,
            string address,
            ushort selectedPort)
        {
            Configure(
                manager,
                selectedTransport,
                selectedTransport,
                selectedIdentityBridge,
                mode,
                address,
                selectedPort);
        }

        public void Configure(
            NetworkManager manager,
            Transport activeTransport,
            Transport selectedRoute,
            FishNetIdentityBridge selectedIdentityBridge,
            TransportMode mode,
            string address,
            ushort selectedPort)
        {
            Unsubscribe();
            networkManager = manager;
            transport = activeTransport;
            routedTransport = selectedRoute;
            identityBridge = selectedIdentityBridge;
            transportMode = mode;
            localAddress = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            port = selectedPort;
            ResolveReferences();
            ConfigureMultipassRoute();
            Subscribe();
        }

        public void ConfigureAuthorizer(IRemoteConnectionAuthorizer authorizer)
        {
            _remoteAuthorizer = authorizer;
        }

        public OperationResult ValidateSetup()
        {
            ResolveReferences();
            if (networkManager == null)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.fishnet.network_manager_missing");
            }

            if (transport == null || networkManager.TransportManager == null ||
                !ReferenceEquals(networkManager.TransportManager.Transport, transport))
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.TransportUnavailable,
                    "multiplayer.fishnet.active_transport_mismatch");
            }

            OperationResult route = ConfigureMultipassRoute();
            if (!route.Succeeded)
            {
                return route;
            }

            if (identityBridge == null || port == 0 || string.IsNullOrWhiteSpace(localAddress))
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.fishnet.driver_configuration_invalid");
            }

            if (identityBridge.TransportMode != transportMode)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.fishnet.identity_mode_mismatch");
            }

            return OperationResult.Success();
        }

        public async Task<OperationResult> StartHostAsync(
            HostConnectionOptions options,
            CancellationToken cancellationToken)
        {
            OperationResult setup = ValidateSetup();
            if (!setup.Succeeded)
            {
                return setup;
            }

            if (_remoteAuthorizer == null)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.fishnet.remote_authorizer_missing");
            }

            if (Phase != ConnectionPhase.Stopped && Phase != ConnectionPhase.Failed)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.Busy,
                    "multiplayer.fishnet.driver_busy");
            }

            Phase = ConnectionPhase.StartingHost;
            _intentionalStop = false;
            _remoteUsers.Clear();
            ConfigureSelectedTransport(localAddress);

            try
            {
                if (!networkManager.IsServerStarted && !StartSelectedConnection(true))
                {
                    return Fail("multiplayer.fishnet.server_start_rejected");
                }

                await WaitUntilAsync(() => networkManager.IsServerStarted, cancellationToken);
                _wasServerActive = true;

                _hostLocalClientStarting = true;
                if (!networkManager.IsClientStarted && !StartSelectedConnection(false))
                {
                    await StopStartedTransportsAsync();
                    return Fail("multiplayer.fishnet.host_client_start_rejected");
                }

                await WaitUntilAsync(() => networkManager.IsClientStarted, cancellationToken);
                _hostLocalClientStarting = false;
                _wasClientActive = true;
                Phase = ConnectionPhase.Hosting;
                Raise(ConnectionEventType.HostStarted, default(PlatformUserId));
                return OperationResult.Success();
            }
            catch (OperationCanceledException)
            {
                _hostLocalClientStarting = false;
                await StopStartedTransportsAsync();
                Phase = ConnectionPhase.Stopped;
                throw;
            }
            catch (Exception exception)
            {
                _hostLocalClientStarting = false;
                await StopStartedTransportsAsync();
                return Fail("multiplayer.fishnet.host_start_exception", exception.GetType().Name);
            }
        }

        public async Task<OperationResult> StartClientAsync(
            ClientConnectionOptions options,
            CancellationToken cancellationToken)
        {
            OperationResult setup = ValidateSetup();
            if (!setup.Succeeded)
            {
                return setup;
            }

            if (Phase != ConnectionPhase.Stopped && Phase != ConnectionPhase.Failed)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.Busy,
                    "multiplayer.fishnet.driver_busy");
            }

            string address = string.IsNullOrWhiteSpace(options.ConnectionAddress)
                ? options.HostId.Value
                : options.ConnectionAddress;
            if (string.IsNullOrWhiteSpace(address))
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidArgument,
                    "multiplayer.fishnet.connection_address_missing");
            }

            Phase = ConnectionPhase.StartingClient;
            _intentionalStop = false;
            _activeHostId = options.HostId;
            ConfigureSelectedTransport(address);

            try
            {
                if (!networkManager.IsClientStarted && !StartSelectedConnection(false))
                {
                    return Fail("multiplayer.fishnet.client_start_rejected");
                }

                await WaitUntilAsync(() => networkManager.IsClientStarted, cancellationToken);
                _wasClientActive = true;
                Phase = ConnectionPhase.Connected;
                Raise(ConnectionEventType.ClientConnected, _activeHostId);
                return OperationResult.Success();
            }
            catch (OperationCanceledException)
            {
                await StopStartedTransportsAsync();
                Phase = ConnectionPhase.Stopped;
                throw;
            }
            catch (Exception exception)
            {
                await StopStartedTransportsAsync();
                return Fail("multiplayer.fishnet.client_start_exception", exception.GetType().Name);
            }
        }

        public async Task<OperationResult> StopAsync(
            DisconnectIntent intent,
            CancellationToken cancellationToken)
        {
            ResolveReferences();
            if (networkManager == null || transport == null)
            {
                Phase = ConnectionPhase.Stopped;
                return OperationResult.Success();
            }

            Phase = ConnectionPhase.Stopping;
            _intentionalStop = true;
            try
            {
                if (GetSelectedConnectionState(false) != LocalConnectionState.Stopped)
                {
                    StopSelectedConnection(false);
                    await WaitUntilAsync(
                        () => GetSelectedConnectionState(false) == LocalConnectionState.Stopped,
                        cancellationToken);
                }

                if (GetSelectedConnectionState(true) != LocalConnectionState.Stopped)
                {
                    StopSelectedConnection(true);
                    await WaitUntilAsync(
                        () => GetSelectedConnectionState(true) == LocalConnectionState.Stopped,
                        cancellationToken);
                }

                _remoteUsers.Clear();
                _wasClientActive = false;
                _wasServerActive = false;
                _activeHostId = default(PlatformUserId);
                Phase = ConnectionPhase.Stopped;
                Raise(ConnectionEventType.Stopped, default(PlatformUserId));
                return OperationResult.Success();
            }
            catch (OperationCanceledException)
            {
                Phase = ConnectionPhase.Failed;
                throw;
            }
            catch (Exception exception)
            {
                return Fail("multiplayer.fishnet.stop_exception", exception.GetType().Name);
            }
            finally
            {
                _intentionalStop = false;
            }
        }

        private void ResolveReferences()
        {
            if (networkManager == null)
            {
                networkManager = GetComponentInParent<NetworkManager>();
            }

            if (networkManager != null && transport == null && networkManager.TransportManager != null)
            {
                transport = networkManager.TransportManager.Transport;
            }

            if (routedTransport == null)
            {
                routedTransport = transport;
            }

            if (identityBridge == null)
            {
                identityBridge = GetComponentInParent<FishNetIdentityBridge>();
            }
        }

        private void Subscribe()
        {
            if (_subscribed || networkManager == null || transport == null)
            {
                return;
            }

            networkManager.ServerManager.OnAuthenticationResult += OnAuthenticationResult;
            networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            transport.OnClientConnectionState += OnClientConnectionState;
            transport.OnServerConnectionState += OnServerConnectionState;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            if (networkManager != null && networkManager.ServerManager != null)
            {
                networkManager.ServerManager.OnAuthenticationResult -= OnAuthenticationResult;
                networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            }

            if (transport != null)
            {
                transport.OnClientConnectionState -= OnClientConnectionState;
                transport.OnServerConnectionState -= OnServerConnectionState;
            }

            _subscribed = false;
        }

        private void OnAuthenticationResult(NetworkConnection connection, bool authenticated)
        {
            if (!authenticated || connection == null || _hostLocalClientStarting || IsLocalHostConnection(connection))
            {
                return;
            }

            OperationResult<PlatformUserId> identity = identityBridge.ResolveRemoteUser(connection, transport);
            if (!identity.Succeeded)
            {
                Raise(
                    ConnectionEventType.Failed,
                    default(PlatformUserId),
                    identity.ErrorCode,
                    identity.MessageKey);
                transport.StopConnection(connection.ClientId, true);
                return;
            }

            OperationResult authorization;
            try
            {
                authorization = _remoteAuthorizer.AuthorizeRemoteUser(identity.Value);
            }
            catch (Exception exception)
            {
                authorization = OperationResult.Failure(
                    MultiplayerErrorCode.UnauthorizedPeer,
                    "multiplayer.fishnet.authorization_exception",
                    exception.GetType().Name);
            }

            if (!authorization.Succeeded)
            {
                Raise(
                    ConnectionEventType.Failed,
                    identity.Value,
                    authorization.ErrorCode,
                    authorization.MessageKey);
                transport.StopConnection(connection.ClientId, true);
                return;
            }

            _remoteUsers[connection.ClientId] = identity.Value;
            Raise(ConnectionEventType.PeerConnected, identity.Value);
        }

        private void OnRemoteConnectionState(
            NetworkConnection connection,
            RemoteConnectionStateArgs state)
        {
            if (state.ConnectionState != RemoteConnectionState.Stopped)
            {
                return;
            }

            PlatformUserId userId;
            if (_remoteUsers.TryGetValue(state.ConnectionId, out userId))
            {
                _remoteUsers.Remove(state.ConnectionId);
                Raise(ConnectionEventType.PeerDisconnected, userId);
            }
        }

        private void OnClientConnectionState(ClientConnectionStateArgs state)
        {
            if (state.ConnectionState == LocalConnectionState.Started)
            {
                _wasClientActive = true;
                return;
            }

            if (state.ConnectionState == LocalConnectionState.Stopped &&
                _wasClientActive && !_intentionalStop)
            {
                _wasClientActive = false;
                Phase = ConnectionPhase.Failed;
                Raise(
                    ConnectionEventType.LocalDisconnected,
                    _activeHostId,
                    MultiplayerErrorCode.TransportFailed,
                    "multiplayer.fishnet.client_disconnected");
            }
        }

        private void OnServerConnectionState(ServerConnectionStateArgs state)
        {
            if (state.ConnectionState == LocalConnectionState.Started)
            {
                _wasServerActive = true;
                return;
            }

            if (state.ConnectionState == LocalConnectionState.Stopped &&
                _wasServerActive && !_intentionalStop)
            {
                _wasServerActive = false;
                Phase = ConnectionPhase.Failed;
                Raise(
                    ConnectionEventType.Failed,
                    default(PlatformUserId),
                    MultiplayerErrorCode.TransportFailed,
                    "multiplayer.fishnet.server_stopped");
            }
        }

        private bool IsLocalHostConnection(NetworkConnection connection)
        {
            return networkManager != null && networkManager.ClientManager != null &&
                   networkManager.ClientManager.Connection != null &&
                   networkManager.ClientManager.Connection.ClientId == connection.ClientId;
        }

        private async Task StopStartedTransportsAsync()
        {
            _intentionalStop = true;
            try
            {
                if (transport != null && GetSelectedConnectionState(false) != LocalConnectionState.Stopped)
                {
                    StopSelectedConnection(false);
                }

                if (transport != null && GetSelectedConnectionState(true) != LocalConnectionState.Stopped)
                {
                    StopSelectedConnection(true);
                }

                await Task.Yield();
                _wasClientActive = false;
                _wasServerActive = false;
            }
            finally
            {
                _intentionalStop = false;
            }
        }

        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
        {
            while (!condition())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        private OperationResult ConfigureMultipassRoute()
        {
            _multipassTransportIndex = -1;
            if (transport == null)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.TransportUnavailable,
                    "multiplayer.fishnet.transport_missing");
            }

            if (!(transport is Multipass multipass))
            {
                if (routedTransport == null)
                {
                    routedTransport = transport;
                }

                return ReferenceEquals(transport, routedTransport)
                    ? OperationResult.Success()
                    : OperationResult.Failure(
                        MultiplayerErrorCode.InvalidConfiguration,
                        "multiplayer.fishnet.route_requires_multipass");
            }

            if (routedTransport == null)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.fishnet.routed_transport_missing");
            }

            for (int index = 0; index < multipass.Transports.Count; index++)
            {
                if (ReferenceEquals(multipass.Transports[index], routedTransport))
                {
                    _multipassTransportIndex = index;
                    break;
                }
            }

            if (_multipassTransportIndex < 0)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.fishnet.routed_transport_not_registered");
            }

            if (multipass.GlobalServerActions)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.fishnet.multipass_global_server_actions_enabled");
            }

            multipass.SetClientTransport(_multipassTransportIndex);
            return OperationResult.Success();
        }

        private void ConfigureSelectedTransport(string address)
        {
            if (transport is Multipass multipass)
            {
                multipass.SetClientTransport(_multipassTransportIndex);
                multipass.SetPort(port, _multipassTransportIndex);
                multipass.SetClientAddress(address, _multipassTransportIndex);
                return;
            }

            transport.SetPort(port);
            transport.SetClientAddress(address);
        }

        private bool StartSelectedConnection(bool server)
        {
            return transport is Multipass multipass
                ? multipass.StartConnection(server, _multipassTransportIndex)
                : transport.StartConnection(server);
        }

        private bool StopSelectedConnection(bool server)
        {
            return transport is Multipass multipass
                ? multipass.StopConnection(server, _multipassTransportIndex)
                : transport.StopConnection(server);
        }

        private LocalConnectionState GetSelectedConnectionState(bool server)
        {
            return transport is Multipass multipass
                ? multipass.GetConnectionState(server, _multipassTransportIndex)
                : transport.GetConnectionState(server);
        }

        private OperationResult Fail(string messageKey, params string[] arguments)
        {
            Phase = ConnectionPhase.Failed;
            Raise(
                ConnectionEventType.Failed,
                default(PlatformUserId),
                MultiplayerErrorCode.TransportFailed,
                messageKey);
            return OperationResult.Failure(
                MultiplayerErrorCode.TransportFailed,
                messageKey,
                arguments);
        }

        private void Raise(
            ConnectionEventType type,
            PlatformUserId userId,
            MultiplayerErrorCode errorCode = MultiplayerErrorCode.None,
            string messageKey = "")
        {
            Action<ConnectionEvent> handler = ConnectionEvent;
            if (handler != null)
            {
                handler(new ConnectionEvent(type, userId, errorCode, messageKey));
            }
        }
    }
}
