using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZeroEngine.Multiplayer.Local
{
    public static class LocalMultiplayerLaunchArguments
    {
        public const string RoleOption = "--ze-multiplayer-role";
        public const string AddressOption = "--ze-multiplayer-address";
        public const string PortOption = "--ze-multiplayer-port";
        public const string RoomOption = "--ze-multiplayer-room";
        public const string SessionOption = "--ze-multiplayer-session";
        public const string GenerationOption = "--ze-multiplayer-generation";
        public const string LocalUserOption = "--ze-multiplayer-local-user";
        public const string LocalNameOption = "--ze-multiplayer-local-name";
        public const string HostUserOption = "--ze-multiplayer-host-user";
        public const string HostNameOption = "--ze-multiplayer-host-name";
        public const string RemoteUserOption = "--ze-multiplayer-remote-user";
        public const string RemoteNameOption = "--ze-multiplayer-remote-name";
        public const string ProtocolOption = "--ze-multiplayer-protocol";
        public const string ProductOption = "--ze-multiplayer-product";
        public const string GameProtocolOption = "--ze-multiplayer-game-protocol";
        public const string ContentOption = "--ze-multiplayer-content";
        public const string BuildOption = "--ze-multiplayer-build";
        public const string GameRoomOption = "--ze-multiplayer-game-room";
        public const string MaxMembersOption = "--ze-multiplayer-max-members";
        public const string ExitOnReadyOption = "--ze-multiplayer-exit-on-ready";

        public static OperationResult<LocalDevelopmentRoomOptions> Parse(
            IReadOnlyList<string> arguments,
            CompatibilityDescriptor fallbackCompatibility,
            string fallbackProtocolVersion,
            ushort fallbackPort = 7770)
        {
            Dictionary<string, string> values = ReadOptions(arguments);
            LocalMultiplayerRole role;
            if (!TryReadRole(Get(values, RoleOption), out role))
            {
                return OperationResult<LocalDevelopmentRoomOptions>.Failure(
                    MultiplayerErrorCode.InvalidArgument,
                    "multiplayer.local.argument_role_invalid");
            }

            ushort port = fallbackPort;
            string portValue = Get(values, PortOption);
            if (!string.IsNullOrEmpty(portValue) &&
                !ushort.TryParse(portValue, NumberStyles.None, CultureInfo.InvariantCulture, out port))
            {
                return OperationResult<LocalDevelopmentRoomOptions>.Failure(
                    MultiplayerErrorCode.InvalidArgument,
                    "multiplayer.local.argument_port_invalid",
                    portValue);
            }

            long generation = 1;
            string generationValue = Get(values, GenerationOption);
            if (!string.IsNullOrEmpty(generationValue) &&
                (!long.TryParse(generationValue, NumberStyles.None, CultureInfo.InvariantCulture, out generation) || generation < 1))
            {
                return OperationResult<LocalDevelopmentRoomOptions>.Failure(
                    MultiplayerErrorCode.InvalidArgument,
                    "multiplayer.local.argument_generation_invalid",
                    generationValue);
            }

            int maxMembers = 2;
            string maxMembersValue = Get(values, MaxMembersOption);
            if (!string.IsNullOrEmpty(maxMembersValue) &&
                (!int.TryParse(maxMembersValue, NumberStyles.None, CultureInfo.InvariantCulture, out maxMembers) || maxMembers < 1))
            {
                return OperationResult<LocalDevelopmentRoomOptions>.Failure(
                    MultiplayerErrorCode.InvalidArgument,
                    "multiplayer.local.argument_max_members_invalid",
                    maxMembersValue);
            }

            string localUserDefault = role == LocalMultiplayerRole.Host ? "local-host" : "local-client";
            string localNameDefault = role == LocalMultiplayerRole.Host ? "Local Host" : "Local Client";
            PlatformUser localUser = new PlatformUser(
                new PlatformUserId(DefaultIfEmpty(Get(values, LocalUserOption), localUserDefault)),
                DefaultIfEmpty(Get(values, LocalNameOption), localNameDefault));
            PlatformUser hostUser = new PlatformUser(
                new PlatformUserId(DefaultIfEmpty(Get(values, HostUserOption), "local-host")),
                DefaultIfEmpty(Get(values, HostNameOption), "Local Host"));
            PlatformUser expectedRemote = new PlatformUser(
                new PlatformUserId(DefaultIfEmpty(Get(values, RemoteUserOption), "local-client")),
                DefaultIfEmpty(Get(values, RemoteNameOption), "Local Client"));

            CompatibilityDescriptor compatibility = new CompatibilityDescriptor(
                DefaultIfEmpty(Get(values, ProductOption), fallbackCompatibility.ProductId),
                DefaultIfEmpty(Get(values, GameProtocolOption), fallbackCompatibility.GameProtocolVersion),
                DefaultIfEmpty(Get(values, ContentOption), fallbackCompatibility.ContentRevision),
                DefaultIfEmpty(Get(values, BuildOption), fallbackCompatibility.BuildVersion),
                DefaultIfEmpty(Get(values, GameRoomOption), fallbackCompatibility.GameRoomId));

            LocalDevelopmentRoomOptions options = new LocalDevelopmentRoomOptions(
                role,
                DefaultIfEmpty(Get(values, AddressOption), "127.0.0.1"),
                port,
                new RoomId(DefaultIfEmpty(Get(values, RoomOption), "local-room")),
                new SessionId(DefaultIfEmpty(Get(values, SessionOption), "local-session")),
                generation,
                localUser,
                hostUser,
                expectedRemote,
                compatibility,
                DefaultIfEmpty(Get(values, ProtocolOption), fallbackProtocolVersion),
                maxMembers,
                RoomVisibility.Private,
                values.ContainsKey(ExitOnReadyOption));

            OperationResult validation = options.Validate();
            return validation.Succeeded
                ? OperationResult<LocalDevelopmentRoomOptions>.Success(options)
                : OperationResult<LocalDevelopmentRoomOptions>.FromFailure(validation);
        }

        public static string Build(LocalDevelopmentRoomOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            List<string> parts = new List<string>();
            Add(parts, RoleOption, options.Role == LocalMultiplayerRole.Host ? "host" : "client");
            Add(parts, AddressOption, options.Address);
            Add(parts, PortOption, options.Port.ToString(CultureInfo.InvariantCulture));
            Add(parts, RoomOption, options.RoomId.Value);
            Add(parts, SessionOption, options.SessionId.Value);
            Add(parts, GenerationOption, options.SessionGeneration.ToString(CultureInfo.InvariantCulture));
            Add(parts, LocalUserOption, options.LocalUser.Id.Value);
            Add(parts, LocalNameOption, options.LocalUser.DisplayName);
            Add(parts, HostUserOption, options.HostUser.Id.Value);
            Add(parts, HostNameOption, options.HostUser.DisplayName);
            Add(parts, RemoteUserOption, options.ExpectedRemoteUser.Id.Value);
            Add(parts, RemoteNameOption, options.ExpectedRemoteUser.DisplayName);
            Add(parts, ProtocolOption, options.ProtocolVersion);
            Add(parts, ProductOption, options.Compatibility.ProductId);
            Add(parts, GameProtocolOption, options.Compatibility.GameProtocolVersion);
            Add(parts, ContentOption, options.Compatibility.ContentRevision);
            Add(parts, BuildOption, options.Compatibility.BuildVersion);
            Add(parts, GameRoomOption, options.Compatibility.GameRoomId);
            Add(parts, MaxMembersOption, options.MaxMembers.ToString(CultureInfo.InvariantCulture));
            if (options.ExitOnReady)
            {
                parts.Add(ExitOnReadyOption);
            }

            return string.Join(" ", parts);
        }

        private static Dictionary<string, string> ReadOptions(IReadOnlyList<string> arguments)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (arguments == null)
            {
                return result;
            }

            for (int i = 0; i < arguments.Count; i++)
            {
                string current = arguments[i];
                if (string.IsNullOrEmpty(current) || !current.StartsWith("--ze-multiplayer-", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(current, ExitOnReadyOption, StringComparison.Ordinal))
                {
                    result[current] = string.Empty;
                    continue;
                }

                if (i + 1 < arguments.Count)
                {
                    result[current] = arguments[++i] ?? string.Empty;
                }
            }

            return result;
        }

        private static bool TryReadRole(string value, out LocalMultiplayerRole role)
        {
            if (string.Equals(value, "host", StringComparison.OrdinalIgnoreCase))
            {
                role = LocalMultiplayerRole.Host;
                return true;
            }

            if (string.Equals(value, "client", StringComparison.OrdinalIgnoreCase))
            {
                role = LocalMultiplayerRole.Client;
                return true;
            }

            role = LocalMultiplayerRole.None;
            return false;
        }

        private static string Get(IDictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : string.Empty;
        }

        private static string DefaultIfEmpty(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value.Trim();
        }

        private static void Add(ICollection<string> parts, string option, string value)
        {
            parts.Add(option);
            parts.Add(Quote(value));
        }

        private static string Quote(string value)
        {
            value = value ?? string.Empty;
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return value;
            }

            StringBuilder builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '"')
                {
                    builder.Append('\\');
                }

                builder.Append(value[i]);
            }

            builder.Append('"');
            return builder.ToString();
        }
    }
}
