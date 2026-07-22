using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Character.Exploration;

namespace ZeroEngine.Character.Tests.Editor
{
    [TestFixture]
    public sealed class ExplorationMotorMathTests
    {
        [TestCase(false, 1f, true, true, ExplorationLocomotionMode.Idle)]
        [TestCase(true, 0.1f, true, true, ExplorationLocomotionMode.Idle)]
        [TestCase(true, 1f, false, true, ExplorationLocomotionMode.Walk)]
        [TestCase(true, 1f, true, false, ExplorationLocomotionMode.Walk)]
        [TestCase(true, 1f, true, true, ExplorationLocomotionMode.Run)]
        public void ResolveLocomotion_UsesIntentAndControlContract(
            bool canMove,
            float inputMagnitude,
            bool sprintHeld,
            bool canSprint,
            ExplorationLocomotionMode expected)
        {
            var actual = ExplorationMotorMath.ResolveLocomotion(
                canMove,
                inputMagnitude,
                0.1f,
                sprintHeld,
                canSprint);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void GetSubstepCount_LargeDisplacement_ClampsToConfiguredMaximum()
        {
            var actual = ExplorationMotorMath.GetSubstepCount(3f, 0.25f, 6);

            Assert.That(actual, Is.EqualTo(6));
        }

        [Test]
        public void IsBlocked_MeaningfulRequestWithLowActualVelocity_ReturnsTrue()
        {
            var actual = ExplorationMotorMath.IsBlocked(
                new Vector3(4f, 0f, 0f),
                new Vector3(0.1f, 0f, 0f),
                0.5f,
                0.2f);

            Assert.That(actual, Is.True);
        }

        [Test]
        public void IntegrateVerticalVelocity_GroundedWhileFalling_UsesGroundStickVelocity()
        {
            var actual = ExplorationMotorMath.IntegrateVerticalVelocity(
                -10f,
                true,
                -2f,
                -19.62f,
                20f,
                0.016f);

            Assert.That(actual, Is.EqualTo(-2f).Within(0.0001f));
        }
    }
}
