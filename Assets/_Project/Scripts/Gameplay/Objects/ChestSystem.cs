using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Common;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Objects;
using Purge.Gameplay.Drops;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Waves;
using UnityEngine;

namespace Purge.Gameplay.Objects
{
    public enum ChestState
    {
        Closed = 0,

        /// <summary>The player is opening it (<see cref="Chest.Progress"/>).</summary>
        Opening = 1,

        /// <summary>A challenge chest: its champions are alive.</summary>
        Challenge = 2,

        /// <summary>Opened and its reward given (once per run).</summary>
        Open = 3,
    }

    /// <summary>What Use did to a chest.</summary>
    public enum ChestUse
    {
        None = 0,

        /// <summary>The opening started.</summary>
        Opening = 1,

        /// <summary>A locked chest and no chest key.</summary>
        NeedsKey = 2,
    }

    /// <summary>A chest of the level (stage 7в): stands in its cell (blocks movement), opened once per run.</summary>
    public sealed class Chest
    {
        private readonly List<int> _champions = new List<int>();
        private readonly List<int> _generations = new List<int>();

        internal Chest(ChestData data, ChestDefinition definition)
        {
            Data = data;
            Definition = definition;
        }

        public ChestData Data { get; }

        /// <summary>Its kind's opening time and reward; null — no asset of the kind (it opens with nothing in it).</summary>
        public ChestDefinition Definition { get; }

        public string Id => Data.Id;
        public ChestKind Kind => Data.Kind;
        public GridPosition Cell => Data.Position;
        public Vector2 Position => new Vector2(Data.Position.X, Data.Position.Y);
        public ChestState State { get; internal set; }

        /// <summary>0..1 of the opening.</summary>
        public float Progress { get; internal set; }

        /// <summary>Its challenge's champions still alive.</summary>
        public int ChampionsLeft => _champions.Count;

        internal void AddChampion(int slot, int generation)
        {
            _champions.Add(slot);
            _generations.Add(generation);
        }

        internal bool RemoveChampion(int slot, int generation)
        {
            for (var i = 0; i < _champions.Count; i++)
                if (_champions[i] == slot && _generations[i] == generation)
                {
                    _champions.RemoveAt(i);
                    _generations.RemoveAt(i);
                    return true;
                }

            return false;
        }
    }

    /// <summary>
    /// The level's chests (stage 7в, ТЗ «Сундуки»): each <see cref="ChestData"/> is a <see cref="Chest"/> standing in its
    /// cell (<see cref="CellBlockers"/>). Use next to it (<see cref="TryUse"/>, from <see cref="PlayerInteraction"/>)
    /// starts the opening: <see cref="ChestDefinition.OpenTime"/> s while the player stays next to it (stepping away
    /// stops it, damage does not). Then:
    /// <list type="bullet">
    /// <item>common — one item of its table, never nothing (× the difficulty's rarity: <see cref="DropSystem.Context"/>);</item>
    /// <item>locked — needs the chest key (spent): a weapon by the table's rarities;</item>
    /// <item>challenge — champions climb out around it (<see cref="HordeSystem.SpawnChampion"/>: kinds by the weights
    /// of the current wave's rank-and-file and officers, in the pause — the next wave's; they do not hold the wave);
    /// when all are dead — the locked chest's reward.</item>
    /// </list>
    /// The reward lies next to the chest until taken (a wave does not clear it). The chest key drops from the first
    /// elite killed (one per level with a locked chest). Opened chests and the key are kept by the run's save.
    /// </summary>
    public sealed class ChestSystem : ILevelLoadStep, ILevelTickable, IDisposable
    {
        /// <summary>The player may stand this far (cells, Chebyshev) from the chest while opening it.</summary>
        public const int Reach = 1;

        private readonly LevelData _level;
        private readonly CellBlockers _blockers;
        private readonly PlayerSystem _player;
        private readonly PlayerInventory _inventory;
        private readonly PickupSystem _pickups;
        private readonly DropPool _pool;
        private readonly DropSystem _drops;
        private readonly HordeSystem _horde;
        private readonly LevelPassability _passability;
        private readonly WaveSystem _waves;
        private readonly ContentCatalog _content;
        private readonly LevelLaunchOptions _options;
        private readonly List<Chest> _chests = new List<Chest>();
        private readonly List<ChestDefinition> _definitions = new List<ChestDefinition>();
        private readonly List<string> _opened = new List<string>();
        private readonly List<int> _kindPool = new List<int>();
        private readonly List<float> _kindWeights = new List<float>();
        private DeterministicRandom _random;
        private ChestKeyDefinition _key;
        private Chest _opening;
        private bool _subscribed;

