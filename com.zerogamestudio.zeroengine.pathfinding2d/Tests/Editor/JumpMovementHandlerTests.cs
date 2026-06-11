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
        public void CalculateFall_WhenReachable_ReturnsTrajectoryEndingAtTarget()
        {
            Vector2 start = new Vector2(0f, 6f);
            Vector2 end = new Vector2(3f, 0f);

            var result = JumpMovementHandler.CalculateFall(start, end, gravityScale: 1f);

            Assert.IsTrue(result.IsReachable);
            Assert.IsNotNull(result.Trajectory);
            Assert.GreaterOrEqual(result.Trajectory.Length, 2);
            Assert.That(result.Trajectory[0].x, Is.EqualTo(start.x).Within(0.001f));
            Assert.That(result.Trajectory[0].y, Is.EqualTo(start.y).Within(0.001f));
            Assert.That(result.Trajectory[result.Trajectory.Length - 1].x, Is.EqualTo(end.x).Within(0.001f));
            Assert.That(result.Trajectory[result.Trajectory.Length - 1].y, Is.EqualTo(end.y).Within(0.001f));
        }

        [Test]
        public void CalculateJump_UpwardTargetAtMaxVelocityWithOvershoot_ReturnsReachable()
        {
            const float gravityScale = 5f;
            const float targetHeight = 4f;
            float gravity = Mathf.Abs(Physics2D.gravity.y) * gravityScale;
            float maxJumpVelocity = Mathf.Sqrt(2f * gravity * targetHeight);

            var result = JumpMovementHandler.CalculateJump(
                Vector2.zero,
                new Vector2(0f, targetHeight),
                maxJumpVelocity,
                gravityScale,
                overshoot: 1.2f);

            Assert.IsTrue(
                result.IsReachable,
                "Overshoot should not reject an upward jump that exactly matches the actor's maximum jump velocity.");
            Assert.LessOrEqual(
                result.VelocityY,
                maxJumpVelocity + 0.001f,
                "The calculated jump must stay within the actor's configured maximum jump velocity.");
        }

        [Test]
        public void ValidateTrajectory_FromToPlatformSideGrazingWithoutCenterPenetration_AllowsTrajectory()
        {
            const int platformLayer = 8;
            var platform = CreateBoxColliderPlatform(
                "SelfPlatformSideGrazing",
                platformLayer,
                new Vector2(2f, 1.5f),
                new Vector2(0.2f, 3f));

            try
            {
                var trajectory = new[]
                {
                    new Vector2(1.55f, 0.2f),
                    new Vector2(1.55f, 1.5f),
                    new Vector2(1.55f, 2.8f)
                };

                bool valid = JumpMovementHandler.ValidateTrajectory(
                    trajectory,
                    1 << platformLayer,
                    colliderRadius: 0.4f,
                    fromPlatform: platform,
                    toPlatform: platform,
                    ignoreInitialDistance: 0.45f);

                Assert.IsTrue(
                    valid,
                    "A trajectory that only grazes the from/to platform with its radius should remain valid when the sample center never enters the collider.");
            }
            finally
            {
                Object.DestroyImmediate(platform.gameObject);
            }
        }

        [Test]
        public void ValidateTrajectory_ThirdPartySideGrazing_BlocksTrajectory()
        {
            const int platformLayer = 8;
            var fromPlatform = CreateBoxColliderPlatform(
                "FromPlatform",
                platformLayer,
                new Vector2(-2f, 0f),
                new Vector2(1f, 0.2f));
            var toPlatform = CreateBoxColliderPlatform(
                "ToPlatform",
                platformLayer,
                new Vector2(4f, 0f),
                new Vector2(1f, 0.2f));
            var blocker = CreateBoxColliderPlatform(
                "ThirdPartySideGrazingBlocker",
                platformLayer,
                new Vector2(2f, 1.5f),
                new Vector2(0.2f, 3f));

            try
            {
                var trajectory = new[]
                {
                    new Vector2(1.55f, 0.2f),
                    new Vector2(1.55f, 1.5f),
                    new Vector2(1.55f, 2.8f)
                };

                bool valid = JumpMovementHandler.ValidateTrajectory(
                    trajectory,
                    1 << platformLayer,
                    colliderRadius: 0.4f,
                    fromPlatform: fromPlatform,
                    toPlatform: toPlatform,
                    ignoreInitialDistance: 0.45f);

                Assert.IsFalse(
                    valid,
                    "Only from/to platform self-grazing should be relaxed; third-party blockers must still reject the trajectory.");
            }
            finally
            {
                Object.DestroyImmediate(fromPlatform.gameObject);
                Object.DestroyImmediate(toPlatform.gameObject);
                Object.DestroyImmediate(blocker.gameObject);
            }
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

        private static Collider2D CreateBoxColliderPlatform(
            string name,
            int layer,
            Vector2 center,
            Vector2 size)
        {
            var platform = new GameObject(name) { layer = layer };
            platform.transform.position = center;
            var collider = platform.AddComponent<BoxCollider2D>();
            collider.size = size;
            Physics2D.SyncTransforms();
            return collider;
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
