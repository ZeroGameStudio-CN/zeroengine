namespace ZeroEngine.Cinematic
{
    public interface ICinematicProjectPlaybackService
    {
        void EnterCinematic(CinematicPlaybackContext context);

        void ExitCinematic(CinematicPlaybackContext context, CinematicPlayResult result);
    }
}
