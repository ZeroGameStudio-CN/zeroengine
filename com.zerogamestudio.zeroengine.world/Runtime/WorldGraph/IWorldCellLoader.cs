using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.World.WorldGraph
{
    public interface IWorldCellLoader
    {
        Task<WorldCellOperationResult> LoadCellAsync(
            WorldCellDefinition cell,
            WorldCellLayer layers,
            CancellationToken cancellationToken);

        Task<WorldCellOperationResult> UnloadCellAsync(
            WorldCellDefinition cell,
            CancellationToken cancellationToken);
    }

    public interface IWorldCellReadinessService
    {
        Task<WorldCellReadinessResult> PrepareCellAsync(
            WorldCellDefinition cell,
            WorldCellLayer layers,
            CancellationToken cancellationToken);
    }
}
