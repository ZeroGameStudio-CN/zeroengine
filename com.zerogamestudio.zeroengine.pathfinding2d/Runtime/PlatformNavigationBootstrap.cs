using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    public readonly struct PlatformNavigationComponents
    {
        public readonly PlatformGraphGenerator GraphGenerator;
        public readonly JumpLinkCalculator JumpLinkCalculator;
        public readonly Platform2DPathfinder Pathfinder;

        public PlatformNavigationComponents(
            PlatformGraphGenerator graphGenerator,
            JumpLinkCalculator jumpLinkCalculator,
            Platform2DPathfinder pathfinder)
        {
            GraphGenerator = graphGenerator;
            JumpLinkCalculator = jumpLinkCalculator;
            Pathfinder = pathfinder;
        }
    }

    public static class PlatformNavigationBootstrap
    {
        public static PlatformNavigationComponents EnsureOn(GameObject host)
        {
            var graphGenerator = host.GetComponent<PlatformGraphGenerator>();
            if (!graphGenerator)
                graphGenerator = host.AddComponent<PlatformGraphGenerator>();

            var jumpLinkCalculator = host.GetComponent<JumpLinkCalculator>();
            if (!jumpLinkCalculator)
                jumpLinkCalculator = host.AddComponent<JumpLinkCalculator>();

            var pathfinder = host.GetComponent<Platform2DPathfinder>();
            if (!pathfinder)
                pathfinder = host.AddComponent<Platform2DPathfinder>();

            pathfinder.SetGraphGenerator(graphGenerator);
            return new PlatformNavigationComponents(graphGenerator, jumpLinkCalculator, pathfinder);
        }
    }
}
