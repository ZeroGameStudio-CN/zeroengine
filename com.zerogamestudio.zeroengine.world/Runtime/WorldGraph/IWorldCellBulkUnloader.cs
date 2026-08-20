using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.World.WorldGraph
{
    public interface IWorldCellBulkUnloader
    {
        Task<WorldCellOperationResult> UnloadAllAsync(CancellationToken cancellationToken);
    }
}
