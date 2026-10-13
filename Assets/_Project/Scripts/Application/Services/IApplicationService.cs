using System.Threading;
using Cysharp.Threading.Tasks;

namespace Maze.Application.Services
{
    /// <summary>
    /// Application-wide service initialized by <see cref="Flow.GameFlow.InitializeApplication"/>
    /// in registration order. Never initializes itself from constructors or Unity callbacks.
    /// </summary>
    public interface IApplicationService
    {
        string Name { get; }

        UniTask InitializeAsync(CancellationToken cancellation);
    }
}
