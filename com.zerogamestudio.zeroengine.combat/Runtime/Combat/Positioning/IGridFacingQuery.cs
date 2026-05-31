namespace ZeroEngine.Combat
{
    public interface IGridFacingQuery<T>
    {
        bool TryGetFacing(T subject, out GridDirection facing);
    }
}
