namespace ZeroEngine.Cinematic
{
    public interface ICinematicCommandExecutor
    {
        CinematicCommandResult Execute(
            CinematicCommandDefinition command,
            CinematicPlaybackContext context);
    }
}
