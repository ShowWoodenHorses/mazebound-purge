using Maze.Core.Grid;

namespace Maze.Core.Navigation
{
    /// <summary>
    /// Cost of stepping into a passable cell for <see cref="GridPathfinder"/>: at least 1 (the Manhattan heuristic stays
    /// admissible). Implemented by structs and passed as a generic argument — no boxing, no allocations.
    /// </summary>
    public interface IGridCost
    {
        int StepCost(GridPosition to);
    }

    /// <summary>Every step costs 1: the shortest path by steps.</summary>
    public readonly struct UniformCost : IGridCost
    {
        public int StepCost(GridPosition to) => 1;
    }
}
