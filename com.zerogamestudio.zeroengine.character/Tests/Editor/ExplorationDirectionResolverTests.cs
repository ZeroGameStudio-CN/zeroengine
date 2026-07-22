using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Character.Exploration;

namespace ZeroEngine.Character.Tests.Editor
{
    [TestFixture]
    public sealed class ExplorationDirectionResolverTests
    {
        [TestCase(0f, 1f, Facing8.North)]
        [TestCase(1f, 1f, Facing8.NorthEast)]
        [TestCase(1f, 0f, Facing8.East)]
        [TestCase(1f, -1f, Facing8.SouthEast)]
        [TestCase(0f, -1f, Facing8.South)]
        [TestCase(-1f, -1f, Facing8.SouthWest)]
        [TestCase(-1f, 0f, Facing8.West)]
        [TestCase(-1f, 1f, Facing8.NorthWest)]
        public void ResolveFacing8_CardinalAndDiagonalInput_ReturnsStableSemanticFacing(
            float x,
            float y,
            Facing8 expected)
        {
            var actual = ExplorationDirectionResolver.ResolveFacing8(
                new Vector2(x, y),
                0.1f,
                Facing8.South);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ResolveFacing8_InputInsideDeadZone_PreservesLastFacing()
        {
            var actual = ExplorationDirectionResolver.ResolveFacing8(
                new Vector2(0.05f, 0.05f),
                0.1f,
                Facing8.NorthWest);

            Assert.That(actual, Is.EqualTo(Facing8.NorthWest));
        }

        [Test]
        public void NormalizeInput_DiagonalInput_DoesNotExceedUnitMagnitude()
        {
            var actual = ExplorationDirectionResolver.NormalizeInput(Vector2.one, 0.1f);

            Assert.That(actual.magnitude, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void MapToFour_DiagonalInsideTieBand_HoldsCompatibleLastVisualFacing()
        {
            var actual = ExplorationDirectionResolver.MapToFour(
                new Vector2(0.72f, 0.69f),
                0.08f,
                true,
                VisualFacing4.East,
                FourDirectionTieBreakAxis.Vertical);

            Assert.That(actual, Is.EqualTo(VisualFacing4.East));
        }

        [Test]
        public void MapToFour_DiagonalWithoutHistory_UsesConfiguredTieBreakAxis()
        {
            var vertical = ExplorationDirectionResolver.MapToFour(
                new Vector2(-1f, 1f),
                0.1f,
                false,
                VisualFacing4.South,
                FourDirectionTieBreakAxis.Vertical);
            var horizontal = ExplorationDirectionResolver.MapToFour(
                new Vector2(-1f, 1f),
                0.1f,
                false,
                VisualFacing4.South,
                FourDirectionTieBreakAxis.Horizontal);

            Assert.That(vertical, Is.EqualTo(VisualFacing4.North));
            Assert.That(horizontal, Is.EqualTo(VisualFacing4.West));
        }
    }
}
