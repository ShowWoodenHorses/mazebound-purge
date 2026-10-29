using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Boosters;
using Purge.Core.Grid;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;

namespace Purge.Gameplay.Visibility
{
    /// <summary>
    /// Cells the player has seen in this run (stage 7г, the map's fog): every cell revealed by
    /// <see cref="VisibilitySystem"/> becomes explored for good. A bit per cell of every floor; in the run's save
    /// (<see cref="Export"/> / the resumed run's <see cref="Run.RunSnapshot.Explored"/>), so a continued run shows the
    /// map as it was. Updated on visibility events only, never per frame; the map listens to <see cref="Explored"/>.
    /// </summary>
    public sealed class MapExploration : ILevelLoadStep, IDisposable
    {
        private readonly LevelGrid _grid;
        private readonly VisibilitySystem _visibility;
        private readonly PlayerSystem _player;
        private readonly LevelLaunchOptions _options;
        private readonly bool[] _explored;
        private readonly List<GridPosition> _added = new List<GridPosition>();
        private bool _subscribed;

        public MapExploration(LevelGrid grid, VisibilitySystem visibility, PlayerSystem player, LevelLaunchOptions options = null)
        {
            _grid = grid;
            _visibility = visibility;
            _player = player;
            _options = options;
            _explored = new bool[grid.FloorSize * grid.FloorCount];
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeVisibility;

        /// <summary>Explored cells so far.</summary>
        public int Count { get; private set; }

        /// <summary>(cells explored just now) — the list is reused, read it at once.</summary>
        public event Action<IReadOnlyList<GridPosition>> Explored;

        public bool IsExplored(GridPosition cell) =>
            _grid.IsInside(cell) && _explored[cell.Floor * _grid.FloorSize + cell.Y * _grid.Width + cell.X];

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_subscribed)
            {
                Import(_options?.Resume?.Explored);
                // The booster «Разведка» (stage 11б): a new run starts with every cell explored.
                if (_options != null && _options.Resume == null && _options.TryGetBoost<ScoutingBoost>(out _)) ExploreAll();
                _visibility.Changed += OnVisibilityChanged;
                _subscribed = true;
            }

            if (_visibility.HasResult) OnVisibilityChanged();
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _visibility.Changed -= OnVisibilityChanged;
            _subscribed = false;
        }

        private void OnVisibilityChanged()
        {
            _added.Clear();
            var cells = _visibility.RevealedCells;
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = new GridPosition(cells[i].X, cells[i].Y, _player.Floor);
                if (!_grid.IsInside(cell)) continue;
                var index = cell.Floor * _grid.FloorSize + cell.Y * _grid.Width + cell.X;
                if (_explored[index]) continue;
                _explored[index] = true;
                Count++;
                _added.Add(cell);
            }

            if (_added.Count > 0) Explored?.Invoke(_added);
        }

        /// <summary>The explored cells packed for the run's save: a bit per cell of every floor, Base64.</summary>
        public string Export() => Pack(_explored);

        /// <summary>Every cell of every floor explored (before the map is drawn: no event).</summary>
        private void ExploreAll()
        {
            for (var i = 0; i < _explored.Length; i++) _explored[i] = true;
            Count = _explored.Length;
        }

        /// <summary>Restores <see cref="Export"/>; a wrong or empty value leaves the map unexplored.</summary>
        private void Import(string packed)
        {
            if (!Unpack(packed, _explored)) return;
            foreach (var explored in _explored)
                if (explored) Count++;
        }

        /// <summary>A bit per flag, Base64.</summary>
        public static string Pack(bool[] cells)
        {
            var bytes = new byte[(cells.Length + 7) / 8];
            for (var i = 0; i < cells.Length; i++)
                if (cells[i])
                    bytes[i >> 3] |= (byte)(1 << (i & 7));
            return Convert.ToBase64String(bytes);
        }

        /// <summary>Fills <paramref name="cells"/> from <see cref="Pack"/>; false (untouched) when empty, broken or of another size.</summary>
        public static bool Unpack(string packed, bool[] cells)
        {
            if (string.IsNullOrEmpty(packed)) return false;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(packed); }
            catch (FormatException) { return false; }

            if (bytes.Length != (cells.Length + 7) / 8) return false; // another level size: ignore
            for (var i = 0; i < cells.Length; i++)
                cells[i] = (bytes[i >> 3] & (1 << (i & 7))) != 0;
            return true;
        }
    }
}
