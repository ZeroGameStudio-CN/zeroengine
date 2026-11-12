using NUnit.Framework;
using ZeroEngine.World.Presentation;

namespace ZeroEngine.World.Tests.Editor.Presentation
{
    public sealed class WorldCellPresentationStabilityResultTests
    {
        [Test]
        public void SucceededResult_NormalizesCellIdAndReportsSuccess()
        {
            var result = WorldCellPresentationStabilityResult.SucceededResult(" longleji-street ");

            Assert.That(result.Status, Is.EqualTo(WorldCellPresentationStabilityStatus.Succeeded));
            Assert.That(result.CellId, Is.EqualTo("longleji-street"));
            Assert.That(result.Message, Is.Empty);
            Assert.That(result.IsSuccess, Is.True);
        }

        [Test]
        public void FailedAndCancelled_NormalizeMessageAndDoNotReportSuccess()
        {
            var failed = WorldCellPresentationStabilityResult.Failed(" kiln ", " shader warmup failed ");
            var cancelled = WorldCellPresentationStabilityResult.Cancelled(" bank ", null);

            Assert.That(failed.Status, Is.EqualTo(WorldCellPresentationStabilityStatus.Failed));
            Assert.That(failed.CellId, Is.EqualTo("kiln"));
            Assert.That(failed.Message, Is.EqualTo("shader warmup failed"));
            Assert.That(failed.IsSuccess, Is.False);

            Assert.That(cancelled.Status, Is.EqualTo(WorldCellPresentationStabilityStatus.Cancelled));
            Assert.That(cancelled.CellId, Is.EqualTo("bank"));
            Assert.That(cancelled.Message, Is.Empty);
            Assert.That(cancelled.IsSuccess, Is.False);
        }

        [Test]
        public void DefaultResult_IsUnknownAndFailsClosed()
        {
            var result = default(WorldCellPresentationStabilityResult);

            Assert.That(result.Status, Is.EqualTo(WorldCellPresentationStabilityStatus.Unknown));
            Assert.That(result.CellId, Is.Empty);
            Assert.That(result.Message, Is.Empty);
            Assert.That(result.IsSuccess, Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t ")]
        public void StaticFactories_EmptyCellId_ThrowArgumentException(string cellId)
        {
            Assert.Throws<System.ArgumentException>(() =>
                WorldCellPresentationStabilityResult.SucceededResult(cellId));
            Assert.Throws<System.ArgumentException>(() =>
                WorldCellPresentationStabilityResult.Failed(cellId, "failed"));
            Assert.Throws<System.ArgumentException>(() =>
                WorldCellPresentationStabilityResult.Cancelled(cellId));
        }

        [Test]
        public void StatusValues_AreExplicitAndOnlySucceededReportsSuccess()
        {
            Assert.That((int)WorldCellPresentationStabilityStatus.Unknown, Is.Zero);
            Assert.That((int)WorldCellPresentationStabilityStatus.Succeeded, Is.Not.Zero);
            Assert.That((int)WorldCellPresentationStabilityStatus.Failed, Is.Not.Zero);
            Assert.That((int)WorldCellPresentationStabilityStatus.Cancelled, Is.Not.Zero);

            Assert.That(
                WorldCellPresentationStabilityResult.SucceededResult("cell").IsSuccess,
                Is.True);
            Assert.That(
                WorldCellPresentationStabilityResult.Failed("cell", "failed").IsSuccess,
                Is.False);
            Assert.That(
                WorldCellPresentationStabilityResult.Cancelled("cell").IsSuccess,
                Is.False);
        }
    }
}
