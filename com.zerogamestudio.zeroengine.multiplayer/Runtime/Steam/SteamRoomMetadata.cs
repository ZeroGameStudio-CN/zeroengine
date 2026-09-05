using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZeroEngine.Multiplayer.Steam
{
    public readonly struct SteamRoomDescriptor
    {
        public SteamRoomDescriptor(
            string productId,
            string protocolVersion,
            string gameProtocolVersion,
            string contentRevision,
            string buildVersion,
            string gameRoomId,
            SessionId sessionId,
            long sessionGeneration,
            SessionPhase phase,
            PlatformUserId hostId,
            RoomVisibility visibility,
            bool isJoinable,
            bool hasStarted)
        {
            ProductId = productId;
            ProtocolVersion = protocolVersion;
            GameProtocolVersion = gameProtocolVersion;
            ContentRevision = contentRevision;
            BuildVersion = buildVersion;
            GameRoomId = gameRoomId;
            SessionId = sessionId;
            SessionGeneration = sessionGeneration;
            Phase = phase;
            HostId = hostId;
            Visibility = visibility;
            IsJoinable = isJoinable;
            HasStarted = hasStarted;
        }

        public string ProductId { get; }
        public string ProtocolVersion { get; }
        public string GameProtocolVersion { get; }
        public string ContentRevision { get; }
        public string BuildVersion { get; }
        public string GameRoomId { get; }
        public SessionId SessionId { get; }
        public long SessionGeneration { get; }
        public SessionPhase Phase { get; }
        public PlatformUserId HostId { get; }
        public RoomVisibility Visibility { get; }
        public bool IsJoinable { get; }
        public bool HasStarted { get; }
    }

    public static class SteamRoomMetadata
    {
        private const int MaximumValueLength = 255;

        public static IReadOnlyDictionary<string, string> Create(
            string prefix,
            RoomCreateOptions options,
            SessionId sessionId,
            long sessionGeneration,
            PlatformUserId hostId)
        {
            string normalizedPrefix = NormalizePrefix(prefix);
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [normalizedPrefix + "product"] = options.Compatibility.ProductId,
                [normalizedPrefix + "protocol"] = options.ProtocolVersion,
                [normalizedPrefix + "game_protocol"] = options.Compatibility.GameProtocolVersion,
                [normalizedPrefix + "content"] = options.Compatibility.ContentRevision,
                [normalizedPrefix + "build"] = options.Compatibility.BuildVersion,
                [normalizedPrefix + "room"] = options.Compatibility.GameRoomId,
                [normalizedPrefix + "session"] = sessionId.Value,
                [normalizedPrefix + "generation"] = sessionGeneration.ToString(CultureInfo.InvariantCulture),
                [normalizedPrefix + "state"] = "waiting",
                [normalizedPrefix + "host"] = hostId.Value,
                [normalizedPrefix + "visibility"] = VisibilityToString(options.Visibility),
                [normalizedPrefix + "joinable"] = "1"
            };
            return values;
        }

        public static OperationResult<SteamRoomDescriptor> Read(
            string prefix,
            Func<string, string> readValue)
        {
            if (readValue == null)
            {
                throw new ArgumentNullException(nameof(readValue));
            }

            string normalizedPrefix = NormalizePrefix(prefix);
            string product = ReadRequired(normalizedPrefix, "product", readValue);
            string protocol = ReadRequired(normalizedPrefix, "protocol", readValue);
            string gameProtocol = ReadRequired(normalizedPrefix, "game_protocol", readValue);
            string content = ReadRequired(normalizedPrefix, "content", readValue);
            string build = ReadRequired(normalizedPrefix, "build", readValue);
            string room = ReadRequired(normalizedPrefix, "room", readValue);
            string session = ReadRequired(normalizedPrefix, "session", readValue);
            string generationValue = ReadRequired(normalizedPrefix, "generation", readValue);
            string state = ReadRequired(normalizedPrefix, "state", readValue);
            string host = ReadRequired(normalizedPrefix, "host", readValue);
            string visibility = ReadRequired(normalizedPrefix, "visibility", readValue);
            string joinableValue = ReadRequired(normalizedPrefix, "joinable", readValue);

            if (string.IsNullOrWhiteSpace(product) || string.IsNullOrWhiteSpace(protocol) ||
                string.IsNullOrWhiteSpace(gameProtocol) || string.IsNullOrWhiteSpace(content) ||
                string.IsNullOrWhiteSpace(build) || string.IsNullOrWhiteSpace(room) ||
                string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(host) ||
                (joinableValue != "0" && joinableValue != "1"))
            {
                return OperationResult<SteamRoomDescriptor>.Failure(
                    MultiplayerErrorCode.JoinFailed,
                    "multiplayer.steam.metadata_missing");
            }

            long generation;
            if (!long.TryParse(generationValue, NumberStyles.None, CultureInfo.InvariantCulture, out generation) || generation < 1)
            {
                return OperationResult<SteamRoomDescriptor>.Failure(
                    MultiplayerErrorCode.SessionMismatch,
                    "multiplayer.steam.metadata_generation_invalid");
            }

            SessionPhase phase;
            bool started;
            bool stateJoinable;
            if (!TryReadState(state, out phase, out started, out stateJoinable))
            {
                return OperationResult<SteamRoomDescriptor>.Failure(
                    MultiplayerErrorCode.JoinFailed,
                    "multiplayer.steam.metadata_state_invalid",
                    state);
            }

            RoomVisibility roomVisibility;
            if (!TryReadVisibility(visibility, out roomVisibility))
            {
                return OperationResult<SteamRoomDescriptor>.Failure(
                    MultiplayerErrorCode.JoinFailed,
                    "multiplayer.steam.metadata_visibility_invalid",
                    visibility);
            }

            bool explicitJoinable = joinableValue == "1";
            SteamRoomDescriptor descriptor = new SteamRoomDescriptor(
                product,
                protocol,
                gameProtocol,
                content,
                build,
                room,
                new SessionId(session),
                generation,
                phase,
                new PlatformUserId(host),
                roomVisibility,
                stateJoinable && explicitJoinable,
                started);
            return OperationResult<SteamRoomDescriptor>.Success(descriptor);
        }

        public static string NormalizePrefix(string prefix)
        {
            string value = string.IsNullOrWhiteSpace(prefix) ? "ze_" : prefix.Trim();
            return value.EndsWith("_", StringComparison.Ordinal) ? value : value + "_";
        }

        public static OperationResult ValidateValues(IReadOnlyDictionary<string, string> values)
        {
            if (values == null || values.Count == 0)
            {
                return OperationResult.Failure(
                    MultiplayerErrorCode.InvalidConfiguration,
                    "multiplayer.steam.metadata_empty");
            }

            foreach (KeyValuePair<string, string> pair in values)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null || pair.Value.Length > MaximumValueLength)
                {
                    return OperationResult.Failure(
                        MultiplayerErrorCode.InvalidConfiguration,
                        "multiplayer.steam.metadata_value_invalid",
                        pair.Key ?? string.Empty);
                }
            }

            return OperationResult.Success();
        }

        private static string ReadRequired(string prefix, string key, Func<string, string> readValue)
        {
            return readValue(prefix + key) ?? string.Empty;
        }

        private static string VisibilityToString(RoomVisibility visibility)
        {
            if (visibility == RoomVisibility.Public)
            {
                return "public";
            }

            return visibility == RoomVisibility.FriendsOnly ? "friends" : "private";
        }

        private static bool TryReadVisibility(string value, out RoomVisibility visibility)
        {
            if (string.Equals(value, "public", StringComparison.Ordinal))
            {
                visibility = RoomVisibility.Public;
                return true;
            }

            if (string.Equals(value, "friends", StringComparison.Ordinal))
            {
                visibility = RoomVisibility.FriendsOnly;
                return true;
            }

            if (string.Equals(value, "private", StringComparison.Ordinal))
            {
                visibility = RoomVisibility.Private;
                return true;
            }

            visibility = RoomVisibility.Private;
            return false;
        }

        private static bool TryReadState(
            string value,
            out SessionPhase phase,
            out bool started,
            out bool joinable)
        {
            started = false;
            joinable = true;
            if (string.Equals(value, "waiting", StringComparison.Ordinal))
            {
                phase = SessionPhase.InRoom;
                return true;
            }

            if (string.Equals(value, "connecting", StringComparison.Ordinal))
            {
                phase = SessionPhase.Connecting;
                return true;
            }

            if (string.Equals(value, "ready", StringComparison.Ordinal))
            {
                phase = SessionPhase.Ready;
                return true;
            }

            if (string.Equals(value, "starting", StringComparison.Ordinal))
            {
                phase = SessionPhase.Starting;
                joinable = false;
                return true;
            }

            if (string.Equals(value, "ingame", StringComparison.Ordinal))
            {
                phase = SessionPhase.InGame;
                started = true;
                return true;
            }

            if (string.Equals(value, "closed", StringComparison.Ordinal))
            {
                phase = SessionPhase.Recovery;
                joinable = false;
                return true;
            }

            phase = SessionPhase.Failed;
            return false;
        }
    }
}
