using System;
using System.Collections.Generic;

namespace ZeroEngine.Multiplayer.Presentation
{
    public sealed class MultiplayerViewState
    {
        private MultiplayerViewState(
            SessionPhase phase,
            RoomSnapshot room,
            bool canCreate,
            bool canInvite,
            bool canStart,
            bool canLeave,
            bool canRetry,
            MultiplayerErrorCode errorCode,
            string errorMessageKey,
            IReadOnlyList<string> errorArguments,
            string progressMessageKey,
            TimeSpan reconnectRemaining,
            int reconnectAttempt)
        {
            Phase = phase;
            Room = room;
            CanCreate = canCreate;
            CanInvite = canInvite;
            CanStart = canStart;
            CanLeave = canLeave;
            CanRetry = canRetry;
            ErrorCode = errorCode;
            ErrorMessageKey = errorMessageKey ?? string.Empty;
            ErrorArguments = errorArguments ?? Array.Empty<string>();
            ProgressMessageKey = progressMessageKey ?? string.Empty;
            ReconnectRemaining = reconnectRemaining;
            ReconnectAttempt = reconnectAttempt;
        }

        public SessionPhase Phase { get; }
        public RoomSnapshot Room { get; }
        public bool CanCreate { get; }
        public bool CanInvite { get; }
        public bool CanStart { get; }
        public bool CanLeave { get; }
        public bool CanRetry { get; }
        public MultiplayerErrorCode ErrorCode { get; }
        public string ErrorMessageKey { get; }
        public IReadOnlyList<string> ErrorArguments { get; }
        public string ProgressMessageKey { get; }
        public TimeSpan ReconnectRemaining { get; }
        public int ReconnectAttempt { get; }

        public static MultiplayerViewState Create(
            MultiplayerSessionSnapshot snapshot,
            MultiplayerSessionConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            RoomSnapshot room = snapshot.Room;
            bool localIsHost = room != null && room.IsHost(snapshot.LocalUser.Id);
            bool roomHasCapacity = room != null && room.Members.Count < room.MaxMembers;
            bool canStart = !snapshot.OperationInProgress && snapshot.Phase == SessionPhase.Ready &&
                            localIsHost && snapshot.IsServer && MeetsReadyConditions(room, config.MinimumPlayersToStart);

            return new MultiplayerViewState(
                snapshot.Phase,
                room,
                !snapshot.OperationInProgress && snapshot.Phase == SessionPhase.Idle,
                !snapshot.OperationInProgress && localIsHost && roomHasCapacity &&
                    (snapshot.Phase == SessionPhase.InRoom || snapshot.Phase == SessionPhase.Ready),
                canStart,
                !snapshot.OperationInProgress && IsLeaveAvailable(snapshot.Phase, room),
                !snapshot.OperationInProgress && snapshot.Phase == SessionPhase.Failed &&
                    snapshot.RetryOperation != RetryOperationKind.None,
                snapshot.LastResult.Succeeded ? MultiplayerErrorCode.None : snapshot.LastResult.ErrorCode,
                snapshot.LastResult.Succeeded ? string.Empty : snapshot.LastResult.MessageKey,
                snapshot.LastResult.Succeeded ? Array.Empty<string>() : snapshot.LastResult.MessageArguments,
                GetProgressMessageKey(snapshot.Phase),
                snapshot.ReconnectRemaining,
                snapshot.ReconnectAttempt);
        }

        private static bool MeetsReadyConditions(RoomSnapshot room, int minimumPlayers)
        {
            if (room == null || room.Members.Count < minimumPlayers)
            {
                return false;
            }

            for (int i = 0; i < room.Members.Count; i++)
            {
                if (room.Members[i].ConnectionPhase != MemberConnectionPhase.Ready)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsLeaveAvailable(SessionPhase phase, RoomSnapshot room)
        {
            if (room == null && phase != SessionPhase.Failed)
            {
                return false;
            }

            return phase == SessionPhase.InRoom || phase == SessionPhase.Ready ||
                   phase == SessionPhase.InGame || phase == SessionPhase.Recovery ||
                   phase == SessionPhase.Failed;
        }

        private static string GetProgressMessageKey(SessionPhase phase)
        {
            switch (phase)
            {
                case SessionPhase.Initializing:
                    return "multiplayer.progress.initializing";
                case SessionPhase.CreatingRoom:
                    return "multiplayer.progress.creating_room";
                case SessionPhase.JoiningRoom:
                    return "multiplayer.progress.joining_room";
                case SessionPhase.Connecting:
                    return "multiplayer.progress.connecting";
                case SessionPhase.Synchronizing:
                    return "multiplayer.progress.synchronizing";
                case SessionPhase.Starting:
                    return "multiplayer.progress.starting";
                case SessionPhase.Reconnecting:
                    return "multiplayer.progress.reconnecting";
                case SessionPhase.Leaving:
                    return "multiplayer.progress.leaving";
                default:
                    return string.Empty;
            }
        }
    }
}
