using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ZeroEngine.Multiplayer.FishNet
{
    public sealed class MultiplayerBootstrap : MonoBehaviour
    {
        [SerializeField] private MultiplayerSessionConfig sessionConfig;
        [SerializeField] private FishNetConnectionDriver connectionDriver;

        private IPlatformRoomService _roomService;
        private IMultiplayerGameAdapter _gameAdapter;
        private bool _disposing;

        public MultiplayerSessionCoordinator Coordinator { get; private set; }
        public MultiplayerSessionConfig SessionConfig => sessionConfig;
        public FishNetConnectionDriver ConnectionDriver => connectionDriver;

        public void Configure(
            MultiplayerSessionConfig config,
            IPlatformRoomService roomService,
            FishNetConnectionDriver driver,
            IMultiplayerGameAdapter gameAdapter)
        {
            if (Coordinator != null)
            {
                throw new InvalidOperationException("MultiplayerBootstrap is already configured.");
            }

            sessionConfig = config ?? throw new ArgumentNullException(nameof(config));
            _roomService = roomService ?? throw new ArgumentNullException(nameof(roomService));
            connectionDriver = driver ?? throw new ArgumentNullException(nameof(driver));
            _gameAdapter = gameAdapter ?? throw new ArgumentNullException(nameof(gameAdapter));
            connectionDriver.ConfigureAuthorizer(_roomService as IRemoteConnectionAuthorizer);
            Coordinator = new MultiplayerSessionCoordinator(
                sessionConfig,
                _roomService,
                connectionDriver,
                _gameAdapter);
        }

        public Task<OperationResult> InitializeAsync(CancellationToken cancellationToken)
        {
            if (Coordinator == null)
            {
                return Task.FromResult(OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.bootstrap.not_configured"));
            }

            return Coordinator.InitializeAsync(cancellationToken);
        }

        private async void OnDestroy()
        {
            if (_disposing || Coordinator == null)
            {
                return;
            }

            _disposing = true;
            try
            {
                await Coordinator.DisposeAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                Coordinator = null;
            }
        }
    }
}
