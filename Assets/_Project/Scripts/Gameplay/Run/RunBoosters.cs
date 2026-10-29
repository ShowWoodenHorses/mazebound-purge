using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Boosters;
using Purge.Core.Common;
using Purge.Core.Grid;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using UnityEngine;

namespace Purge.Gameplay.Run
{
    /// <summary>
    /// The run's boosters that act after the player is placed (stage 11б), step <see cref="LevelLoadStage.SpawnZombies"/>
    /// after the cards: <see cref="StartCardBoost"/> — cards to choose at once (<see cref="RunExperience.AddPending"/>),
    /// <see cref="RerollBoost"/> — free rerolls (<see cref="CardSystem.FreeRerolls"/>; a continued run gets its left
    /// ones), <see cref="MedkitBoost"/> — a medkit next to the start that lies the whole run (a continued run puts it back
    /// until it is taken). Throwables — <see cref="RunLoadout"/>, scouting — <c>MapExploration</c>.
    /// </summary>
    public sealed class RunBoosters : ILevelLoadStep, IDisposable
    {
        // The start's neighbours, in the order tried: sides first.
        private static readonly Vector2Int[] Around =
        {
            new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(-1, 1),
        };

        private readonly LevelLaunchOptions _options;
        private readonly RunExperience _experience;
        private readonly CardSystem _cards;
        private readonly PickupSystem _pickups;
        private readonly PlayerSystem _player;
        private readonly LevelPassability _passability;
        private string _medkitId;

        public RunBoosters(LevelLaunchOptions options, RunExperience experience, CardSystem cards, PickupSystem pickups,
            PlayerSystem player, LevelPassability passability)
        {
            _options = options ?? LevelLaunchOptions.Default;
            _experience = experience;
            _cards = cards;
            _pickups = pickups;
            _player = player;
            _passability = passability;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        /// <summary>The booster's medkit was taken (the run's save keeps it).</summary>
        public bool MedkitTaken { get; private set; }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var resume = _options.Resume;
            if (resume == null)
            {
                if (_options.TryGetBoost<StartCardBoost>(out var card) && _experience.IsActive) _experience.AddPending(card.Cards);
                if (_options.TryGetBoost<RerollBoost>(out var reroll)) _cards.SetFreeRerolls(reroll.Rerolls);
            }
            else
            {
                _cards.SetFreeRerolls(resume.FreeRerolls);
                MedkitTaken = resume.StartMedkitTaken;
            }

            if (!MedkitTaken && _options.TryGetBoost<MedkitBoost>(out var medkit) && medkit.Medkit != null) PlaceMedkit(medkit);
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_medkitId != null) _pickups.Removed -= OnRemoved;
            _medkitId = null;
        }

        private void PlaceMedkit(MedkitBoost boost)
        {
            if (!_player.IsSpawned) return;
            var start = _player.Cell;
            var cell = start;
            foreach (var step in Around)
            {
                var next = new GridPosition(start.X + step.x, start.Y + step.y, start.Floor);
                if (!_passability.IsWalkable(next)) continue;
                cell = next;
                break;
            }

            var pickup = _pickups.DropMedkit(new Vector2(cell.X, cell.Y), cell.Floor, boost.Medkit, 0f, keep: true);
            if (pickup == null) return;
            _medkitId = pickup.Id;
            _pickups.Removed += OnRemoved;
            GameLog.Info(LogChannel.Gameplay, $"Booster medkit at {cell}.");
        }

        private void OnRemoved(Pickup pickup)
        {
            if (pickup.Id != _medkitId || !pickup.WasTaken) return;
            MedkitTaken = true;
            _pickups.Removed -= OnRemoved;
            _medkitId = null;
        }
    }
}
