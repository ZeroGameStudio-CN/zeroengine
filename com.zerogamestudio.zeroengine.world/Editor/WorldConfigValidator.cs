using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Calendar;
using ZeroEngine.EnvironmentSystem;

namespace ZeroEngine.World.Editor
{
    public enum WorldValidationSeverity
    {
        Error,
        Warning,
        Info
    }

    public readonly struct WorldValidationIssue
    {
        public readonly ScriptableObject Asset;
        public readonly WorldValidationSeverity Severity;
        public readonly string FieldPath;
        public readonly string Message;

        public WorldValidationIssue(
            ScriptableObject asset,
            WorldValidationSeverity severity,
            string fieldPath,
            string message)
        {
            Asset = asset;
            Severity = severity;
            FieldPath = fieldPath ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    public static class WorldConfigValidator
    {
        public static IReadOnlyList<T> LoadAssets<T>() where T : ScriptableObject
        {
            var result = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                {
                    result.Add(asset);
                }
            }

            return result;
        }

        public static IReadOnlyList<WorldValidationIssue> Validate(
            IEnumerable<CalendarEventSO> calendarEvents = null,
            IEnumerable<WeatherPresetSO> weatherPresets = null,
            IEnumerable<DayNightPresetSO> dayNightPresets = null)
        {
            var issues = new List<WorldValidationIssue>();
            var eventList = Materialize(calendarEvents);
            var weatherList = Materialize(weatherPresets);
            var dayNightList = Materialize(dayNightPresets);

            foreach (var calendarEvent in eventList)
            {
                ValidateCalendarEvent(calendarEvent, issues);
            }

            foreach (var weatherPreset in weatherList)
            {
                ValidateWeatherPreset(weatherPreset, issues);
            }

            foreach (var dayNightPreset in dayNightList)
            {
                ValidateDayNightPreset(dayNightPreset, issues);
            }

            AddDuplicateStringIssues(eventList, evt => evt.EventData?.EventId, "EventData.EventId", "Calendar event ID", issues);
            AddDuplicateEnumIssues(weatherList, preset => preset.Data?.WeatherType, "Data.WeatherType", "WeatherType", issues);
            return issues;
        }

        private static T[] Materialize<T>(IEnumerable<T> assets) where T : ScriptableObject
        {
            return (assets ?? Array.Empty<T>())
                .Where(asset => asset != null)
                .ToArray();
        }

        private static void ValidateCalendarEvent(CalendarEventSO calendarEvent, ICollection<WorldValidationIssue> issues)
        {
            var data = calendarEvent.EventData;
            if (data == null)
            {
                issues.Add(new WorldValidationIssue(calendarEvent, WorldValidationSeverity.Error, nameof(CalendarEventSO.EventData), "Calendar event data is missing."));
                return;
            }

            RequireId(calendarEvent, issues, "EventData.EventId", data.EventId, "Calendar event ID");
            RequireDisplayName(calendarEvent, issues, "EventData.DisplayName", data.DisplayName, "Calendar event display name");
            ValidateDate(calendarEvent, data.StartDate, "EventData.StartDate", issues);
            ValidateDate(calendarEvent, data.EndDate, "EventData.EndDate", issues);
            ValidateTime(calendarEvent, data.StartTime, "EventData.StartTime", issues);
            ValidateTime(calendarEvent, data.EndTime, "EventData.EndTime", issues);
            RequirePositive(calendarEvent, issues, "EventData.RecurrenceInterval", data.RecurrenceInterval, "RecurrenceInterval");
            RequireNonNegative(calendarEvent, issues, "EventData.RequiredLevel", data.RequiredLevel, "RequiredLevel");
            RequireNonNegative(calendarEvent, issues, "EventData.ReminderDaysBefore", data.ReminderDaysBefore, "ReminderDaysBefore");

            if (data.Type == CalendarEventType.OneTime && data.EndDate < data.StartDate)
            {
                issues.Add(new WorldValidationIssue(calendarEvent, WorldValidationSeverity.Error, "EventData.EndDate", "EndDate is earlier than StartDate."));
            }

            if (data.EndDate == data.StartDate && data.EndTime.TotalMinutes < data.StartTime.TotalMinutes)
            {
                issues.Add(new WorldValidationIssue(calendarEvent, WorldValidationSeverity.Error, "EventData.EndTime", "EndTime is earlier than StartTime on the same day."));
            }

            if (data.Type == CalendarEventType.Weekly)
            {
                ValidateWeeklyRecurrence(calendarEvent, data, issues);
            }

            if (data.Type == CalendarEventType.Custom)
            {
                issues.Add(new WorldValidationIssue(calendarEvent, WorldValidationSeverity.Warning, "EventData.Type", "Custom calendar events need project-specific runtime handling."));
            }
        }

        private static void ValidateWeeklyRecurrence(
            CalendarEventSO calendarEvent,
            CalendarEventData data,
            ICollection<WorldValidationIssue> issues)
        {
            if (data.RecurrenceDays == null || data.RecurrenceDays.Count == 0)
            {
                issues.Add(new WorldValidationIssue(calendarEvent, WorldValidationSeverity.Error, "EventData.RecurrenceDays", "Weekly event has no recurrence days."));
                return;
            }

            var seen = new HashSet<int>();
            foreach (var day in data.RecurrenceDays)
            {
                if (day < 0 || day > 6)
                {
                    issues.Add(new WorldValidationIssue(calendarEvent, WorldValidationSeverity.Error, "EventData.RecurrenceDays", "Weekly event has invalid recurrence day."));
                    continue;
                }

                if (!seen.Add(day))
                {
                    issues.Add(new WorldValidationIssue(calendarEvent, WorldValidationSeverity.Error, "EventData.RecurrenceDays", "Weekly event has duplicate recurrence day."));
                }
            }
        }

        private static void ValidateWeatherPreset(WeatherPresetSO preset, ICollection<WorldValidationIssue> issues)
        {
            var data = preset.Data;
            if (data == null)
            {
                issues.Add(new WorldValidationIssue(preset, WorldValidationSeverity.Error, nameof(WeatherPresetSO.Data), "Weather preset data is missing."));
                return;
            }

            RequireDisplayName(preset, issues, "Data.Description", data.Description, "Weather description");
            RequireNonNegative(preset, issues, "Data.LightIntensityMultiplier", data.LightIntensityMultiplier, "LightIntensityMultiplier");
            RequireNonNegative(preset, issues, "Data.TransitionDuration", data.TransitionDuration, "TransitionDuration");
            RequireNormalized(preset, issues, "Data.AmbientVolume", data.AmbientVolume, "AmbientVolume");

            if (data.OverrideFog)
            {
                RequireRange(preset, issues, "Data.FogDensity", data.FogDensity, 0f, 0.1f, "FogDensity");
            }

            if (data.WeatherType == WeatherType.Custom && string.IsNullOrWhiteSpace(data.Description))
            {
                issues.Add(new WorldValidationIssue(preset, WorldValidationSeverity.Warning, "Data.Description", "Custom weather preset should describe its project-specific behavior."));
            }
        }

        private static void ValidateDayNightPreset(DayNightPresetSO preset, ICollection<WorldValidationIssue> issues)
        {
            var data = preset.Data;
            if (data == null)
            {
                issues.Add(new WorldValidationIssue(preset, WorldValidationSeverity.Error, nameof(DayNightPresetSO.Data), "Day/night preset data is missing."));
                return;
            }

            RequireCurve(preset, issues, "Data.SunIntensityOverDay", data.SunIntensityOverDay, "SunIntensityOverDay");
            RequireCurve(preset, issues, "Data.AmbientIntensityOverDay", data.AmbientIntensityOverDay, "AmbientIntensityOverDay");
            RequireGradient(preset, issues, "Data.SunColorOverDay", data.SunColorOverDay, "SunColorOverDay");
            RequireGradient(preset, issues, "Data.AmbientColorOverDay", data.AmbientColorOverDay, "AmbientColorOverDay");
            RequireGradient(preset, issues, "Data.FogColorOverDay", data.FogColorOverDay, "FogColorOverDay");
            RequireNonNegative(preset, issues, "Data.MaxSunIntensity", data.MaxSunIntensity, "MaxSunIntensity");
            RequireAngle(preset, issues, "Data.SunriseAngle", data.SunriseAngle, "SunriseAngle");
            RequireAngle(preset, issues, "Data.SunsetAngle", data.SunsetAngle, "SunsetAngle");

            if (Mathf.Approximately(data.SunriseAngle, data.SunsetAngle))
            {
                issues.Add(new WorldValidationIssue(preset, WorldValidationSeverity.Error, "Data.SunsetAngle", "SunriseAngle and SunsetAngle must be different."));
            }

            if (data.SkyboxMaterial == null)
            {
                issues.Add(new WorldValidationIssue(preset, WorldValidationSeverity.Warning, "Data.SkyboxMaterial", "SkyboxMaterial is not assigned."));
            }
        }

        private static void ValidateDate(
            ScriptableObject asset,
            GameDate date,
            string fieldPath,
            ICollection<WorldValidationIssue> issues)
        {
            RequirePositive(asset, issues, $"{fieldPath}.{nameof(GameDate.Year)}", date.Year, $"{fieldPath} year");
            RequireRange(asset, issues, $"{fieldPath}.{nameof(GameDate.Month)}", date.Month, 1, 12, $"{fieldPath} month");
            RequireRange(asset, issues, $"{fieldPath}.{nameof(GameDate.Day)}", date.Day, 1, 30, $"{fieldPath} day");
        }

        private static void ValidateTime(
            ScriptableObject asset,
            GameTime time,
            string fieldPath,
            ICollection<WorldValidationIssue> issues)
        {
            RequireRange(asset, issues, $"{fieldPath}.{nameof(GameTime.Hour)}", time.Hour, 0, 23, $"{fieldPath} hour");
            RequireRange(asset, issues, $"{fieldPath}.{nameof(GameTime.Minute)}", time.Minute, 0, 59, $"{fieldPath} minute");
        }

        private static void RequireId(
            ScriptableObject asset,
            ICollection<WorldValidationIssue> issues,
            string fieldPath,
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new WorldValidationIssue(asset, WorldValidationSeverity.Error, fieldPath, $"{label} is empty."));
            }
            else if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                issues.Add(new WorldValidationIssue(asset, WorldValidationSeverity.Warning, fieldPath, $"{label} has leading/trailing whitespace."));
            }
        }

        private static void RequireDisplayName(
            ScriptableObject asset,
            ICollection<WorldValidationIssue> issues,
            string fieldPath,
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new WorldValidationIssue(asset, WorldValidationSeverity.Warning, fieldPath, $"{label} is empty."));
            }
        }

        private static void RequirePositive(
            ScriptableObject asset,
            ICollection<WorldValidationIssue> issues,
            string fieldPath,
            int value,
            string label)
        {
            if (value <= 0)
            {
                issues.Add(new WorldValidationIssue(asset, WorldValidationSeverity.Error, fieldPath, $"{label} must be greater than 0."));
            }
        }

        private static void RequireNonNegative(
            ScriptableObject asset,
            ICollection<WorldValidationIssue> issues,
            string fieldPath,
            int value,
            string label)
        {
            if (value < 0)
            {
                issues.Add(new WorldValidationIssue(asset, WorldValidationSeverity.Error, fieldPath, $"{label} must not be negative."));
            }
        }

        private static void RequireNonNegative(
            ScriptableObject asset,
            ICollection<WorldValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value < 0f)
            {
                issues.Add(new WorldValidationIssue(asset, WorldValidationSeverity.Error, fieldPath, $"{label} must not be negative."));
            }
        }

        private static void RequireNormalized(
            ScriptableObject asset,
            ICollection<WorldValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value < 0f || value > 1f)
            {
                issues.Add(new WorldValidationIssue(asset, WorldValidationSeverity.Error, fieldPath, $"{label} must be between 0 and 1."));
            }
        }

        private static void RequireRange(
            ScriptableObject asset,
            ICollection<WorldValidationIssue> issues,
            string fieldPath,
            int value,
            int minInclusive,
            int maxInclusive,
            string label)
        {
            if (value < minInclusive || value > maxInclusive)
            {
                issues.Add(new WorldValidationIssue(asset, WorldValidationSeverity.Error, fieldPath, $"{label} must be between {minInclusive} and {maxInclusive}."));
            }
        }

        private static void RequireRange(
            ScriptableObject asset,
            ICollection<WorldValidationIssue> issues,
            string fieldPath,
            float value,
            float minInclusive,
            float maxInclusive,
            string label)
        {
            if (value < minInclusive || value > maxInclusive)
            {
                issues.Add(new WorldValidationIssue(asset, WorldValidationSeverity.Error, fieldPath, $"{label} must be between {minInclusive:0.###} and {maxInclusive:0.###}."));
            }
        }

        private static void RequireAngle(
            ScriptableObject asset,
            ICollection<WorldValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value < 0f || value >= 360f)
            {
                issues.Add(new WorldValidationIssue(asset, WorldValidationSeverity.Error, fieldPath, $"{label} must be between 0 inclusive and 360 exclusive."));
            }
        }

        private static void RequireCurve(
            ScriptableObject asset,
            ICollection<WorldValidationIssue> issues,
            string fieldPath,
            AnimationCurve curve,
            string label)
        {
            if (curve == null || curve.length == 0)
            {
                issues.Add(new WorldValidationIssue(asset, WorldValidationSeverity.Error, fieldPath, $"{label} curve is empty."));
            }
        }

        private static void RequireGradient(
            ScriptableObject asset,
            ICollection<WorldValidationIssue> issues,
            string fieldPath,
            Gradient gradient,
            string label)
        {
            if (gradient == null)
            {
                issues.Add(new WorldValidationIssue(asset, WorldValidationSeverity.Error, fieldPath, $"{label} gradient is missing."));
            }
        }

        private static void AddDuplicateStringIssues<T>(
            IEnumerable<T> assets,
            Func<T, string> keySelector,
            string fieldPath,
            string label,
            ICollection<WorldValidationIssue> issues)
            where T : ScriptableObject
        {
            foreach (var duplicateGroup in assets
                         .Select(asset => new { Asset = asset, Key = keySelector(asset)?.Trim() })
                         .Where(record => !string.IsNullOrEmpty(record.Key))
                         .GroupBy(record => record.Key, StringComparer.OrdinalIgnoreCase))
            {
                var duplicates = duplicateGroup.ToArray();
                if (duplicates.Length <= 1)
                {
                    continue;
                }

                foreach (var duplicate in duplicates)
                {
                    issues.Add(new WorldValidationIssue(
                        duplicate.Asset,
                        WorldValidationSeverity.Error,
                        fieldPath,
                        $"{label} '{duplicate.Key}' is duplicated in {duplicates.Length} assets."));
                }
            }
        }

        private static void AddDuplicateEnumIssues<T, TEnum>(
            IEnumerable<T> assets,
            Func<T, TEnum?> keySelector,
            string fieldPath,
            string label,
            ICollection<WorldValidationIssue> issues)
            where T : ScriptableObject
            where TEnum : struct, Enum
        {
            foreach (var duplicateGroup in assets
                         .Select(asset => new { Asset = asset, Key = keySelector(asset) })
                         .Where(record => record.Key.HasValue)
                         .GroupBy(record => record.Key.Value))
            {
                var duplicates = duplicateGroup.ToArray();
                if (duplicates.Length <= 1)
                {
                    continue;
                }

                foreach (var duplicate in duplicates)
                {
                    issues.Add(new WorldValidationIssue(
                        duplicate.Asset,
                        WorldValidationSeverity.Error,
                        fieldPath,
                        $"{label} '{duplicate.Key.Value}' is duplicated in {duplicates.Length} assets."));
                }
            }
        }
    }
}
