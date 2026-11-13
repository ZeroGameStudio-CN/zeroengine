using System;

namespace ZeroEngine.Multiplayer.Replication
{
    /// <summary>
    /// Accepts only forward-moving uint sequence numbers, including wrap-around.
    /// A distance of exactly half the uint range is ambiguous and is rejected.
    /// </summary>
    public sealed class MessageSequenceGate
    {
        private const uint HalfRange = 0x80000000u;

        public bool HasAcceptedSequence { get; private set; }
        public uint LastAcceptedSequence { get; private set; }

        public bool TryAccept(uint sequence)
        {
            if (!HasAcceptedSequence)
            {
                HasAcceptedSequence = true;
                LastAcceptedSequence = sequence;
                return true;
            }

            if (!IsNewer(sequence, LastAcceptedSequence))
                return false;

            LastAcceptedSequence = sequence;
            return true;
        }

        public static bool IsNewer(uint candidate, uint baseline)
        {
            uint forwardDistance = unchecked(candidate - baseline);
            return forwardDistance != 0u && forwardDistance < HalfRange;
        }

        public void Reset()
        {
            HasAcceptedSequence = false;
            LastAcceptedSequence = 0u;
        }
    }

    public sealed class AuthorityRevisionGate
    {
        public bool HasAcceptedRevision { get; private set; }
        public uint LastAcceptedRevision { get; private set; }

        public bool TryAccept(uint revision)
        {
            if (!CanAccept(revision))
                return false;

            HasAcceptedRevision = true;
            LastAcceptedRevision = revision;
            return true;
        }

        public bool CanAccept(uint revision)
        {
            return revision != 0u
                   && (!HasAcceptedRevision || MessageSequenceGate.IsNewer(revision, LastAcceptedRevision));
        }

        public void Reset()
        {
            HasAcceptedRevision = false;
            LastAcceptedRevision = 0u;
        }
    }
}