        public ChestSystem(LevelData level, CellBlockers blockers, PlayerSystem player, PlayerInventory inventory,
            PickupSystem pickups, DropPool pool, DropSystem drops, HordeSystem horde, LevelPassability passability,
            WaveSystem waves = null, ContentCatalog content = null, LevelLaunchOptions options = null)
        {
            _level = level;
            _blockers = blockers;
            _player = player;
            _inventory = inventory;
            _pickups = pickups;
            _pool = pool;
            _drops = drops;
            _horde = horde;
            _passability = passability;
            _waves = waves;
            _content = content;
            _options = options;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeSystems;

        public IReadOnlyList<Chest> Chests => _chests;

        /// <summary>The chest being opened now; null — none.</summary>
        public Chest Opening => _opening;

        /// <summary>The chest key has dropped in this run (held or lying).</summary>
        public bool KeyDropped { get; private set; }

        /// <summary>Ids of the chests opened in this run (the run's save).</summary>
        public IReadOnlyList<string> OpenedIds => _opened;

        /// <summary>(chest) the opening started / stopped (the player stepped away).</summary>
        public event Action<Chest> OpeningStarted;
        public event Action<Chest> OpeningStopped;

        /// <summary>(chest) it opened: a reward lies next to it, or its challenge began.</summary>
        public event Action<Chest> Opened;

        /// <summary>(chest, champions) a challenge's champions climbed out.</summary>
        public event Action<Chest, int> ChallengeStarted;

        /// <summary>(chest) the challenge's champions are dead: its reward lies next to it.</summary>
        public event Action<Chest> ChallengeDone;

        /// <summary>(where) the chest key dropped from an elite.</summary>
        public event Action<Vector2> KeyDroppedAt;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            _definitions.Clear();
            _content?.GetAll(_definitions);
            var keys = new List<ChestKeyDefinition>();
            _content?.GetAll(keys);
            _key = keys.Count > 0 ? keys[0] : null;
            _random = new DeterministicRandom((int)StableHash.Of($"{_level.name}/chests/{_waves?.Seed ?? 0}"));

            var resume = _options?.Resume;
            var locked = false;
            foreach (var data in _level.Chests)
            {
                var definition = DefinitionOf(data.Kind);
                if (definition == null)
                    GameLog.Warning(LogChannel.Gameplay, $"No chest asset of kind {data.Kind}: '{data.Id}' opens with nothing.");
                var chest = new Chest(data, definition);
                if (resume != null && resume.OpenedChests.Contains(data.Id))
                {
                    chest.State = ChestState.Open;
                    _opened.Add(data.Id);
                }

                if (data.Kind == ChestKind.Locked) locked |= chest.State != ChestState.Open;
                _chests.Add(chest);
                _blockers.Add(data.Position);
                _pool.Include(definition != null ? definition.Reward : null);
            }

            if (_chests.Count == 0) return UniTask.CompletedTask;
            if (HasLocked) _pool.Include(_key);
            if (resume != null && resume.ChestKeyDropped)
            {
                KeyDropped = true;
                if (locked) _inventory.AddChestKey(); // held or lying when saved: the player has it
            }

            if (!_subscribed)
            {
                _horde.Killed += OnKilled;
                _subscribed = true;
            }

            GameLog.Info(LogChannel.Gameplay, $"Chests: {_chests.Count}, {_opened.Count} opened.");
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _horde.Killed -= OnKilled;
            _subscribed = false;
        }

        /// <summary>The level has a locked chest (the chest key drops).</summary>
        public bool HasLocked
        {
            get
            {
                foreach (var chest in _chests)
                    if (chest.Kind == ChestKind.Locked) return true;
                return false;
            }
        }

        public ChestDefinition DefinitionOf(ChestKind kind)
        {
            foreach (var definition in _definitions)
                if (definition != null && definition.Kind == kind)
                    return definition;
            return null;
        }

        /// <summary>A closed chest next to the player (the one they face first); null — none.</summary>
        public Chest FindUsable()
        {
            if (!_player.IsSpawned || _chests.Count == 0) return null;
            Chest best = null;
            var bestScore = float.NegativeInfinity;
            foreach (var chest in _chests)
            {
                if (chest.State != ChestState.Closed || !InReach(chest)) continue;
                var offset = chest.Position - _player.Position;
                var score = offset.sqrMagnitude > 1e-6f ? Vector2.Dot(_player.Facing, offset.normalized) : 1f;
                if (score <= bestScore) continue;
                best = chest;
                bestScore = score;
            }

            return best;
        }

