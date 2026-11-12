namespace ZeroEngine.Cinematic
{
    public sealed class CinematicProjectPlaybackServices
    {
        private static readonly ICinematicProjectPlaybackService[] EmptyServices =
            new ICinematicProjectPlaybackService[0];

        private readonly ICinematicProjectPlaybackService[] _services;

        public CinematicProjectPlaybackServices(ICinematicProjectPlaybackService[] services)
        {
            _services = services == null || services.Length == 0
                ? EmptyServices
                : (ICinematicProjectPlaybackService[])services.Clone();
        }

        public static CinematicProjectPlaybackServices None { get; } =
            new CinematicProjectPlaybackServices(EmptyServices);

        public CinematicPlayResult Enter(CinematicPlaybackContext context)
        {
            for (var i = 0; i < _services.Length; i++)
            {
                try
                {
                    _services[i]?.EnterCinematic(context);
                }
                catch (System.Exception exception)
                {
                    var result = new CinematicPlayResult(
                        CinematicPlayStatus.Failed,
                        exception.Message,
                        requiresAbortCleanup: true,
                        sequenceId: context.Request.SequenceId);
                    return Exit(context, result, i - 1);
                }
            }

            return CinematicPlayResult.None;
        }

        public CinematicPlayResult Exit(CinematicPlaybackContext context, CinematicPlayResult result)
        {
            return Exit(context, result, _services.Length - 1);
        }

        private CinematicPlayResult Exit(CinematicPlaybackContext context, CinematicPlayResult result, int lastEnteredIndex)
        {
            var exitResult = result;
            for (var i = lastEnteredIndex; i >= 0; i--)
            {
                try
                {
                    _services[i]?.ExitCinematic(context, exitResult);
                }
                catch (System.Exception exception)
                {
                    exitResult = new CinematicPlayResult(
                        CinematicPlayStatus.Failed,
                        exception.Message,
                        requiresAbortCleanup: true,
                        sequenceId: exitResult.SequenceId);
                }
            }

            return exitResult;
        }
    }
}
