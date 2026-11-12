using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.World.Presentation
{
    public interface IWorldCellPresentationStabilityService
    {
        Task<WorldCellPresentationStabilityResult> WaitForStableAsync(
            string cellId,
            CancellationToken cancellationToken);
    }
}
