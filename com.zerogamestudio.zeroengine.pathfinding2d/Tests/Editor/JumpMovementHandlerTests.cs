using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D.Tests.Editor
{
    [TestFixture]
    public class JumpMovementHandlerTests
    {
        [Test]
        public void CalculateJump_WhenRequiredHorizontalSpeedExceedsAirLimit_ReturnsNotReachable()
        {
            var unlimited = JumpMovementHandler.CalculateJump(
                new Vector2(0f, 0f),
                new Vector2(8f, 0f),
                maxJumpVelocity: 20f,
                gravityScale: 1f,
                overshoot: 1f);

            var limited = JumpMovementHandler.CalculateJump(
                new Vector2(0f, 0f),
                new Vector2(8f, 0f),
                maxJumpVelocity: 20f,
                gravityScale: 1f,
                overshoot: 1f,
                maxAirHorizontalSpeed: 3f);

            Assert.IsTrue(unlimited.IsReachable, "Baseline jump should be reachable without an air-speed cap.");
            Assert.IsFalse(limited.IsReachable, "Jump links must not require more horizontal air speed than the mover can apply.");
        }

        [Test]
        public void CalculateFall_WhenRequiredHorizontalSpeedExceedsAirLimit_ReturnsNotReachable()
        {
            var unlimited = JumpMovementHandler.CalculateFall(
                new Vector2(0f, 6f),
                new Vector2(8f, 0f),
                gravityScale: 1f);

            var limited = JumpMovementHandler.CalculateFall(
                new Vector2(0f, 6f),
                new Vector2(8f, 0f),
                gravityScale: 1f,
                maxAirHorizontalSpeed: 3f);

            Assert.IsTrue(unlimited.IsReachable, "Baseline fall should be reachable without an air-speed cap.");
            Assert.IsFalse(limited.IsReachable, "Fall links must not require more horizontal air speed than the mover can apply.");
        }

        [Test]
        public void ValidateTrajectory_SameColliderMiddlePlatform_BlocksTrajectory()
        {
            const int platformLayer = 8;
            var platform = CreateMultiPathPolygonPlatform(
                "SameColliderTrajectoryBlocker",
                platformLayer,
                (new Vector2(0f, 0f), new Vector2(2f, 0.2f)),
                (new Vector2(2f, 1.5f), new Vector2(2f, 0.2f)),
                (new Vector2(4f, 0f), new Vector2(2f, 0.2f)));

            try
            {
                var trajectory = new[]
                {
                    new Vector2(0f, 0.35f),
                    new Vector2(2f, 1.55f),
                    new Vector2(4f, 0.35f)
                };

                bool valid = JumpMovementHandler.ValidateTrajectory(
                    trajectory,
                    1 << platformLayer,
                    colliderRadius: 0.2f,
                    fromPlatform: platform,
                    toPlatform: platform,
                    ignoreInitialDistance: 0.45f);

                Assert.IsFalse(
                    valid,
                    "A multi-path PolygonCollider should only be ignored near the start/end, not for a middle platform segment.");
            }
            finally
            {
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        private static PolygonCollider2D CreateMultiPathPolygonPlatform(
            string name,
            int layer,
            params (Vector2 center, Vector2 size)[] platforms)
        {
            var platform = new GameObject(name) { layer = layer };
            var collider = platform.AddComponent<PolygonCollider2D>();
            collider.pathCount = platforms.Length;

            for (int i = 0; i < platforms.Length; i++)
            {
                var rect = platforms[i];
                float halfWidth = rect.size.x * 0.5f;
                float halfHeight = rect.size.y * 0.5f;
                collider.SetPath(i, new[]
                {
                    new Vector2(rect.center.x - halfWidth, rect.center.y - halfHeight),
                    new Vector2(rect.center.x - halfWidth, rect.center.y + halfHeight),
                    new Vector2(rect.center.x + halfWidth, rect.center.y + halfHeight),
                    new Vector2(rect.center.x + halfWidth, rect.center.y - halfHeight)
                });
            }

            Physics2D.SyncTransforms();
            return collider;
        }
    }
}
