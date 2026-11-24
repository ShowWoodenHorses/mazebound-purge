using System.Threading;
using Cysharp.Threading.Tasks;

namespace Purge.Application.Flow
{
    /// <summary>Prepares the main menu before it becomes visible.</summary>
    public interface IMainMenuPreparation
    {
        UniTask PrepareAsync(CancellationToken cancellation);
    }
}
