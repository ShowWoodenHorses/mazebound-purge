using Maze.Core.Generation;
using Maze.Core.Level;
using Maze.Core.Visual;

namespace Maze.Core.Authoring
{
    /// <summary>High-level level creation commands used by the editor tool (ТЗ §16, §29, §95).</summary>
    internal static class LevelAuthoring
    {
        /// <summary>
        /// Destructive: new logical maze from MazeSeed, all objects and visual design (including overrides)
        /// are replaced, then initial visuals are assigned from VisualSeed.
        /// </summary>
        public static void GenerateNew(LevelData level)
        {
            var result = new MazeGenerator().Generate(level.Generation);
            level.ApplyGeneratedMaze(result);
            VisualAssigner.AssignAll(level, clearOverrides: true);
        }

        /// <summary>Recreates visual assignments only. Manual overrides survive unless <paramref name="clearOverrides"/>.</summary>
        public static void RegenerateVisuals(LevelData level, bool clearOverrides) =>
            VisualAssigner.AssignAll(level, clearOverrides);
    }
}
