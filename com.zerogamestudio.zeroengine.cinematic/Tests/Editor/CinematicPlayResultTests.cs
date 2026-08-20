using NUnit.Framework;

namespace ZeroEngine.Cinematic.Tests
{
    public sealed class CinematicPlayResultTests
    {
        [TestCase(CinematicPlayStatus.Completed, true)]
        [TestCase(CinematicPlayStatus.SkippedCompleted, true)]
        [TestCase(CinematicPlayStatus.Started, false)]
        [TestCase(CinematicPlayStatus.Aborted, false)]
        [TestCase(CinematicPlayStatus.Failed, false)]
        public void Succeeded_ReturnsTrueOnlyForSuccessfulTerminalStatuses(
            CinematicPlayStatus status,
            bool expected)
        {
            var result = new CinematicPlayResult(status);
            var property = typeof(CinematicPlayResult).GetProperty("Succeeded");

            Assert.IsNotNull(property);
            Assert.AreEqual(expected, property.GetValue(result));
        }
    }
}
