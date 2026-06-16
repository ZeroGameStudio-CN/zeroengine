using NUnit.Framework;

namespace ZeroEngine.Pathfinding2D.Tests.Editor
{
    [TestFixture]
    public class PlatformLinkDataTests
    {
        [Test]
        public void CreateJump_IncludesHorizontalVelocityInCost()
        {
            var slowHorizontalJump = PlatformLinkData.CreateJump(
                from: 1,
                to: 2,
                velocityY: 10f,
                velocityX: 1f,
                duration: 0.8f);
            var fastHorizontalJump = PlatformLinkData.CreateJump(
                from: 1,
                to: 2,
                velocityY: 10f,
                velocityX: 5f,
                duration: 0.8f);

            Assert.AreEqual(2.1f, slowHorizontalJump.Cost, 0.0001f);
            Assert.AreEqual(4.1f, fastHorizontalJump.Cost, 0.0001f);
            Assert.Greater(fastHorizontalJump.Cost, slowHorizontalJump.Cost);
        }
    }
}
