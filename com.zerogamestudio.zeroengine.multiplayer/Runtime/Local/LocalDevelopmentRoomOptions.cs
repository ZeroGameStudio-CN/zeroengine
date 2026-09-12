using System;

namespace ZeroEngine.Multiplayer.Local
{
    public enum LocalMultiplayerRole
    {
        None,
        Host,
        Client
    }

    public sealed class LocalDevelopmentRoomOptions
    {
        public LocalDevelopmentRoomOptions(
            LocalMultiplayerRole role,
            string address,
            ushort port,
            RoomId roomId,
            SessionId sessionId,
            long sessionGeneration,
            PlatformUser localUser,
            PlatformUser hostUser,
            PlatformUser expectedRemoteUser,
            CompatibilityDescriptor compatibility,
            string protocolVersion,
            int maxMembers,
            RoomVisibility visibility,
            bool exitOnReady = false)
        {
            Role = role;
            Address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            Port = port;
            RoomId = roomId;
            SessionId = sessionId;
            SessionGeneration = sessionGeneration;
            LocalUser = localUser;
            HostUser = hostUser;
            ExpectedRemoteUser = expectedRemoteUser;
            Compatibility = compatibility;
            ProtocolVersion = protocolVersion ?? string.Empty;
            MaxMembers = maxMembers;
            Visibility = visibility;
            ExitOnReady = exitOnReady;
        }

        public LocalMultiplayerRole Role { get; }
        public string Address { get; }
        public ushort Port { get; }
        public RoomId RoomId { get; }
        public SessionId SessionId { get; }
        public long SessionGeneration { get; }
        public PlatformUser LocalUser { get; }
        public PlatformUser HostUser { get; }
        public PlatformUser ExpectedRemoteUser { get; }
        public CompatibilityDescriptor Compatibility { get; }
        public string ProtocolVersion { get; }
        public int MaxMembers { get; }
        public RoomVisibility Visibility { get; }
        public bool ExitOnReady { get; }

        public OperationResult Validate()
        {
            if (Role == LocalMultiplayerRole.None)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.local.role_missing");
            }

            if (Port == 0 || RoomId.IsEmpty || SessionId.IsEmpty || SessionGeneration < 1)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.local.session_descriptor_invalid");
            }

            if (LocalUser.Id.IsEmpty || HostUser.Id.IsEmpty || string.IsNullOrWhiteSpace(Address))
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.local.identity_or_address_missing");
            }

            if (MaxMembers < 1 || string.IsNullOrWhiteSpace(ProtocolVersion))
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.local.room_descriptor_invalid");
            }

            return CompatibilityValidator.ValidateDescriptor(Compatibility, ProtocolVersion);
        }
    }
}
