using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using ZeroEngine.Multiplayer.Local;

namespace ZeroEngine.Multiplayer.Tests
{
    public sealed class LocalDevelopmentRoomServiceTests
    {
        [Test]
        public void HostCreate_ProducesSteamShapedDescriptorWithConnectionAddress()
        {
            LocalDevelopmentRoomOptions options = CreateOptions(LocalMultiplayerRole.Host);
            LocalDevelopmentRoomService service = new LocalDevelopmentRoomService(options);
            RoomCreateOptions create = new RoomCreateOptions(
                RoomVisibility.Private,
                2,
                false,
                options.ProtocolVersion,
                options.Compatibility);

            OperationResult<RoomSnapshot> result = service.CreateAsync(create, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Value.Id, Is.EqualTo(options.RoomId));
            Assert.That(result.Value.SessionId, Is.EqualTo(options.SessionId));
            Assert.That(result.Value.ConnectionAddress, Is.EqualTo("127.0.0.1"));
            Assert.That(result.Value.Members.Count, Is.EqualTo(1));
            Assert.That(result.Value.Members[0].IsHost, Is.True);
        }

        [Test]
        public void ClientJoin_ProducesHostAndLocalMember()
        {
            LocalDevelopmentRoomOptions options = CreateOptions(LocalMultiplayerRole.Client);
            LocalDevelopmentRoomService service = new LocalDevelopmentRoomService(options);

            OperationResult<RoomSnapshot> result = service.JoinAsync(options.RoomId, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Value.HostId, Is.EqualTo(options.HostUser.Id));
            Assert.That(result.Value.Members.Count, Is.EqualTo(2));
            Assert.That(result.Value.Members[1].User.Id, Is.EqualTo(options.LocalUser.Id));
        }

        [Test]
        public void ClientJoin_DifferentRoomFailsAsRoomNotFound()
        {
            LocalDevelopmentRoomOptions options = CreateOptions(LocalMultiplayerRole.Client);
            LocalDevelopmentRoomService service = new LocalDevelopmentRoomService(options);

            OperationResult<RoomSnapshot> result = service.JoinAsync(
                    new RoomId("different"),
                    CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(MultiplayerErrorCode.RoomNotFound));
        }

        [Test]
        public void AttachedDriver_PeerConnectedAddsMemberAndRaisesEvent()
        {
            LocalDevelopmentRoomOptions options = CreateOptions(LocalMultiplayerRole.Host);
            LocalDevelopmentRoomService service = new LocalDevelopmentRoomService(options);
            FakeConnectionDriver driver = new FakeConnectionDriver();
            service.AttachConnectionDriver(driver);
            service.CreateAsync(
                    new RoomCreateOptions(
                        RoomVisibility.Private,
                        2,
                        false,
                        options.ProtocolVersion,
                        options.Compatibility),
                    CancellationToken.None)
                .GetAwaiter().GetResult();
            List<PlatformRoomEvent> events = new List<PlatformRoomEvent>();
            service.RoomEvent += events.Add;

            driver.Emit(new ConnectionEvent(
                ConnectionEventType.PeerConnected,
                options.ExpectedRemoteUser.Id,
                MultiplayerErrorCode.None,
                string.Empty));

            Assert.That(service.CurrentRoom.Members.Count, Is.EqualTo(2));
            Assert.That(service.CurrentRoom.Members[1].ConnectionPhase, Is.EqualTo(MemberConnectionPhase.Connected));
            Assert.That(events.Exists(item => item.Type == RoomEventType.MemberJoined), Is.True);
        }

        [Test]
        public void HostAuthorization_OnlyAcceptsConfiguredRemoteIdentity()
        {
            LocalDevelopmentRoomOptions options = CreateOptions(LocalMultiplayerRole.Host);
            LocalDevelopmentRoomService service = new LocalDevelopmentRoomService(options);
            service.CreateAsync(
                    new RoomCreateOptions(
                        RoomVisibility.Private,
                        2,
                        false,
                        options.ProtocolVersion,
                        options.Compatibility),
                    CancellationToken.None)
                .GetAwaiter().GetResult();

            OperationResult accepted = service.AuthorizeRemoteUser(options.ExpectedRemoteUser.Id);
            OperationResult rejected = service.AuthorizeRemoteUser(new PlatformUserId("intruder"));

            Assert.That(accepted.Succeeded, Is.True);
            Assert.That(rejected.ErrorCode, Is.EqualTo(MultiplayerErrorCode.UnauthorizedPeer));
        }

        [Test]
        public void Leave_ClearsCurrentRoom()
        {
            LocalDevelopmentRoomOptions options = CreateOptions(LocalMultiplayerRole.Host);
            LocalDevelopmentRoomService service = new LocalDevelopmentRoomService(options);
            service.CreateAsync(
                    new RoomCreateOptions(
                        RoomVisibility.Private,
                        2,
                        false,
                        options.ProtocolVersion,
                        options.Compatibility),
                    CancellationToken.None)
                .GetAwaiter().GetResult();

            OperationResult result = service.LeaveAsync(CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(service.CurrentRoom, Is.Null);
        }

        private static LocalDevelopmentRoomOptions CreateOptions(LocalMultiplayerRole role)
        {
            PlatformUser host = new PlatformUser(new PlatformUserId("local-host"), "Host");
            PlatformUser client = new PlatformUser(new PlatformUserId("local-client"), "Client");
            return new LocalDevelopmentRoomOptions(
                role,
                "127.0.0.1",
                7770,
                new RoomId("local-room"),
                new SessionId("local-session"),
                1,
                role == LocalMultiplayerRole.Host ? host : client,
                host,
                role == LocalMultiplayerRole.Host ? client : host,
                TestData.Compatibility,
                "1",
                2,
                RoomVisibility.Private);
        }
    }
}
