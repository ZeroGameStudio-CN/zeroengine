using NUnit.Framework;
using ZeroEngine.Calendar;

namespace ZeroEngine.World.Editor.Tests
{
    public sealed class CalendarContractTests
    {
        [Test]
        public void GameDateAddDaysCrossesMonthAndYearBoundaries()
        {
            var date = new GameDate(1, 1, 1);

            Assert.AreEqual(new GameDate(1, 1, 30), date.AddDays(29));
            Assert.AreEqual(new GameDate(1, 2, 1), date.AddDays(30));
            Assert.AreEqual(new GameDate(2, 1, 1), date.AddDays(360));
        }

        [Test]
        public void GameDateConstructorClampsInvalidMonthAndDay()
        {
            var date = new GameDate(1, 99, -5);

            Assert.AreEqual(12, date.Month);
            Assert.AreEqual(1, date.Day);
            Assert.AreEqual(Season.Winter, date.Season);
        }

        [Test]
        public void OneTimeEventIsActiveBetweenStartAndEndDates()
        {
            var evt = new CalendarEventData
            {
                Type = CalendarEventType.OneTime,
                StartDate = new GameDate(1, 2, 5),
                EndDate = new GameDate(1, 2, 7)
            };

            Assert.IsFalse(evt.IsActiveOn(new GameDate(1, 2, 4)));
            Assert.IsTrue(evt.IsActiveOn(new GameDate(1, 2, 6)));
            Assert.IsFalse(evt.IsActiveOn(new GameDate(1, 2, 8)));
        }

        [Test]
        public void WeeklyEventUsesConfiguredDayOfWeek()
        {
            var activeDate = new GameDate(1, 3, 10);
            var inactiveDate = activeDate.AddDays(1);
            var evt = new CalendarEventData
            {
                Type = CalendarEventType.Weekly,
                StartDate = new GameDate(1, 1, 1)
            };
            evt.RecurrenceDays.Add(activeDate.DayOfWeek);

            Assert.IsTrue(evt.IsActiveOn(activeDate));
            Assert.IsFalse(evt.IsActiveOn(inactiveDate));
        }

        [Test]
        public void SeasonalEventFollowsStartDateSeasonAcrossYears()
        {
            var evt = new CalendarEventData
            {
                Type = CalendarEventType.Seasonal,
                StartDate = new GameDate(1, 4, 1)
            };

            Assert.IsTrue(evt.IsActiveOn(new GameDate(2, 5, 1)));
            Assert.IsFalse(evt.IsActiveOn(new GameDate(2, 10, 1)));
        }
    }
}
