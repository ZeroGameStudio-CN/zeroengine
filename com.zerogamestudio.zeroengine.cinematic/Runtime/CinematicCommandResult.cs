namespace ZeroEngine.Cinematic
{
    public readonly struct CinematicCommandResult
    {
        private CinematicCommandResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }

        public string Message { get; }

        public static CinematicCommandResult Success()
        {
            return new CinematicCommandResult(true, string.Empty);
        }

        public static CinematicCommandResult Fail(string message)
        {
            return new CinematicCommandResult(false, message);
        }
    }
}