        /// <summary>Use on the chest: starts its opening (a locked one only with the chest key).</summary>
        public ChestUse TryUse(Chest chest)
        {
            if (chest == null || chest.State != ChestState.Closed || !InReach(chest)) return ChestUse.None;
            if (chest.Kind == ChestKind.Locked && _inventory.ChestKeys <= 0) return ChestUse.NeedsKey;
            if (_opening != null) Stop(_opening);
            _opening = chest;
            chest.State = ChestState.Opening;
            chest.Progress = 0f;
            OpeningStarted?.Invoke(chest);
            return ChestUse.Opening;
        }

        public void Tick(float deltaTime)
        {
            var chest = _opening;
            if (chest == null) return;
            if (!InReach(chest) || chest.Kind == ChestKind.Locked && _inventory.ChestKeys <= 0)
            {
                Stop(chest);
                return;
            }

            var time = chest.Definition != null ? chest.Definition.OpenTime : 1f;
            chest.Progress = Mathf.Min(1f, chest.Progress + deltaTime / Mathf.Max(0.05f, time));
            if (chest.Progress >= 1f) Complete(chest);
        }

        private bool InReach(Chest chest)
        {
            if (!_player.IsSpawned) return false;
            var cell = _player.Cell;
            return cell.Floor == chest.Cell.Floor && Math.Abs(cell.X - chest.Cell.X) <= Reach && Math.Abs(cell.Y - chest.Cell.Y) <= Reach;
        }

        private void Stop(Chest chest)
        {
            if (_opening == chest) _opening = null;
            chest.State = ChestState.Closed;
            chest.Progress = 0f;
            OpeningStopped?.Invoke(chest);
        }

        private void Complete(Chest chest)
        {
            _opening = null;
            chest.Progress = 1f;
            switch (chest.Kind)
            {
                case ChestKind.Locked:
                    _inventory.UseChestKey();
                    GiveWeapon(chest);
                    Finish(chest);
                    break;
                case ChestKind.Challenge:
                    var count = StartChallenge(chest);
                    if (count == 0)
                    {
                        GiveWeapon(chest); // no monster could come: the reward at once
                        Finish(chest);
                        break;
                    }

                    chest.State = ChestState.Challenge;
                    Opened?.Invoke(chest);
                    ChallengeStarted?.Invoke(chest, count);
                    break;
                default:
                    Give(chest, DropRoller.Roll(TableOf(chest), _pool, Context(), _random, never: true));
                    Finish(chest);
                    break;
            }
        }

        private void Finish(Chest chest)
        {
            var wasChallenge = chest.State == ChestState.Challenge;
            chest.State = ChestState.Open;
            if (!_opened.Contains(chest.Id)) _opened.Add(chest.Id);
            GameLog.Info(LogChannel.Gameplay, $"Chest '{chest.Id}' ({chest.Kind}) opened.");
            if (wasChallenge) ChallengeDone?.Invoke(chest);
            else Opened?.Invoke(chest);
        }

        /// <summary>The player's state for the smart drop (no drop system — tests: a plain context).</summary>
        private DropContext Context() => _drops != null ? _drops.Context() : new DropContext { RarityFactor = 1f };

        private Core.Drops.DropTable TableOf(Chest chest) =>
            chest.Definition != null && chest.Definition.Reward != null ? chest.Definition.Reward : _pool.Table;

        private void GiveWeapon(Chest chest) => Give(chest, DropRoller.Weapon(TableOf(chest), _pool, Context(), _random));

        /// <summary>The reward lies in the free cell next to the chest nearest the player.</summary>
        private void Give(Chest chest, DropRoll roll)
        {
            if (roll.Kind == DropKind.Nothing)
            {
                GameLog.Warning(LogChannel.Gameplay, $"Chest '{chest.Id}': nothing to give (the drop pool is empty).");
                return;
            }

            var at = RewardPoint(chest);
            var floor = chest.Cell.Floor;
            switch (roll.Kind)
            {
                case DropKind.Ammo: _pickups.DropAmmo(at, floor, (AmmoDefinition)roll.Item, roll.Amount, 0f, keep: true); break;
                case DropKind.Medkit: _pickups.DropMedkit(at, floor, (MedkitDefinition)roll.Item, 0f, keep: true); break;
                case DropKind.Throwable: _pickups.DropThrowable(at, floor, (ThrowableDefinition)roll.Item, roll.Amount, 0f, keep: true); break;
                case DropKind.Weapon: _pickups.DropWeaponItem(at, floor, (WeaponDefinition)roll.Item, 0f, keep: true); break;
            }
        }

