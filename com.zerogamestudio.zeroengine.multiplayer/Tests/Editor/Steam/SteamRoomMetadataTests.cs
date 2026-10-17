using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.Multiplayer.Steam;

namespace ZeroEngine.Multiplayer.Tests.Steam
{
    public sealed class SteamRoomMetadataTests
    {
        [Test]
        public void RoomService_WhenSteamIsOffline_ConstructsWithoutInitializing()
        {
            FakeSteamRuntime runtime = new FakeSteamRuntime();
            SteamRoomService service = null;

            Assert.DoesNotThrow(() => service = new SteamRoomService(runtime, "ze_"));
            Assert.That(runtime.EnsureInitializedCalls, Is.EqualTo(0));
            Assert.That(service.IsAvailable, Is.False);
            Assert.That(service.UnavailableReasonKey, Is.EqualTo("multiplayer.steam.offline"));

            service.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void CreateAndRead_RoundTripsCompatibilityAndAuthority()
        {
            CompatibilityDescriptor compatibility = new CompatibilityDescriptor(
                "test-product",
                "game-1",
                "content-1",
                "build-1",
                "room-config");
            RoomCreateOptions options = new RoomCreateOptions(
                RoomVisibility.FriendsOnly,
                2,
                false,
                "1",
                compatibility);
            IReadOnlyDictionary<string, string> values = SteamRoomMetadata.Create(
                "ze_",
                options,
                new SessionId("session-1"),
                3,
                new PlatformUserId("76561198000000000"));

            OperationResult<SteamRoomDescriptor> result = SteamRoomMetadata.Read(
                "ze_",
                key => values.ContainsKey(key) ? values[key] : string.Empty);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Value.ProductId, Is.EqualTo("test-product"));
            Assert.That(result.Value.SessionGeneration, Is.EqualTo(3));
            Assert.That(result.Value.HostId.Value, Is.EqualTo("76561198000000000"));
            Assert.That(result.Value.Visibility, Is.EqualTo(RoomVisibility.FriendsOnly));
            Assert.That(result.Value.IsJoinable, Is.True);
        }

        [Test]
        public void Read_UnknownStateFailsStably()
        {
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                ["ze_product"] = "product",
                ["ze_protocol"] = "1",
                ["ze_game_protocol"] = "1",
                ["ze_content"] = "content",
                ["ze_build"] = "build",
                ["ze_room"] = "room",
                ["ze_session"] = "session",
                ["ze_generation"] = "1",
                ["ze_state"] = "future-state",
                ["ze_host"] = "76561198000000000",
                ["ze_visibility"] = "friends",
                ["ze_joinable"] = "1"
            };

            OperationResult<SteamRoomDescriptor> result = SteamRoomMetadata.Read(
                "ze_",
                key => values.ContainsKey(key) ? values[key] : string.Empty);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.MessageKey, Is.EqualTo("multiplayer.steam.metadata_state_invalid"));
        }

        [Test]
        public void Read_InGameJoinabilityUsesExplicitMetadata()
        {
            RoomCreateOptions options = new RoomCreateOptions(
                RoomVisibility.FriendsOnly,
                2,
                true,
                "1",
                new CompatibilityDescriptor("product", "game", "content", "build", "room"));
            IReadOnlyDictionary<string, string> created = SteamRoomMetadata.Create(
                "ze_",
                options,
                new SessionId("session"),
                2,
                new PlatformUserId("76561198000000000"));
            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> pair in created)
            {
                values.Add(pair.Key, pair.Value);
            }
            values["ze_state"] = "ingame";
            values["ze_joinable"] = "1";

            OperationResult<SteamRoomDescriptor> result = SteamRoomMetadata.Read(
                "ze_",
                key => values.ContainsKey(key) ? values[key] : string.Empty);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Value.HasStarted, Is.True);
            Assert.That(result.Value.IsJoinable, Is.True);
        }

        private sealed class FakeSteamRuntime : ISteamRuntime
        {
            public bool IsAvailable => false;
            public string UnavailableReasonKey => "multiplayer.steam.offline";
            public PlatformUser LocalUser => default(PlatformUser);
            public int EnsureInitializedCalls { get; private set; }

            public OperationResult EnsureInitialized()
            {
                EnsureInitializedCalls++;
                return OperationResult.Failure(
                    MultiplayerErrorCode.PlatformUnavailable,
                    UnavailableReasonKey);
            }
        }
    }
}
