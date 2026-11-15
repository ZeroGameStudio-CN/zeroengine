using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ZeroEngine.Interaction.Core.Authority;
using ZeroEngine.Multiplayer.Replication;

namespace ZeroEngine.Interaction.Multiplayer.Samples
{
    /// <summary>Loopback-only sample transport. Workers move bounded bytes; the caller's Pump alone runs gameplay.</summary>
    internal sealed class DemoConnection : IDisposable
    {
        private readonly TcpClient socket;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly ConcurrentQueue<DemoPacket> outgoing = new ConcurrentQueue<DemoPacket>();
        private readonly SemaphoreSlim ready = new SemaphoreSlim(0);
        private readonly Task reader, writer;
        private int incomingCount, outgoingCount, closed;
        internal readonly ConcurrentQueue<DemoPacket> Incoming = new ConcurrentQueue<DemoPacket>();
        internal bool IsClosed => Volatile.Read(ref closed) != 0;
        internal string Failure { get; private set; }
        internal DemoConnection(TcpClient socket)
        {
            this.socket = socket; socket.NoDelay = true;
            reader = Task.Run(ReadLoop); writer = Task.Run(WriteLoop);
        }
        internal void Send(DemoPacket packet)
        {
            if (IsClosed) return;
            if (Interlocked.Increment(ref outgoingCount) > 128) { Close("Outgoing sample queue overflow."); return; }
            outgoing.Enqueue(packet); ready.Release();
        }
        internal bool TryRead(out DemoPacket packet)
        {
            if (Incoming.TryDequeue(out packet)) { Interlocked.Decrement(ref incomingCount); return true; }
            return false;
        }
        private async Task ReadLoop()
        {
            try
            {
                var stream = socket.GetStream(); var header = new byte[4];
                while (!stop.IsCancellationRequested)
                {
                    await ReadExact(stream, header, stop.Token).ConfigureAwait(false);
                    int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header, 0));
                    if (length <= 0 || length > DemoWire.MaxFrameBytes) throw new InvalidDataException("Invalid sample frame length.");
                    var bytes = new byte[length]; await ReadExact(stream, bytes, stop.Token).ConfigureAwait(false);
                    var packet = DemoWire.Decode<DemoPacket>(bytes);
                    if (packet.Protocol != 1) throw new InvalidDataException("Unsupported sample protocol.");
                    if (Interlocked.Increment(ref incomingCount) > 128) throw new InvalidDataException("Incoming sample queue overflow.");
                    Incoming.Enqueue(packet);
                }
            }
            catch (Exception error) { if (!stop.IsCancellationRequested) Failure = error.Message; }
            finally { Close(Failure); }
        }
        private static async Task ReadExact(NetworkStream stream, byte[] bytes, CancellationToken token)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = await stream.ReadAsync(bytes, offset, bytes.Length - offset, token).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("Sample peer disconnected."); offset += count;
            }
        }
        private async Task WriteLoop()
        {
            try
            {
                var stream = socket.GetStream();
                while (!stop.IsCancellationRequested)
                {
                    await ready.WaitAsync(stop.Token).ConfigureAwait(false);
                    if (!outgoing.TryDequeue(out var packet)) continue;
                    Interlocked.Decrement(ref outgoingCount);
                    var bytes = DemoWire.Encode(packet);
                    var header = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length));
                    await stream.WriteAsync(header, 0, header.Length, stop.Token).ConfigureAwait(false);
                    await stream.WriteAsync(bytes, 0, bytes.Length, stop.Token).ConfigureAwait(false);
                }
            }
            catch (Exception error) { if (!stop.IsCancellationRequested) Failure = error.Message; }
            finally { Close(Failure); }
        }
        private void Close(string failure)
        {
            if (Interlocked.Exchange(ref closed, 1) != 0) return;
            Failure = failure; stop.Cancel(); socket.Dispose();
        }
        public void Dispose() { Close(null); }
        internal bool WorkersEnded => reader.IsCompleted && writer.IsCompleted;
    }

    public sealed class DemoClientState
    {
        private readonly AuthorityRevisionGate views = new AuthorityRevisionGate();
        public NetworkEntityKey Entity { get; private set; }
        public InteractionInputSession Input { get; private set; }
        public InteractionOutcomeMirror Outcomes { get; private set; }
        public DemoWorldView View { get; private set; }
        public DemoReceipt LastReceipt { get; private set; }
        public DemoReceipt LastWireReceipt { get; private set; }
        public bool IsBound => Input != null;
        public DemoActorView Self => View?.Actors.FirstOrDefault(a => a.Entity == Entity.EntityId.Value && a.Generation == Entity.Generation);
        internal void Bind(NetworkEntityKey entity, uint epoch, long activation)
        { Entity = entity; Input = new InteractionInputSession(epoch, activation); Outcomes = new InteractionOutcomeMirror(epoch, entity.ToString(), activation); views.Reset(); }
        internal void Apply(DemoPacket packet)
        {
            if (packet.Kind == "hello") Bind(new NetworkEntityKey(new NetEntityId(packet.Actor), packet.ActorGeneration), packet.Epoch, packet.Binding);
            if (!IsBound) return;
            if (packet.View != null && packet.View.Epoch == Input.Epoch && views.TryAccept(packet.View.Revision)) View = packet.View;
            if (packet.Receipt != null)
            {
                LastWireReceipt = packet.Receipt;
                if (Outcomes.TryApply(packet.Receipt.ToReceipt()) && Outcomes.LatestReceipt.HasValue)
                    LastReceipt = DemoReceipt.From(Outcomes.LatestReceipt.Value);
            }
            if (packet.Outcome != null) Outcomes.TryApply(packet.Outcome.ToOutcome());
        }
        public DemoPacket Press(string name)
        {
            var target = View?.Targets.FirstOrDefault(t => t.Name == name);
            if (target == null) return null;
            var key = new NetworkEntityKey(new NetEntityId(target.Entity), target.Generation);
            if (!Input.TryPress(new InteractionTargetIdentity(key.ToString(), target.Activation), out var command)) return null;
            return DemoPacket.Command(Entity, key, command);
        }
        public DemoPacket Edge(string kind)
        {
            if (!IsBound) return null;
            InteractionCommand command;
            bool valid = kind == "release" ? Input.TryRelease(out command) : kind == "cancel" ? Input.TryCancel(out command) : Input.TryConfirm(out command);
            if (!valid) return null;
            string[] parts = command.Target.Id.Split(':');
            var target = new NetworkEntityKey(new NetEntityId(ulong.Parse(parts[0])), uint.Parse(parts[1]));
            return DemoPacket.Command(Entity, target, command);
        }
    }

    public sealed class AuthorityDemoHost : IDisposable
    {
        private sealed class Remote
        { internal DemoConnection Connection; internal NetworkEntityKey Actor; internal bool Closed; }
        private readonly AuthorityDemoWorld world;
        private readonly TcpListener listener;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly ConcurrentQueue<TcpClient> accepted = new ConcurrentQueue<TcpClient>();
        private readonly List<Remote> remotes = new List<Remote>();
        private readonly List<InteractionAuthorityOutcome> notifications = new List<InteractionAuthorityOutcome>();
        private readonly Task acceptTask;
        private int guestCount;
        private double nextSnapshot, serviceTime;
        public DemoClientState Local { get; } = new DemoClientState();
        public int Port { get; }
        public int ReceivedPackets { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool WorkersEnded => (acceptTask == null || acceptTask.IsCompleted) && remotes.All(r => r.Connection.WorkersEnded);
        public AuthorityDemoHost(uint epoch, bool network, int port = 0)
        {
            world = new AuthorityDemoWorld(epoch);
            var actor = world.GetPeer(world.HostEntity).Receiver;
            Local.Apply(new DemoPacket { Kind = "hello", Actor = world.HostEntity.EntityId.Value, ActorGeneration = world.HostEntity.Generation,
                Epoch = epoch, Binding = actor.Activation, View = world.Snapshot() });
            if (network)
            {
                listener = new TcpListener(IPAddress.Loopback, port); listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                acceptTask = Task.Run(async () =>
                {
                    try { while (!stop.IsCancellationRequested) accepted.Enqueue(await listener.AcceptTcpClientAsync().ConfigureAwait(false)); }
                    catch (Exception) { if (!stop.IsCancellationRequested) throw; }
                });
            }
        }
        public void Pump(double delta)
        {
            if (IsDisposed) return;
            while (accepted.TryDequeue(out var socket))
            {
                if (remotes.Count >= 8) { socket.Dispose(); continue; }
                var actor = world.ConnectGuest("guest-" + (++guestCount));
                var remote = new Remote { Actor = actor, Connection = new DemoConnection(socket) }; remotes.Add(remote);
                remote.Connection.Send(new DemoPacket { Kind = "hello", Actor = actor.EntityId.Value, ActorGeneration = actor.Generation,
                    Epoch = world.Epoch, Binding = world.GetPeer(actor).Receiver.Activation, View = world.Snapshot() });
            }
            foreach (var remote in remotes)
            {
                while (remote.Connection.TryRead(out var packet))
                {
                    ReceivedPackets++;
                    try { Handle(remote.Actor, packet, remote.Connection.Send); }
                    catch (Exception error) { remote.Connection.Send(new DemoPacket { Kind = "error", Name = error.Message }); }
                }
                if (!remote.Closed && remote.Connection.IsClosed) { remote.Closed = true; world.Disconnect(remote.Actor); }
            }
            serviceTime += delta; world.Tick(delta);
            Drain(world.HostEntity, Local.Apply);
            foreach (var remote in remotes) if (!remote.Closed) Drain(remote.Actor, remote.Connection.Send);
            if (serviceTime >= nextSnapshot || delta == 0) { nextSnapshot = serviceTime + .1; Broadcast(); }
        }
        private void Drain(NetworkEntityKey actor, Action<DemoPacket> send)
        {
            notifications.Clear(); world.GetPeer(actor).Receiver.DrainOutcomes(notifications);
            foreach (var value in notifications) send(new DemoPacket { Kind = "outcome", Outcome = DemoOutcome.From(value) });
        }
        private void Handle(NetworkEntityKey actor, DemoPacket packet, Action<DemoPacket> reply)
        {
            if (packet.Kind == "approach") world.Approach(actor, packet.Name);
            else if (packet.Kind == "leave") world.LeaveRange(actor);
            else if (packet.Kind == "command")
            {
                var receipt = world.GetPeer(actor).Receive(packet.ToCommand());
                reply(new DemoPacket { Kind = "receipt", Receipt = DemoReceipt.From(receipt), View = world.Snapshot() });
            }
            else throw new InvalidDataException("Unknown sample command.");
        }
        private void Broadcast()
        {
            var packet = new DemoPacket { Kind = "view", View = world.Snapshot() }; Local.Apply(packet);
            foreach (var remote in remotes) if (!remote.Closed) remote.Connection.Send(packet);
        }
        public void SubmitLocal(DemoPacket packet) { if (packet != null) { Handle(world.HostEntity, packet, Local.Apply); Broadcast(); } }
        public void ReuseShop() { world.ReuseShop(); Broadcast(); }
        public void SetPaused(bool value) { world.Paused = value; Broadcast(); }
        public void Dispose()
        {
            if (IsDisposed) return; IsDisposed = true; stop.Cancel(); listener?.Stop();
            while (accepted.TryDequeue(out var socket)) socket.Dispose();
            foreach (var remote in remotes) remote.Connection.Dispose();
            world.Dispose();
        }
    }

    public sealed class AuthorityDemoClient : IDisposable
    {
        private volatile DemoConnection connection;
        private readonly Task connect;
        private readonly List<DemoPacket> held = new List<DemoPacket>();
        private volatile bool disposed;
        public DemoClientState State { get; } = new DemoClientState();
        public bool HoldReplies { get; set; }
        public bool Connected => connection != null && !connection.IsClosed && State.IsBound;
        public string Failure { get; private set; }
        public bool WorkersEnded => connect.IsCompleted && (connection == null || connection.WorkersEnded);
        public AuthorityDemoClient(int port)
        {
            connect = Task.Run(async () =>
            {
                var socket = new TcpClient();
                try
                {
                    var connecting = socket.ConnectAsync(IPAddress.Loopback, port);
                    if (await Task.WhenAny(connecting, Task.Delay(5000)).ConfigureAwait(false) != connecting)
                        throw new TimeoutException("Loopback sample connection timed out.");
                    await connecting.ConfigureAwait(false);
                    if (disposed) { socket.Dispose(); return; }
                    connection = new DemoConnection(socket);
                }
                catch (Exception error) { socket.Dispose(); Failure = error.Message; }
            });
        }
        public void Pump()
        {
            if (connection == null) return;
            while (connection.TryRead(out var packet))
            {
                if (packet.Kind == "error") { Failure = packet.Name; continue; }
                if (HoldReplies && packet.Kind != "hello")
                { if (held.Count == 128) throw new InvalidOperationException("Held sample replies reached their bound."); held.Add(packet); }
                else State.Apply(packet);
            }
        }
        public void FlushReplies(bool reverse)
        { HoldReplies = false; if (reverse) held.Reverse(); foreach (var packet in held) State.Apply(packet); held.Clear(); }
        public void Send(DemoPacket packet) { if (packet != null) connection?.Send(packet); }
        public void Dispose() { disposed = true; connection?.Dispose(); }
    }
}
