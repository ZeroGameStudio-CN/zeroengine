using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D.Tests.Editor
{
    [TestFixture]
    public class JumpMovementHandlerTests
    {
        [Test]
        public void CalculateJump_WithHorizontalSpeedLimit_UsesHigherArcWhenNeeded()
        {
            const float maxJumpVelocity = 20f;
            const float gravityScale = 5f;
            const float overshoot = 1.2f;
            const float maxHorizontalSpeed = 11f;

            var start = new Vector2(0f, 0f);
            var end = new Vector2(6f, 2f);
            float gravity = 9.81f * gravityScale;
            float minimumHeight = (end.y - start.y) * overshoot;
            float lowArcVelocityY = Mathf.Sqrt(2f * gravity * minimumHeight);

            var result = JumpMovementHandler.CalculateJump(
                start,
                end,
                maxJumpVelocity,
                gravityScale,
                overshoot,
                maxHorizontalSpeed);

            Assert.IsTrue(result.IsReachable);
            Assert.LessOrEqual(Mathf.Abs(result.VelocityX), maxHorizontalSpeed + 0.05f);
            Assert.Greater(result.VelocityY, lowArcVelocityY + 0.1f);
        }

        [Test]
        public void CalculateJump_WithHorizontalSpeedLimit_RejectsWhenMaxArcIsTooShort()
        {
            var result = JumpMovementHandler.CalculateJump(
                new Vector2(0f, 0f),
                new Vector2(12f, 2f),
                maxJumpVelocity: 20f,
                gravityScale: 5f,
                overshoot: 1.2f,
                maxHorizontalSpeed: 11f);

            Assert.IsFalse(result.IsReachable);
        }

        [Test]
        public void CalculateJump_WithoutHorizontalSpeedLimit_PreservesLowArcBehavior()
        {
            var result = JumpMovementHandler.CalculateJump(
                new Vector2(0f, 0f),
                new Vector2(6f, 2f),
                maxJumpVelocity: 20f,
                gravityScale: 5f,
                overshoot: 1.2f);

            Assert.IsTrue(result.IsReachable);
            Assert.Greater(Mathf.Abs(result.VelocityX), 11f);
        }

        [Test]
        public void CalculateFall_WithHorizontalSpeedLimit_RejectsShortFallAcrossWideGap()
        {
            var result = JumpMovementHandler.CalculateFall(
                new Vector2(0f, 1f),
                new Vector2(4f, 0f),
                gravityScale: 5f,
                maxHorizontalSpeed: 11f);

            Assert.IsFalse(result.IsReachable);
        }

        [Test]
        public void ValidateTrajectory_TargetPlatformBodyBlocksBeforeLanding()
        {
            var platform = new GameObject("TargetBodyBlocker");
            try
            {
                var collider = platform.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(4f, 1f);
                platform.transform.position = new Vector3(0f, 2f, 0f);
                Physics2D.SyncTransforms();

                var trajectory = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1.5f),
                    new Vector2(0f, 2.5f),
                    new Vector2(0f, 4f)
                };

                bool isClear = JumpMovementHandler.ValidateTrajectory(
                    trajectory,
                    1 << platform.layer,
                    0.1f,
                    fromPlatform: null,
                    toPlatform: collider,
                    ignoreInitialDistance: 0f);

                Assert.IsFalse(isClear, "The endpoint collider should only be ignored near the landing point, not through its whole body.");
            }
            finally
            {
                Object.DestroyImmediate(platform);
            }
        }
    }
}
