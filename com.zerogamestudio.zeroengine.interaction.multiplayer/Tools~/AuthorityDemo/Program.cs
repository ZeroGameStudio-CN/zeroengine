using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZeroEngine.Interaction.Multiplayer.Samples;

[DataContract] internal sealed class Control
{
    [DataMember] public int Id;
    [DataMember] public string Op, Target = string.Empty;
    [DataMember] public bool Flag = false;
}
[DataContract] internal sealed class Report
{
    [DataMember] public int Id, Pid, Port, ReceivedPackets;
    [DataMember] public string Kind, Role, Error, Rejection, OutcomeStatus;
    [DataMember] public bool Connected, Clean;
    [DataMember] public ulong Actor;
    [DataMember] public uint ActorGeneration;
    [DataMember] public DemoWorldView View;
    [DataMember] public DemoReceipt Receipt;
    [DataMember] public DemoOutcome Outcome;
}
internal static class Program
{
    private static void Write(Report value) => Console.WriteLine(Encoding.UTF8.GetString(DemoWire.Encode(value)));
    private static int Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8; Console.OutputEncoding = new UTF8Encoding(false);
        string role = args.Length == 0 ? "host" : args[0];
        int port = args.Length > 1 ? int.Parse(args[1]) : 0;
        int pid = Process.GetCurrentProcess().Id;
        AuthorityDemoHost host = null; AuthorityDemoClient client = null; DemoPacket last = null, captured = null;
        var commands = new ConcurrentQueue<Control>();
        bool eof = false;
        var input = Task.Run(() =>
        {
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                try { commands.Enqueue(DemoWire.Decode<Control>(Encoding.UTF8.GetBytes(line))); }
                catch (Exception) { commands.Enqueue(new Control { Id = -1, Op = "invalid" }); }
            }
            Volatile.Write(ref eof, true);
        });
        try
        {
            if (role == "host" || role == "offline") host = new AuthorityDemoHost(7, role == "host", port);
            else if (role == "client") client = new AuthorityDemoClient(port);
            else throw new ArgumentException("Use host, client <port>, or offline.");
            Write(new Report { Kind = "ready", Pid = pid, Role = role, Port = host?.Port ?? port });
            var timer = Stopwatch.StartNew(); double previous = timer.Elapsed.TotalSeconds;
            bool quitting = false;
            while (!quitting && !Volatile.Read(ref eof))
            {
                double now = timer.Elapsed.TotalSeconds, delta = now - previous; previous = now;
                host?.Pump(delta); client?.Pump();
                while (commands.TryDequeue(out var command))
                {
                    string error = null;
                    try
                    {
                        var state = host?.Local ?? client.State;
                        void Send(DemoPacket packet)
                        {
                            if (packet == null) return;
                            last = packet;
                            if (host != null) host.SubmitLocal(packet); else client.Send(packet);
                        }
                        switch (command.Op)
                        {
                            case "state": break;
                            case "approach": Send(new DemoPacket { Kind = "approach", Name = command.Target }); break;
                            case "leave": Send(new DemoPacket { Kind = "leave" }); break;
                            case "press": Send(state.Press(command.Target)); break;
                            case "release": case "cancel": case "confirm": Send(state.Edge(command.Op)); break;
                            case "replay": if (last != null) { if (host != null) host.SubmitLocal(last); else client.Send(last); } break;
                            case "capture": captured = last; break;
                            case "replayCaptured": if (captured != null) { if (host != null) host.SubmitLocal(captured); else client.Send(captured); } break;
                            case "holdReplies": if (client == null) throw new InvalidOperationException("Client-only sample control."); client.HoldReplies = command.Flag; break;
                            case "flush": if (client == null) throw new InvalidOperationException("Client-only sample control."); client.FlushReplies(command.Flag); break;
                            case "repool": if (host == null) throw new InvalidOperationException("Authority-only sample control."); host.ReuseShop(); break;
                            case "pause": if (host == null) throw new InvalidOperationException("Authority-only sample control."); host.SetPaused(command.Flag); break;
                            case "disconnect": if (client == null) throw new InvalidOperationException("Client-only sample control."); client.Dispose(); break;
                            case "spoof":
                                if (client == null || last == null || last.Kind != "command") throw new InvalidOperationException("Capture a client command first.");
                                var copy = DemoWire.Decode<DemoPacket>(DemoWire.Encode(last));
                                var other = state.View.Actors[0]; copy.Actor = other.Entity; copy.ActorGeneration = other.Generation;
                                client.Send(copy); break;
                            case "wrongEpoch":
                                if (client == null || last == null || last.Kind != "command") throw new InvalidOperationException("Capture a client command first.");
                                var stale = DemoWire.Decode<DemoPacket>(DemoWire.Encode(last)); stale.Epoch++;
                                client.Send(stale); break;
                            case "quit": quitting = true; break;
                            default: throw new InvalidOperationException("Unknown control operation.");
                        }
                    }
                    catch (Exception exception) { error = exception.Message; }
                    var current = host?.Local ?? client?.State;
                    Write(new Report { Kind = "report", Id = command.Id, Pid = pid, Role = role,
                        Connected = host != null || client?.Connected == true, Actor = current?.Entity.EntityId.Value ?? 0,
                        ActorGeneration = current?.Entity.Generation ?? 0, View = current?.View, Receipt = current?.LastWireReceipt,
                        Rejection = current?.LastWireReceipt?.ToReceipt().Rejection.ToString(),
                        OutcomeStatus = current?.Outcomes?.LatestOutcome?.Status.ToString(),
                        Outcome = current?.Outcomes?.LatestOutcome is { } outcome ? DemoOutcome.From(outcome) : null,
                        ReceivedPackets = host?.ReceivedPackets ?? 0, Error = error ?? client?.Failure });
                }
                Thread.Sleep(5);
            }
            host?.Dispose(); client?.Dispose();
            var cleanup = Stopwatch.StartNew();
            while (cleanup.Elapsed.TotalSeconds < 3 && !(host?.WorkersEnded ?? client.WorkersEnded)) Thread.Sleep(10);
            bool clean = host?.WorkersEnded ?? client.WorkersEnded;
            Write(new Report { Kind = "closed", Pid = pid, Role = role, Clean = clean });
            return clean ? 0 : 2;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error); host?.Dispose(); client?.Dispose(); return 1; }
    }
}
