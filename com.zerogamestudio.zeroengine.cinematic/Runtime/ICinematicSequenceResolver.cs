namespace ZeroEngine.Cinematic
{
    public interface ICinematicSequenceResolver
    {
        bool TryResolve(string sequenceId, out CinematicSequenceDefinition sequence);
    }
}
