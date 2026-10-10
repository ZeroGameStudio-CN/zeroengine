using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Multiplayer
{
    [CreateAssetMenu(fileName = "MultiplayerSessionConfig", menuName = "ZeroEngine/Multiplayer/Session Config")]
    public sealed class MultiplayerSessionConfig : ScriptableObject
    {
        [Header("Room")]
        [SerializeField] private RoomVisibility defaultVisibility = RoomVisibility.FriendsOnly;
        [Min(1)] [SerializeField] private int maxPlayers = 2;
        [Min(1)] [SerializeField] private int minimumPlayersToStart = 2;
        [SerializeField] private bool allowJoinInProgress;

        [Header("Timeouts (seconds)")]
        [Min(0.1f)] [SerializeField] private float createTimeoutSeconds = 10f;
        [Min(0.1f)] [SerializeField] private float joinTimeoutSeconds = 10f;
        [Min(0.1f)] [SerializeField] private float connectionTimeoutSeconds = 10f;
        [Min(0.1f)] [SerializeField] private float initialSyncTimeoutSeconds = 15f;
        [Min(0.1f)] [SerializeField] private float startTimeoutSeconds = 15f;
        [Min(0.1f)] [SerializeField] private float leaveTimeoutSeconds = 5f;

        [Header("Reconnect")]
        [SerializeField] private bool reconnectEnabled = true;
        [Min(0.1f)] [SerializeField] private float reconnectGraceSeconds = 20f;
        [Min(1)] [SerializeField] private int reconnectMaxAttempts = 3;
        [Min(0.1f)] [SerializeField] private float reconnectAttemptTimeoutSeconds = 4f;
        [SerializeField] private float[] reconnectRetryIntervalsSeconds = { 0.5f, 1.5f, 3f };
        [Min(0.1f)] [SerializeField] private float reconnectHardDeadlineSeconds = 18f;

        [Header("Compatibility")]
        [SerializeField] private string protocolVersion = "1";
        [SerializeField] private BuildMatchPolicy buildMatchPolicy = BuildMatchPolicy.Exact;
        [SerializeField] private string metadataPrefix = "ze_";

        [Header("Transport")]
        [SerializeField] private TransportMode defaultTransport = TransportMode.SteamP2P;
        [Range(1, 65535)] [SerializeField] private int localPort = 7770;
        [SerializeField] private string localDevelopmentArguments = "--multiplayer-role";

        [Header("Logging")]
        [SerializeField] private MultiplayerLogLevel logLevel = MultiplayerLogLevel.Info;
        [SerializeField] private bool logStateTransitions = true;
        [SerializeField] private bool logNetworkStatistics;

        public RoomVisibility DefaultVisibility => defaultVisibility;
        public int MaxPlayers => maxPlayers;
        public int MinimumPlayersToStart => minimumPlayersToStart;
        public bool AllowJoinInProgress => allowJoinInProgress;
        public TimeSpan CreateTimeout => TimeSpan.FromSeconds(createTimeoutSeconds);
        public TimeSpan JoinTimeout => TimeSpan.FromSeconds(joinTimeoutSeconds);
        public TimeSpan ConnectionTimeout => TimeSpan.FromSeconds(connectionTimeoutSeconds);
        public TimeSpan InitialSyncTimeout => TimeSpan.FromSeconds(initialSyncTimeoutSeconds);
        public TimeSpan StartTimeout => TimeSpan.FromSeconds(startTimeoutSeconds);
        public TimeSpan LeaveTimeout => TimeSpan.FromSeconds(leaveTimeoutSeconds);
        public bool ReconnectEnabled => reconnectEnabled;
        public TimeSpan ReconnectGracePeriod => TimeSpan.FromSeconds(reconnectGraceSeconds);
        public int ReconnectMaxAttempts => reconnectMaxAttempts;
        public TimeSpan ReconnectAttemptTimeout => TimeSpan.FromSeconds(reconnectAttemptTimeoutSeconds);
        public IReadOnlyList<float> ReconnectRetryIntervalsSeconds => reconnectRetryIntervalsSeconds;
        public TimeSpan ReconnectHardDeadline => TimeSpan.FromSeconds(reconnectHardDeadlineSeconds);
        public string ProtocolVersion => protocolVersion ?? string.Empty;
        public BuildMatchPolicy BuildMatchPolicy => buildMatchPolicy;
        public string MetadataPrefix => metadataPrefix ?? string.Empty;
        public TransportMode DefaultTransport => defaultTransport;
        public int LocalPort => localPort;
        public string LocalDevelopmentArguments => localDevelopmentArguments ?? string.Empty;
        public MultiplayerLogLevel LogLevel => logLevel;
        public bool LogStateTransitions => logStateTransitions;
        public bool LogNetworkStatistics => logNetworkStatistics;

        public IReadOnlyList<string> ValidateConfiguration()
        {
            List<string> errors = new List<string>();

            if (maxPlayers < 1)
            {
                errors.Add("multiplayer.config.max_players_invalid");
            }

            if (minimumPlayersToStart < 1 || minimumPlayersToStart > maxPlayers)
            {
                errors.Add("multiplayer.config.minimum_players_invalid");
            }

            if (createTimeoutSeconds <= 0f || joinTimeoutSeconds <= 0f ||
                connectionTimeoutSeconds <= 0f || initialSyncTimeoutSeconds <= 0f ||
                startTimeoutSeconds <= 0f || leaveTimeoutSeconds <= 0f)
            {
                errors.Add("multiplayer.config.timeout_invalid");
            }

            if (string.IsNullOrWhiteSpace(protocolVersion))
            {
                errors.Add("multiplayer.config.protocol_version_missing");
            }

            if (string.IsNullOrWhiteSpace(metadataPrefix))
            {
                errors.Add("multiplayer.config.metadata_prefix_missing");
            }

            if (localPort < 1 || localPort > 65535)
            {
                errors.Add("multiplayer.config.local_port_invalid");
            }

            if (reconnectEnabled)
            {
                ValidateReconnect(errors);
            }

            return errors.AsReadOnly();
        }

        public ReconnectPolicy CreateReconnectPolicy()
        {
            TimeSpan[] delays = new TimeSpan[reconnectRetryIntervalsSeconds == null ? 0 : reconnectRetryIntervalsSeconds.Length];
            for (int i = 0; i < delays.Length; i++)
            {
                delays[i] = TimeSpan.FromSeconds(reconnectRetryIntervalsSeconds[i]);
            }

            return new ReconnectPolicy(
                reconnectMaxAttempts,
                ReconnectAttemptTimeout,
                delays,
                ReconnectHardDeadline,
                ReconnectGracePeriod);
        }

        private void ValidateReconnect(List<string> errors)
        {
            if (reconnectMaxAttempts < 1 || reconnectAttemptTimeoutSeconds <= 0f ||
                reconnectHardDeadlineSeconds <= 0f || reconnectGraceSeconds <= 0f)
            {
                errors.Add("multiplayer.config.reconnect_limits_invalid");
                return;
            }

            if (reconnectRetryIntervalsSeconds == null || reconnectRetryIntervalsSeconds.Length < reconnectMaxAttempts)
            {
                errors.Add("multiplayer.config.reconnect_intervals_missing");
                return;
            }

            double scheduleSeconds = reconnectAttemptTimeoutSeconds * reconnectMaxAttempts;
            for (int i = 0; i < reconnectMaxAttempts; i++)
            {
                if (reconnectRetryIntervalsSeconds[i] < 0f)
                {
                    errors.Add("multiplayer.config.reconnect_interval_negative");
                    return;
                }

                scheduleSeconds += reconnectRetryIntervalsSeconds[i];
            }

            if (scheduleSeconds > reconnectHardDeadlineSeconds)
            {
                errors.Add("multiplayer.config.reconnect_schedule_exceeds_deadline");
            }

            if (reconnectHardDeadlineSeconds > reconnectGraceSeconds)
            {
                errors.Add("multiplayer.config.reconnect_deadline_exceeds_grace");
            }
        }
    }
}
