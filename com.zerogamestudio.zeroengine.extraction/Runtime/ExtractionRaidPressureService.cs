using System;

namespace POB.Extraction
{
    public static class ExtractionRaidPressureService
    {
        public static int GetRemainingSeconds(ExtractionRaidSession session, long currentUnixSeconds)
        {
            if (session == null || session.DurationSeconds <= 0) return 0;

            long elapsed = GetElapsedSeconds(session, currentUnixSeconds);
            long remaining = GetEffectiveDurationSeconds(session) - elapsed;
            if (remaining <= 0) return 0;
            return remaining > int.MaxValue ? int.MaxValue : (int)remaining;
        }

        // graceSeconds：M2 SS.1 上涌危险的安全网——地图配了上涌时，硬判负阈值后移
        // Duration+graceSeconds，给"全程骑在水面之上"的极端情形留出退场时间。默认 0=既有行为，
        // CanStartExtraction/CanCompleteExtraction 内部调用不传 grace，撤离点判定口径不受影响。
        public static bool ShouldFailForTimeout(ExtractionRaidSession session, long currentUnixSeconds, int graceSeconds = 0)
        {
            if (session == null || session.DurationSeconds <= 0) return false;
            // Profile-driven raids transition to Overtime at zero. Timeout is
            // retained only for legacy sessions without a frozen rule snapshot.
            if (session.RuleSnapshot != null) return false;
            long deadline = (long)GetEffectiveDurationSeconds(session) + Math.Max(0, graceSeconds);
            return GetElapsedSeconds(session, currentUnixSeconds) >= deadline;
        }

        // Timeline consumers can distinguish the zero-second boundary from
        // the legacy fail-for-timeout result. This additive query never changes
        // ShouldFailForTimeout's existing behavior.
        public static bool IsOvertime(ExtractionRaidSession session, long currentUnixSeconds)
        {
            if (session == null || session.DurationSeconds <= 0) return false;
            return session.Content?.Phase == ExtractionRaidPhase.Overtime
                || session.Content?.IsOvertime == true
                || GetElapsedSeconds(session, currentUnixSeconds) >= GetEffectiveDurationSeconds(session);
        }

        public static bool CanStartExtraction(
            ExtractionRaidSession session,
            ExtractionPointDefinition point,
            long currentUnixSeconds,
            bool hasEmergencyOverride)
        {
            if (session == null || point == null || !point.IsValid) return false;
            if (session.Content?.Phase != ExtractionRaidPhase.Overtime
                && session.Content?.IsOvertime != true
                && ShouldFailForTimeout(session, currentUnixSeconds)) return false;

            long elapsed = GetElapsedSeconds(session, currentUnixSeconds);
            if (point.SingleUse && session.Content.UsedExtractionPointIds.Contains(point.PointId)) return false;
            if (point.DefaultOpen) return true;
            if (elapsed >= GetEffectiveOpenAtElapsedSeconds(session, point)) return true;

            return hasEmergencyOverride
                   && session.AllowEmergencyExtraction
                   && point.AllowEmergencyExtractionOverride;
        }

        public static bool CanCompleteExtraction(
            ExtractionRaidSession session,
            ExtractionPointDefinition point,
            long channelStartedAtUnixSeconds,
            long currentUnixSeconds,
            bool hasEmergencyOverride)
        {
            if (session == null || point == null || !point.IsValid) return false;
            if (session.Content?.Phase != ExtractionRaidPhase.Overtime
                && session.Content?.IsOvertime != true
                && ShouldFailForTimeout(session, currentUnixSeconds)) return false;
            if (!CanStartExtraction(session, point, channelStartedAtUnixSeconds, hasEmergencyOverride)) return false;

            long channelElapsed = currentUnixSeconds - channelStartedAtUnixSeconds;
            return channelElapsed >= point.ChannelSeconds;
        }

        // M2 SS.1：上涌危险的高度公式需要同一份 elapsed 口径，改成 public 复用，
        // 不让 ExtractionRisingHazard 另起一套(currentUnixSeconds - StartedAtUnixSeconds)算法漂移。
        public static long GetElapsedSeconds(ExtractionRaidSession session, long currentUnixSeconds)
        {
            long elapsed = currentUnixSeconds - session.StartedAtUnixSeconds;
            return elapsed < 0 ? 0 : elapsed;
        }

        public static int GetEffectiveDurationSeconds(ExtractionRaidSession session)
        {
            if (session == null) return 0;
            long duration = (long)session.DurationSeconds + (session.Content?.DeadlineExtensionSeconds ?? 0);
            return duration <= 0 ? 0 : duration > int.MaxValue ? int.MaxValue : (int)duration;
        }

        public static int GetEffectiveThreatLevel(ExtractionRaidSession session)
        {
            if (session == null) return 0;
            long threat = (long)session.ThreatLevel + (session.Content?.ThreatLevelDelta ?? 0);
            return threat <= 0 ? 0 : threat > int.MaxValue ? int.MaxValue : (int)threat;
        }

        public static int GetEffectiveOpenAtElapsedSeconds(
            ExtractionRaidSession session,
            ExtractionPointDefinition point)
        {
            if (session?.Content?.ExtractionPointStates != null && point != null)
            {
                foreach (var state in session.Content.ExtractionPointStates)
                    if (state != null && state.PointId == point.PointId)
                        return Math.Max(0, state.EffectiveOpenAtElapsedSeconds);
            }

            return Math.Max(0, point?.OpenAtElapsedSeconds ?? 0);
        }
    }
}
