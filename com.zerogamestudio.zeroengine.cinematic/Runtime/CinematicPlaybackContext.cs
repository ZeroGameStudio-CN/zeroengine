namespace ZeroEngine.Cinematic
{
    public readonly struct CinematicPlaybackContext
    {
        public CinematicPlaybackContext(
            CinematicPlayRequest request,
            CinematicSequenceDefinition sequence)
        {
            Request = request;
            Sequence = sequence;
        }

        public CinematicPlayRequest Request { get; }

        public CinematicSequenceDefinition Sequence { get; }
    }
}
