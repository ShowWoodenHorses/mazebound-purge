using System.Runtime.CompilerServices;

// Level data mutation API is internal: only the editor tool and tests may change LevelData.
[assembly: InternalsVisibleTo("Maze.Editor")]
[assembly: InternalsVisibleTo("Maze.Tests.EditMode")]
