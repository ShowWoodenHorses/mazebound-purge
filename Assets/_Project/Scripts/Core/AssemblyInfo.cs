using System.Runtime.CompilerServices;

// Level data mutation API is internal: only the editor tool and tests may change LevelData.
[assembly: InternalsVisibleTo("Purge.Editor")]
[assembly: InternalsVisibleTo("Purge.Tests.EditMode")]
