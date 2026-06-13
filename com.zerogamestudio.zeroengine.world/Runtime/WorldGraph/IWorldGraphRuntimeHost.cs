using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.World.WorldGraph
{
    public interface IWorldGraphRuntimeHost
    {
        string ActiveWorldGraphId { get; }

        Task<WorldGraphRuntimeSessionResult> LoadTargetAsync(
            WorldGraphConnectionDefinition connection,
            CancellationToken cancellationToken);

        Task<WorldGraphRuntimeSessionResult> SwitchActiveGraphAsync(
            WorldGraphConnectionDefinition connection,
            CancellationToken cancellationToken);

        Task<WorldGraphRuntimeSessionResult> UnloadSourceAsync(
            WorldGraphConnectionDefinition connection,
            CancellationToken cancellationToken);
    }
}
