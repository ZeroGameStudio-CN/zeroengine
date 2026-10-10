using NUnit.Framework;
using ZeroEngine.Multiplayer.Local;

namespace ZeroEngine.Multiplayer.Tests
{
    public sealed class LocalMultiplayerLaunchArgumentsTests
    {
        [Test]
        public void Parse_ExplicitClientArgumentsBuildsDescriptor()
        {
            string[] arguments =
            {
                "player.exe",
                LocalMultiplayerLaunchArguments.RoleOption, "client",
                LocalMultiplayerLaunchArguments.AddressOption, "192.168.1.20",
                LocalMultiplayerLaunchArguments.PortOption, "7788",
                LocalMultiplayerLaunchArguments.RoomOption, "room-a",
                LocalMultiplayerLaunchArguments.SessionOption, "session-a",
                LocalMultiplayerLaunchArguments.LocalNameOption, "Player Two",
                LocalMultiplayerLaunchArguments.ExitOnReadyOption
            };

            OperationResult<LocalDevelopmentRoomOptions> result = LocalMultiplayerLaunchArguments.Parse(
                arguments,
                TestData.Compatibility,
                "1");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Value.Role, Is.EqualTo(LocalMultiplayerRole.Client));
            Assert.That(result.Value.Address, Is.EqualTo("192.168.1.20"));
            Assert.That(result.Value.Port, Is.EqualTo(7788));
            Assert.That(result.Value.RoomId.Value, Is.EqualTo("room-a"));
            Assert.That(result.Value.LocalUser.DisplayName, Is.EqualTo("Player Two"));
            Assert.That(result.Value.ExitOnReady, Is.True);
        }

        [Test]
        public void Parse_MissingRoleFailsStably()
        {
            OperationResult<LocalDevelopmentRoomOptions> result = LocalMultiplayerLaunchArguments.Parse(
                new[] { "player.exe" },
                TestData.Compatibility,
                "1");

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(MultiplayerErrorCode.InvalidArgument));
            Assert.That(result.MessageKey, Is.EqualTo("multiplayer.local.argument_role_invalid"));
        }

        [Test]
        public void Build_QuotesDisplayNamesAndIncludesCompatibility()
        {
            PlatformUser host = new PlatformUser(new PlatformUserId("local-host"), "Local Host");
            PlatformUser client = new PlatformUser(new PlatformUserId("local-client"), "Local Client");
            LocalDevelopmentRoomOptions options = new LocalDevelopmentRoomOptions(
                LocalMultiplayerRole.Host,
                "127.0.0.1",
                7770,
                new RoomId("local-room"),
                new SessionId("local-session"),
                1,
                host,
                host,
                client,
                TestData.Compatibility,
                "1",
                2,
                RoomVisibility.Private,
                true);

            string result = LocalMultiplayerLaunchArguments.Build(options);

            StringAssert.Contains("--ze-multiplayer-local-name \"Local Host\"", result);
            StringAssert.Contains("--ze-multiplayer-product test-product", result);
            StringAssert.Contains(LocalMultiplayerLaunchArguments.ExitOnReadyOption, result);
        }
    }
}
