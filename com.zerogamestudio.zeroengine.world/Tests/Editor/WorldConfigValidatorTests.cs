using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Calendar;
using ZeroEngine.EnvironmentSystem;
using ZeroEngine.World.Editor;
using Object = UnityEngine.Object;

namespace ZeroEngine.World.Editor.Tests
{
    public sealed class WorldConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsDesignerBlockingWorldConfigIssues()
        {
            var eventA = ScriptableObject.CreateInstance<CalendarEventSO>();
            var eventB = ScriptableObject.CreateInstance<CalendarEventSO>();
            var sameDayEvent = ScriptableObject.CreateInstance<CalendarEventSO>();
            var weeklyEvent = ScriptableObject.CreateInstance<CalendarEventSO>();
            var weatherA = ScriptableObject.CreateInstance<WeatherPresetSO>();
            var weatherB = ScriptableObject.CreateInstance<WeatherPresetSO>();
            var dayNight = ScriptableObject.CreateInstance<DayNightPresetSO>();

            try
            {
                eventA.name = "EventA";
                eventA.EventData = new CalendarEventData
                {
                    EventId = " festival ",
                    DisplayName = string.Empty,
                    Type = CalendarEventType.OneTime,
                    StartDate = new GameDate(1, 2, 10),
                    EndDate = new GameDate(1, 2, 5),
                    StartTime = new GameTime(23, 0),
                    EndTime = new GameTime(22, 0)
                };

                eventB.name = "EventB";
                eventB.EventData = new CalendarEventData
                {
                    EventId = "festival",
                    DisplayName = "Festival",
                    StartDate = new GameDate(1, 2, 1),
                    EndDate = new GameDate(1, 2, 1)
                };

                sameDayEvent.name = "SameDayEvent";
                sameDayEvent.EventData = new CalendarEventData
                {
                    EventId = "same_day",
                    DisplayName = "Same Day",
                    Type = CalendarEventType.OneTime,
                    StartDate = new GameDate(1, 2, 1),
                    EndDate = new GameDate(1, 2, 1),
                    StartTime = new GameTime(23, 0),
                    EndTime = new GameTime(22, 0)
                };

                weeklyEvent.name = "WeeklyEvent";
                weeklyEvent.EventData = new CalendarEventData
                {
                    EventId = "weekly",
                    DisplayName = "Weekly",
                    Type = CalendarEventType.Weekly,
                    StartDate = new GameDate { Year = 0, Month = 13, Day = 0 },
                    EndDate = new GameDate(1, 1, 1),
                    RecurrenceInterval = 0,
                    RequiredLevel = -1,
                    ReminderDaysBefore = -1,
                    RecurrenceDays = new List<int> { -1, 1, 1 }
                };

                weatherA.name = "WeatherA";
                weatherA.Data = new WeatherPresetData
                {
                    WeatherType = WeatherType.Clear,
                    Description = string.Empty,
                    OverrideFog = true,
                    FogDensity = -0.1f,
                    LightIntensityMultiplier = -1f,
                    TransitionDuration = -1f,
                    AmbientVolume = 2f
                };

                weatherB.name = "WeatherB";
                weatherB.Data = new WeatherPresetData
                {
                    WeatherType = WeatherType.Clear,
                    Description = "Clear weather"
                };

                dayNight.name = "DayNight";
                dayNight.Data = new DayNightPresetData
                {
                    SunColorOverDay = null,
                    SunIntensityOverDay = null,
                    AmbientColorOverDay = null,
                    AmbientIntensityOverDay = new AnimationCurve(),
                    FogColorOverDay = null,
                    MaxSunIntensity = -1f,
                    SunriseAngle = -1f,
                    SunsetAngle = 360f,
                    SkyboxMaterial = null
                };

                var issues = WorldConfigValidator.Validate(
                    new[] { eventA, eventB, sameDayEvent, weeklyEvent },
                    new[] { weatherA, weatherB },
                    new[] { dayNight });

                AssertIssue(issues, eventA, WorldValidationSeverity.Warning, "Calendar event ID has leading/trailing whitespace.");
                AssertIssue(issues, eventA, WorldValidationSeverity.Warning, "Calendar event display name is empty.");
                AssertIssue(issues, eventA, WorldValidationSeverity.Error, "EndDate is earlier than StartDate.");
                AssertIssue(issues, sameDayEvent, WorldValidationSeverity.Error, "EndTime is earlier than StartTime on the same day.");
                Assert.That(issues.Count(issue => issue.Message.Contains("Calendar event ID") && issue.Message.Contains("duplicated")), Is.EqualTo(2));

                AssertIssue(issues, weeklyEvent, WorldValidationSeverity.Error, "EventData.StartDate year must be greater than 0.");
                AssertIssue(issues, weeklyEvent, WorldValidationSeverity.Error, "EventData.StartDate month must be between 1 and 12.");
                AssertIssue(issues, weeklyEvent, WorldValidationSeverity.Error, "EventData.StartDate day must be between 1 and 30.");
                AssertIssue(issues, weeklyEvent, WorldValidationSeverity.Error, "RecurrenceInterval must be greater than 0.");
                AssertIssue(issues, weeklyEvent, WorldValidationSeverity.Error, "RequiredLevel must not be negative.");
                AssertIssue(issues, weeklyEvent, WorldValidationSeverity.Error, "ReminderDaysBefore must not be negative.");
                AssertIssue(issues, weeklyEvent, WorldValidationSeverity.Error, "Weekly event has invalid recurrence day.");
                AssertIssue(issues, weeklyEvent, WorldValidationSeverity.Error, "Weekly event has duplicate recurrence day.");

                AssertIssue(issues, weatherA, WorldValidationSeverity.Warning, "Weather description is empty.");
                AssertIssue(issues, weatherA, WorldValidationSeverity.Error, "LightIntensityMultiplier must not be negative.");
                AssertIssue(issues, weatherA, WorldValidationSeverity.Error, "TransitionDuration must not be negative.");
                AssertIssue(issues, weatherA, WorldValidationSeverity.Error, "AmbientVolume must be between 0 and 1.");
                AssertIssue(issues, weatherA, WorldValidationSeverity.Error, "FogDensity must be between 0 and 0.1.");
                Assert.That(issues.Count(issue => issue.Message.Contains("WeatherType") && issue.Message.Contains("duplicated")), Is.EqualTo(2));

                AssertIssue(issues, dayNight, WorldValidationSeverity.Error, "SunColorOverDay gradient is missing.");
                AssertIssue(issues, dayNight, WorldValidationSeverity.Error, "SunIntensityOverDay curve is empty.");
                AssertIssue(issues, dayNight, WorldValidationSeverity.Error, "AmbientColorOverDay gradient is missing.");
                AssertIssue(issues, dayNight, WorldValidationSeverity.Error, "AmbientIntensityOverDay curve is empty.");
                AssertIssue(issues, dayNight, WorldValidationSeverity.Error, "FogColorOverDay gradient is missing.");
                AssertIssue(issues, dayNight, WorldValidationSeverity.Error, "MaxSunIntensity must not be negative.");
                AssertIssue(issues, dayNight, WorldValidationSeverity.Error, "SunriseAngle must be between 0 inclusive and 360 exclusive.");
                AssertIssue(issues, dayNight, WorldValidationSeverity.Error, "SunsetAngle must be between 0 inclusive and 360 exclusive.");
                AssertIssue(issues, dayNight, WorldValidationSeverity.Warning, "SkyboxMaterial is not assigned.");
            }
            finally
            {
                Object.DestroyImmediate(eventA);
                Object.DestroyImmediate(eventB);
                Object.DestroyImmediate(sameDayEvent);
                Object.DestroyImmediate(weeklyEvent);
                Object.DestroyImmediate(weatherA);
                Object.DestroyImmediate(weatherB);
                Object.DestroyImmediate(dayNight);
            }
        }

        private static void AssertIssue(
            IEnumerable<WorldValidationIssue> issues,
            ScriptableObject asset,
            WorldValidationSeverity severity,
            string message)
        {
            Assert.That(
                issues.Any(issue =>
                    issue.Asset == asset &&
                    issue.Severity == severity &&
                    issue.Message == message),
                Is.True,
                message);
        }
    }
}
