namespace ZeroEngine.World.WorldGraph
{
    public interface IWorldGraphRuntimeLocationStore
    {
        void Save(WorldGraphRuntimeLocation location);

        void Clear();
    }
}
