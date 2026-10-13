using System;
using System.Collections.Generic;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Player;

namespace Maze.Gameplay.Level
{
    /// <summary>
    /// Exits (ТЗ §62): every exit is equal. Entering an exit cell asks for confirmation; entering again asks again.
    /// The confirmation itself is application flow (GameFlow), not gameplay.
    /// </summary>
    public sealed class ExitSystem : IDisposable
    {
        private readonly PlayerSystem _player;
        private readonly Dictionary<GridPosition, ExitData> _exits = new Dictionary<GridPosition, ExitData>();

        public ExitSystem(LevelData level, PlayerSystem player)
        {
            _player = player;
            foreach (var exit in level.Exits)
                _exits[exit.Position] = exit;
            _player.CellChanged += OnPlayerCellChanged;
        }

        public event Action<ExitData> ExitReached;

        public bool IsExit(GridPosition cell) => _exits.ContainsKey(cell);

        public void Dispose()
        {
            _player.CellChanged -= OnPlayerCellChanged;
            ExitReached = null;
        }

        private void OnPlayerCellChanged(GridPosition from, GridPosition to)
        {
            if (_exits.TryGetValue(to, out var exit))
                ExitReached?.Invoke(exit);
        }
    }
}