        private Vector2 RewardPoint(Chest chest)
        {
            var best = chest.Position;
            var bestDistance = float.MaxValue;
            var target = _player.IsSpawned ? _player.Position : chest.Position;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var cell = new GridPosition(chest.Cell.X + dx, chest.Cell.Y + dy, chest.Cell.Floor);
                if (!_passability.IsWalkable(cell)) continue;
                var point = new Vector2(cell.X, cell.Y);
                var distance = (point - target).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = point;
            }

            return best;
        }

        // ------------------------------------------------------------------ Challenge

        private int StartChallenge(Chest chest)
        {
            if (!_horde.IsActive) return 0;
            GatherKinds();
            if (_kindPool.Count == 0) return 0;
            var count = chest.Definition != null ? chest.Definition.Champions : 6;
            var radius = chest.Definition != null ? chest.Definition.ChampionRadius : 6f;
            var affixes = _horde.Champions != null ? _horde.Champions.Affixes.Length : 0;
            var spawned = 0;
            for (var i = 0; i < count; i++)
            {
                var kind = PickKind();
                var slot = _horde.SpawnChampion(chest.Position, kind, radius, affixes > 0 ? _random.NextInt(affixes) : -1);
                if (slot < 0) continue;
                chest.AddChampion(slot, _horde.Generations[slot]);
                spawned++;
            }

            GameLog.Info(LogChannel.Gameplay, $"Chest '{chest.Id}': challenge of {spawned} champion(s).");
            return spawned;
        }

        /// <summary>Rank-and-file and officers of the current wave (in the pause — the next one), else of the horde.</summary>
        private void GatherKinds()
        {
            _kindPool.Clear();
            _kindWeights.Clear();
            var scenario = _waves != null && _waves.IsActive ? _waves.Scenario : null;
            if (scenario != null && scenario.Count > 0)
            {
                var index = _waves.Phase == WavePhase.Running ? _waves.WaveIndex : _waves.WaveIndex + 1;
                index = Mathf.Clamp(index, 0, scenario.Count - 1);
                foreach (var monster in scenario.Waves[index].Monsters)
                    if (monster?.Monster != null && monster.Weight > 0f && ChampionRules.CanBe(monster.Monster.Rank))
                        AddKind(_horde.KindIndexOf(monster.Monster), monster.Weight);
            }

            if (_kindPool.Count > 0) return;
            var kinds = _horde.KindList;
            for (var k = 0; k < kinds.Count; k++)
            {
                var definition = kinds[k].Definition;
                if (definition != null && ChampionRules.CanBe(definition.Rank))
                    AddKind(k, Mathf.Max(0.01f, kinds[k].Weight));
            }
        }

        private void AddKind(int kind, float weight)
        {
            if (kind < 0) return;
            _kindPool.Add(kind);
            _kindWeights.Add(weight);
        }

        private int PickKind()
        {
            var total = 0f;
            foreach (var weight in _kindWeights) total += weight;
            var pick = (float)_random.NextDouble() * total;
            for (var i = 0; i < _kindPool.Count; i++)
                if ((pick -= _kindWeights[i]) < 0f) return _kindPool[i];
            return _kindPool[_kindPool.Count - 1];
        }

        private void OnKilled(int agent, Vector2 position)
        {
            var generation = _horde.Generations[agent];
            foreach (var chest in _chests)
            {
                if (chest.State != ChestState.Challenge || !chest.RemoveChampion(agent, generation)) continue;
                if (chest.ChampionsLeft > 0) return;
                GiveWeapon(chest);
                Finish(chest);
                return;
            }

            // ТЗ: ключ сундука — с первой убитой элиты.
            if (KeyDropped || !HasLocked) return;
            var definition = _horde.DefinitionOf(agent);
            if (definition == null || definition.Rank != MonsterRank.Elite) return;
            KeyDropped = true;
            if (_key == null) GameLog.Warning(LogChannel.Gameplay, "No chest key asset: the key has no look.");
            _pickups.DropChestKey(position, _player.Floor, _key);
            GameLog.Info(LogChannel.Gameplay, "The chest key dropped.");
            KeyDroppedAt?.Invoke(position);
        }
    }
}
