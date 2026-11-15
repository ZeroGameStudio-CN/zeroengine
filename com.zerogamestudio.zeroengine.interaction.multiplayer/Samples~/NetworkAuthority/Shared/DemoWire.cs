using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using ZeroEngine.Interaction.Core;
using ZeroEngine.Interaction.Core.Authority;
using ZeroEngine.Multiplayer.Replication;

namespace ZeroEngine.Interaction.Multiplayer.Samples
{
    [DataContract] public sealed class DemoOutcome
    {
        [DataMember] public uint Epoch;
        [DataMember] public string Actor, Target, Reason;
        [DataMember] public long Binding, Action, Revision, Execution, Activation;
        [DataMember] public int Gesture, Status;
        public static DemoOutcome From(InteractionAuthorityOutcome value) => new DemoOutcome { Epoch = value.Epoch,
            Actor = value.ActorId, Binding = value.ActorActivation, Action = value.ActionId, Revision = value.Revision,
            Execution = value.ExecutionId, Target = value.Target.Id, Activation = value.Target.Activation,
            Gesture = (int)value.Gesture, Status = (int)value.Status, Reason = value.Reason };
        public InteractionAuthorityOutcome ToOutcome() => new InteractionAuthorityOutcome(Epoch, Actor, Binding, Action,
            Revision, Execution, new InteractionTargetIdentity(Target, Activation), (InteractionGesture)Gesture, (InteractionStatus)Status, Reason);
    }
    [DataContract] public sealed class DemoReceipt
    {
        [DataMember] public uint Epoch;
        [DataMember] public string Actor;
        [DataMember] public long Binding, Sequence, Action;
        [DataMember] public bool Handled, Admitted, Replay, Held, Pending;
        [DataMember] public int Rejection;
        [DataMember] public DemoOutcome Outcome;
        public static DemoReceipt From(InteractionCommandReceipt value) => new DemoReceipt { Epoch = value.Epoch,
            Actor = value.ActorId, Binding = value.ActorActivation, Sequence = value.Sequence, Action = value.ActionId,
            Handled = value.Handled, Admitted = value.InputAdmitted, Replay = value.IsReplay, Held = value.IsHeld,
            Pending = value.IsPending, Rejection = (int)value.Rejection,
            Outcome = value.Outcome.HasValue ? DemoOutcome.From(value.Outcome.Value) : null };
        public InteractionCommandReceipt ToReceipt() => new InteractionCommandReceipt(Epoch, Actor, Binding, Sequence, Action,
            Handled, Admitted, Replay, Held, Pending, (InteractionCommandRejection)Rejection, Outcome?.ToOutcome());
    }
    [DataContract] public sealed class DemoPacket
    {
        [DataMember] public int Protocol = 1;
        [DataMember] public string Kind, Name;
        [DataMember] public uint Epoch, ActorGeneration, TargetGeneration;
        [DataMember] public ulong Actor, Target;
        [DataMember] public long Binding, Sequence, Action, TargetActivation;
        [DataMember] public int Phase;
        [DataMember] public DemoReceipt Receipt;
        [DataMember] public DemoOutcome Outcome;
        [DataMember] public DemoWorldView View;
        public static DemoPacket Command(NetworkEntityKey actor, NetworkEntityKey target, InteractionCommand command) =>
            new DemoPacket { Kind = "command", Actor = actor.EntityId.Value, ActorGeneration = actor.Generation,
                Target = target.EntityId.Value, TargetGeneration = target.Generation, Epoch = command.Epoch,
                Binding = command.ActorActivation, Sequence = command.Sequence, Action = command.ActionId,
                TargetActivation = command.Target.Activation, Phase = (int)command.Kind };
        public InteractionNetworkCommand ToCommand()
        {
            var actor = new NetworkEntityKey(new NetEntityId(Actor), ActorGeneration);
            var target = new NetworkEntityKey(new NetEntityId(Target), TargetGeneration);
            return new InteractionNetworkCommand(actor, target, new InteractionCommand(Epoch, Binding, Sequence, Action,
                new InteractionTargetIdentity(target.ToString(), TargetActivation), (InteractionCommandKind)Phase));
        }
    }
    public static class DemoWire
    {
        public const int MaxFrameBytes = 32768;
        private static DataContractJsonSerializer Serializer(Type type) => new DataContractJsonSerializer(type,
            new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 512 });
        public static byte[] Encode<T>(T value)
        {
            using var stream = new MemoryStream(); Serializer(typeof(T)).WriteObject(stream, value);
            if (stream.Length > MaxFrameBytes) throw new InvalidDataException("Sample frame exceeds its bound.");
            return stream.ToArray();
        }
        public static T Decode<T>(byte[] raw)
        {
            if (raw == null || raw.Length == 0 || raw.Length > MaxFrameBytes) throw new InvalidDataException("Invalid sample frame length.");
            using var stream = new MemoryStream(raw); return (T)Serializer(typeof(T)).ReadObject(stream);
        }
    }
}
