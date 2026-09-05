using System;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

namespace ZeroEngine.Multiplayer.FishNet
{
    public sealed class FishNetIdentityBridge : MonoBehaviour
    {
        [SerializeField] private TransportMode transportMode = TransportMode.LocalDirect;
        [SerializeField] private string localDevelopmentRemoteUserId = "local-client";
        [SerializeField] private bool logUntrustedLocalIdentity = true;

        private bool _loggedUntrustedIdentity;

        public TransportMode TransportMode => transportMode;
        public bool IsPlatformAuthenticated => transportMode == TransportMode.SteamP2P;

        public void Configure(TransportMode mode, string localRemoteUserId)
        {
            transportMode = mode;
            localDevelopmentRemoteUserId = localRemoteUserId ?? string.Empty;
            _loggedUntrustedIdentity = false;
        }

        public OperationResult<PlatformUserId> ResolveRemoteUser(
            NetworkConnection connection,
            Transport transport)
        {
            if (connection == null || transport == null || connection.ClientId < 0)
            {
                return OperationResult<PlatformUserId>.Failure(
                    MultiplayerErrorCode.IdentityRejected,
                    "multiplayer.fishnet.identity_context_invalid");
            }

            string connectionAddress;
            try
            {
                connectionAddress = transport.GetConnectionAddress(connection.ClientId);
            }
            catch (Exception exception)
            {
                return OperationResult<PlatformUserId>.Failure(
                    MultiplayerErrorCode.IdentityRejected,
                    "multiplayer.fishnet.identity_address_failed",
                    exception.GetType().Name);
            }

            if (transportMode == TransportMode.SteamP2P)
            {
                ulong steamId;
                if (string.IsNullOrWhiteSpace(connectionAddress) ||
                    !ulong.TryParse(connectionAddress, out steamId) || steamId == 0)
                {
                    return OperationResult<PlatformUserId>.Failure(
                        MultiplayerErrorCode.IdentityRejected,
                        "multiplayer.fishnet.steam_identity_invalid");
                }

                return OperationResult<PlatformUserId>.Success(
                    new PlatformUserId(steamId.ToString()));
            }

            string localIdentity = string.IsNullOrWhiteSpace(localDevelopmentRemoteUserId)
                ? "local:" + (connectionAddress ?? string.Empty)
                : localDevelopmentRemoteUserId.Trim();
            if (string.IsNullOrWhiteSpace(localIdentity) || string.Equals(localIdentity, "local:", StringComparison.Ordinal))
            {
                return OperationResult<PlatformUserId>.Failure(
                    MultiplayerErrorCode.IdentityRejected,
                    "multiplayer.fishnet.local_identity_missing");
            }

            if (logUntrustedLocalIdentity && !_loggedUntrustedIdentity)
            {
                _loggedUntrustedIdentity = true;
                Debug.LogWarning(
                    "[ZeroEngine.Multiplayer] LocalDirect identity is an explicit development identity and is not platform authenticated.",
                    this);
            }

            return OperationResult<PlatformUserId>.Success(new PlatformUserId(localIdentity));
        }
    }
}
