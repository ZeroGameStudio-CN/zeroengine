namespace ZeroEngine.Cinematic
{
    public readonly struct CinematicPlayRequest
    {
        public CinematicPlayRequest(
            string sequenceId,
            CinematicSkipPolicy skipPolicy,
            CinematicInputLockPolicy inputLockPolicy,
            CinematicCameraRestorePolicy cameraRestorePolicy,
            CinematicPlaybackTimeoutPolicy timeoutPolicy,
            string sourceId = null,
            bool allowInterrupt = false)
        {
            SequenceId = NormalizeSequenceId(sequenceId);
            SourceId = NormalizeId(sourceId);
            AllowInterrupt = allowInterrupt;
            SkipPolicy = skipPolicy;
            InputLockPolicy = inputLockPolicy;
            CameraRestorePolicy = cameraRestorePolicy;
            TimeoutPolicy = timeoutPolicy;
        }

        public string SequenceId { get; }

        public string SourceId { get; }

        public bool AllowInterrupt { get; }

        public CinematicSkipPolicy SkipPolicy { get; }

        public CinematicInputLockPolicy InputLockPolicy { get; }

        public CinematicCameraRestorePolicy CameraRestorePolicy { get; }

        public CinematicPlaybackTimeoutPolicy TimeoutPolicy { get; }

        public static CinematicPlayRequest ForSequence(
            string sequenceId,
            string sourceId = null,
            bool allowInterrupt = false)
        {
            return new CinematicPlayRequest(
                sequenceId,
                CinematicSkipPolicy.AllowAfterMinimumPlayback,
                CinematicInputLockPolicy.GameplayOnly,
                CinematicCameraRestorePolicy.RestorePrevious,
                CinematicPlaybackTimeoutPolicy.CreateDefault(),
                sourceId,
                allowInterrupt);
        }

        public static CinematicPlayRequest FromSequence(
            CinematicSequenceDefinition sequence,
            string sourceId = null,
            bool allowInterrupt = false)
        {
            if (sequence == null)
            {
                return ForSequence(string.Empty, sourceId, allowInterrupt);
            }

            return new CinematicPlayRequest(
                sequence.SequenceId,
                sequence.SkipPolicy,
                sequence.InputLockPolicy,
                sequence.CameraRestorePolicy,
                sequence.TimeoutPolicy,
                sourceId,
                allowInterrupt);
        }

        private static string NormalizeSequenceId(string sequenceId)
        {
            return NormalizeId(sequenceId);
        }

        private static string NormalizeId(string id)
        {
            return string.IsNullOrWhiteSpace(id)
                ? string.Empty
                : id.Trim();
        }
    }
}
