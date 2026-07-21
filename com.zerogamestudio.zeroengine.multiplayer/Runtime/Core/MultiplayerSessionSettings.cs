using System;
using System.Collections.Generic;

namespace ZeroEngine.Multiplayer
{
    public static class MultiplayerSessionSettings
    {
        public static IReadOnlyList<string> Validate(IMultiplayerSessionSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            List<string> errors = new List<string>();

            if (settings.MaxPlayers < 1)
            {
                errors.Add("multiplayer.config.max_players_invalid");
            }

            if (settings.MinimumPlayersToStart < 1 ||
                settings.MinimumPlayersToStart > settings.MaxPlayers)
            {
                errors.Add("multiplayer.config.minimum_players_invalid");
            }

            if (settings.CreateTimeout <= TimeSpan.Zero ||
                settings.JoinTimeout <= TimeSpan.Zero ||
                settings.ConnectionTimeout <= TimeSpan.Zero ||
                settings.InitialSyncTimeout <= TimeSpan.Zero ||
                settings.StartTimeout <= TimeSpan.Zero ||
                settings.LeaveTimeout <= TimeSpan.Zero)
            {
                errors.Add("multiplayer.config.timeout_invalid");
            }

            if (string.IsNullOrWhiteSpace(settings.ProtocolVersion))
            {
                errors.Add("multiplayer.config.protocol_version_missing");
            }

            if (settings.ReconnectEnabled)
            {
                ValidateReconnect(settings, errors);
            }

            if (settings is IMultiplayerSessionSettingsValidator validator)
            {
                IReadOnlyList<string> additionalErrors = validator.ValidateAdditionalSettings();
                if (additionalErrors != null)
                {
                    for (int i = 0; i < additionalErrors.Count; i++)
                    {
                        string error = additionalErrors[i];
                        if (!string.IsNullOrWhiteSpace(error) && !errors.Contains(error))
                        {
                            errors.Add(error);
                        }
                    }
                }
            }

            return errors.AsReadOnly();
        }

        public static ReconnectPolicy CreateReconnectPolicy(IMultiplayerSessionSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            IReadOnlyList<TimeSpan> configured = settings.ReconnectRetryIntervals;
            TimeSpan[] delays = new TimeSpan[configured == null ? 0 : configured.Count];
            for (int i = 0; i < delays.Length; i++)
            {
                delays[i] = configured[i];
            }

            return new ReconnectPolicy(
                settings.ReconnectMaxAttempts,
                settings.ReconnectAttemptTimeout,
                delays,
                settings.ReconnectHardDeadline,
                settings.ReconnectGracePeriod);
        }

        private static void ValidateReconnect(
            IMultiplayerSessionSettings settings,
            List<string> errors)
        {
            if (settings.ReconnectMaxAttempts < 1 ||
                settings.ReconnectAttemptTimeout <= TimeSpan.Zero ||
                settings.ReconnectHardDeadline <= TimeSpan.Zero ||
                settings.ReconnectGracePeriod <= TimeSpan.Zero)
            {
                errors.Add("multiplayer.config.reconnect_limits_invalid");
                return;
            }

            IReadOnlyList<TimeSpan> intervals = settings.ReconnectRetryIntervals;
            if (intervals == null || intervals.Count < settings.ReconnectMaxAttempts)
            {
                errors.Add("multiplayer.config.reconnect_intervals_missing");
                return;
            }

            double scheduleSeconds =
                settings.ReconnectAttemptTimeout.TotalSeconds * settings.ReconnectMaxAttempts;
            for (int i = 0; i < settings.ReconnectMaxAttempts; i++)
            {
                if (intervals[i] < TimeSpan.Zero)
                {
                    errors.Add("multiplayer.config.reconnect_interval_negative");
                    return;
                }

                scheduleSeconds += intervals[i].TotalSeconds;
            }

            if (scheduleSeconds > settings.ReconnectHardDeadline.TotalSeconds)
            {
                errors.Add("multiplayer.config.reconnect_schedule_exceeds_deadline");
            }

            if (settings.ReconnectHardDeadline > settings.ReconnectGracePeriod)
            {
                errors.Add("multiplayer.config.reconnect_deadline_exceeds_grace");
            }
        }
    }
}
