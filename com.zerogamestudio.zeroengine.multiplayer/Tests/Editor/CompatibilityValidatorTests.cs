using NUnit.Framework;

namespace ZeroEngine.Multiplayer.Tests
{
    public sealed class CompatibilityValidatorTests
    {
        [Test]
        public void MatchingRoom_IsCompatible()
        {
            OperationResult result = CompatibilityValidator.Validate(
                TestData.CreateRoom(TestData.Guest),
                TestData.Compatibility,
                "1",
                BuildMatchPolicy.Exact);

            Assert.IsTrue(result.Succeeded);
        }

        [TestCase("product", MultiplayerErrorCode.ProductMismatch)]
        [TestCase("protocol", MultiplayerErrorCode.ProtocolMismatch)]
        [TestCase("gameProtocol", MultiplayerErrorCode.GameProtocolMismatch)]
        [TestCase("content", MultiplayerErrorCode.ContentMismatch)]
        [TestCase("build", MultiplayerErrorCode.BuildMismatch)]
        [TestCase("gameRoom", MultiplayerErrorCode.GameRoomMismatch)]
        public void Mismatch_ReturnsSpecificError(string field, MultiplayerErrorCode expected)
        {
            RoomSnapshot room = TestData.CreateRoom(
                TestData.Guest,
                productId: field == "product" ? "other" : "test-product",
                protocolVersion: field == "protocol" ? "other" : "1",
                gameProtocolVersion: field == "gameProtocol" ? "other" : "game-1",
                contentRevision: field == "content" ? "other" : "content-1",
                buildVersion: field == "build" ? "other" : "build-1",
                gameRoomId: field == "gameRoom" ? "other" : "gallery-room");

            OperationResult result = CompatibilityValidator.Validate(
                room,
                TestData.Compatibility,
                "1",
                BuildMatchPolicy.Exact);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(expected, result.ErrorCode);
        }

        [Test]
        public void IgnoreBuildPolicy_AllowsBuildDifferenceOnly()
        {
            RoomSnapshot room = TestData.CreateRoom(TestData.Guest, buildVersion: "other-build");

            OperationResult result = CompatibilityValidator.Validate(
                room,
                TestData.Compatibility,
                "1",
                BuildMatchPolicy.Ignore);

            Assert.IsTrue(result.Succeeded);
        }

        [Test]
        public void IncompleteLocalDescriptor_IsInvalidConfiguration()
        {
            CompatibilityDescriptor incomplete = new CompatibilityDescriptor(
                string.Empty,
                "game-1",
                "content-1",
                "build-1",
                "gallery-room");

            OperationResult result = CompatibilityValidator.ValidateDescriptor(incomplete, "1");

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(MultiplayerErrorCode.InvalidConfiguration, result.ErrorCode);
        }
    }
}
