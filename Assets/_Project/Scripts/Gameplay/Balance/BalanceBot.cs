using System.Collections.Generic;
using Purge.Core.Balance;
using Purge.Core.Cards;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Navigation;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Grid;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Navigation;
using Purge.Gameplay.Objects;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Run;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Weapons;
using UnityEngine;

namespace Purge.Gameplay.Balance
{
    /// <summary>
    /// The balance bot (stage 10a, decisions 2026-10-29): plays a run by the rules through <see cref="BotInput"/>.
    /// <list type="bullet">
    /// <item>Shoots the nearest monster in reach with a clear line (the aim stick: exactly at it); a barrel with a
    /// crowd around it first. Throws the throwable into a crowd (<see cref="BotPlan.ThrowCrowd"/>).</item>
    /// <item>Keeps its distance: monsters closer than <see cref="BotPlan.KeepDistance"/> — it backs off where it is least
    /// crowded and most open (16 directions, walls and corners avoided); far ones — it walks to them (A*, at most once
    /// a second); in between — it stands and shoots. With melee in hands — it closes in and backs off only when swarmed.</item>
    /// <item>No monster within <see cref="BotPlan.SafeDistance"/>: errands within <see cref="BotPlan.ErrandRadius"/> —
    /// drop it needs (ammo of its guns, medkits when hurt, throwables with room, chest keys, a weapon rarer than its
    /// worst), closed chests (a locked one with the key).</item>
    /// <item>Weapons: the rarest one with rounds in hands; cards — by <see cref="BotPlan.CardPriority"/>, then the rarest.
    /// No vehicle, no countdown skip, no revive (<see cref="PlayerRevival.Allowed"/>).</item>
    /// </list>
    /// Decides <see cref="ThinkInterval"/> times a second; the aim follows its target every tick. Registered after every
    /// gameplay system (its presses are read on the next tick) and only for a bot's run.
    /// </summary>
    public sealed class BalanceBot : ILevelTickable
    {
        private const float ThinkInterval = 0.1f;
        private const int MaxNear = 256;
        private const int Directions = 16;
        private const int OpenCap = 6;
        private const int MaxThrowCandidates = 24;
        private const float ArriveDistance = 0.35f;
        private const float RepathTime = 1f;
        private const float StuckTime = 1.5f;
        private const float StuckDistance = 0.3f;
        private const float IgnoreTime = 15f;
        private const float ErrandCheck = 0.5f;
        private const float MeleeSwarm = 2.5f;
        private const int MeleeSwarmCount = 4;
        private const float DryScore = -10f;
        private const float MeleeShare = 0.5f;
        private const float ArenaCircle = 0.6f;
        private const float HazardDanger = 50f;
        private const int EscapeRadius = 6;
        private const float SeniorMargin = 2.5f;
        private const float SeniorDanger = 3f;
        private const float Hysteresis = 1f;
        private const float BlindTime = 3f;

        private readonly BotPlan _plan;
        private readonly BotInput _input;
        private readonly PlayerSystem _player;
        private readonly PlayerHealth _health;
        private readonly PlayerInventory _inventory;
        private readonly HordeSystem _horde;
        private readonly CombatTargets _targets;
        private readonly PlayerCombat _combat;
        private readonly WeaponSystem _weapons;
        private readonly PickupSystem _pickups;
        private readonly PlayerThrowing _throwing;
        private readonly ChestSystem _chests;
        private readonly BarrelSystem _barrels;
        private readonly CardSystem _cards;
        private readonly RunExperience _experience;
        private readonly LevelPassability _passability;
        private readonly ZoneSystem _zones;
        private readonly BossSystem _bosses;
        private readonly LevelGrid _grid;
        private readonly GridPathfinder _pathfinder;
        private readonly int _width;
        private readonly int _height;

        private readonly int[] _near = new int[MaxNear];
        private readonly int[] _crowd = new int[MaxNear];
        private readonly List<GridPosition> _path = new List<GridPosition>();
        private readonly List<Pickup> _found = new List<Pickup>();
        private readonly Dictionary<string, float> _ignored = new Dictionary<string, float>();
        private readonly Queue<int> _queue = new Queue<int>();
        private byte[] _openness;
        private int _opennessFloor = -1;
        private int _nearCount;

        private float _time;
        private float _thinkLeft;
        private Vector2 _lastMove;
        private int _targetAgent = -1;
        private Barrel _targetBarrel;
        private float _targetSeenAt;
        private bool _wasBlind;
        private bool _holding;
        private Vector2 _fleeFrom;
        private float _fleeCheckAt;

        private Pickup _errandPickup;
        private Chest _errandChest;
        private float _errandCheckAt;
        private float _errandSince;
        private GridPosition _pathGoal = new GridPosition(-1, -1);
        private int _pathIndex;
        private float _repathAt;
        private Vector2 _stuckFrom;
        private float _stuckCheckAt;

        public BalanceBot(LevelLaunchOptions options, BotInput input, LevelGrid grid, PlayerSystem player, PlayerHealth health,
            PlayerInventory inventory, PlayerRevival revival, HordeSystem horde, CombatTargets targets, PlayerCombat combat,
            WeaponSystem weapons, PickupSystem pickups, PlayerThrowing throwing, ChestSystem chests, BarrelSystem barrels,
            CardSystem cards, RunExperience experience, LevelPassability passability, ZoneSystem zones, BossSystem bosses)
        {
            _zones = zones;
            _bosses = bosses;
            _grid = grid;
            _plan = options.Bot.Plan;
            _input = input;
            _player = player;
            _health = health;
            _inventory = inventory;
            _horde = horde;
            _targets = targets;
            _combat = combat;
            _weapons = weapons;
            _pickups = pickups;
            _throwing = throwing;
            _chests = chests;
            _barrels = barrels;
            _cards = cards;
            _experience = experience;
            _passability = passability;
            _width = grid.Width;
            _height = grid.Height;
            _pathfinder = new GridPathfinder(_width, _height);
            revival.Allowed = false;
        }

        public void Tick(float deltaTime)
        {
            _input.ClearPresses();
            _time += deltaTime;
            if (!_player.IsSpawned || _player.Down || !_horde.IsActive)
            {
                _input.Move = Vector2.zero;
                _input.AimStick = Vector2.zero;
                return;
            }

            TakeCards();
            _thinkLeft -= deltaTime;
            if (_thinkLeft <= 0f)
            {
                _thinkLeft = ThinkInterval;
                Think();
            }

            Aim();
        }

        // ── Cards ───────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Before the cards window opens (the views' late tick): every level-up waiting is chosen here.</summary>
        private void TakeCards()
        {
            if (!_cards.IsActive) return;
            for (var guard = 0; guard < 16 && _experience.Pending > 0 && _cards.OpenNext(); guard++)
                _cards.Choose(Pick(_cards.Offer));
        }

        private int Pick(IReadOnlyList<CardDefinition> offer)
        {
            var best = 0;
            for (var i = 1; i < offer.Count; i++)
            {
                var priority = _plan.PriorityOf(offer[i]);
                var bestPriority = _plan.PriorityOf(offer[best]);
                if (priority < bestPriority || priority == bestPriority && offer[i].Rarity > offer[best].Rarity)
                    best = i;
            }

            return best;
        }

        // ── Thinking ────────────────────────────────────────────────────────────────────────────────────────────────

        private void Think()
        {
            var origin = _player.Position;
            var floor = _player.Floor;
            EnsureOpenness(floor);
            _nearCount = _horde.QueryCone(origin, Vector2.up, _plan.ThreatRadius, -1f, _near);

            ChooseWeapon();
            ChooseTarget(origin, floor);
            if (_targetAgent >= 0 || _targetBarrel != null || _horde.AliveCount == 0) _targetSeenAt = _time;
            TryThrow(origin, floor);
            Move(origin, floor);
        }

        private void ChooseWeapon()
        {
            var active = _weapons.ActiveSlot;
            var other = active == WeaponSlot.First ? WeaponSlot.Second : WeaponSlot.First;
            // Both guns dry: the one in hands goes (as a held slot key), the base pistol with its endless rounds comes back.
            if (WeaponScore(active) == DryScore && WeaponScore(other) == DryScore)
            {
                _pickups.DropWeapon(active);
                return;
            }

            if (WeaponScore(other) > WeaponScore(active))
                _input.Press(other == WeaponSlot.First ? PlayerAction.Slot1 : PlayerAction.Slot2);
        }

        /// <summary>
        /// Damage per second of steady fire (10v; was the rarity — a green sword over a gray shotgun, and no shot while
        /// backing off): melee counts at <see cref="MeleeShare"/> — in hands only when much stronger; a gun out of rounds
        /// below everything.
        /// </summary>
        private float WeaponScore(WeaponSlot slot)
        {
            var weapon = _weapons.Get(slot);
            if (weapon == null) return -100f;
            if (_combat.IsOutOfAmmo(weapon)) return DryScore;
            var dps = WeaponStats.DamagePerSecond(weapon.Definition);
            return weapon.Definition.IsMelee ? dps * MeleeShare : dps;
        }

        private int WorstRarity()
        {
            var worst = int.MaxValue;
            for (var slot = WeaponSlot.First; slot <= WeaponSlot.Second; slot++)
            {
                var weapon = _weapons.Get(slot);
                worst = Mathf.Min(worst, weapon == null || weapon.IsBase ? -1 : (int)weapon.Definition.Rarity);
            }

            return worst;
        }

        private WeaponSlot WorstSlot() =>
            Rarity(WeaponSlot.Second) < Rarity(WeaponSlot.First) ? WeaponSlot.Second : WeaponSlot.First;

        private int Rarity(WeaponSlot slot)
        {
            var weapon = _weapons.Get(slot);
            return weapon == null || weapon.IsBase ? -1 : (int)weapon.Definition.Rarity;
        }

        private float AimRange()
        {
            var attack = _weapons.Active?.Definition.Attack;
            return attack != null ? attack.AimRange : 0f;
        }

        private bool MeleeInHands => _weapons.Active != null && _weapons.Active.Definition.IsMelee;

        // ── Shooting and throwing ───────────────────────────────────────────────────────────────────────────────────

        private void ChooseTarget(Vector2 origin, int floor)
        {
            _targetAgent = -1;
            _targetBarrel = null;
            var range = AimRange();
            if (range <= 0f) return;

            var best = float.MaxValue;
            if (!MeleeInHands)
                foreach (var barrel in _barrels.Barrels)
                {
                    if (!barrel.IsAlive || barrel.Floor != floor) continue;
                    var distance = Vector2.Distance(barrel.Position, origin);
                    if (distance > range || distance >= best) continue;
                    if (_horde.QueryCone(barrel.Position, Vector2.up, _plan.CrowdRadius, -1f, _crowd) < _plan.BarrelCrowd) continue;
                    if (!_targets.IsClear(origin, barrel.Position, floor)) continue;
                    best = distance;
                    _targetBarrel = barrel;
                }

            if (_targetBarrel != null) return;

            var positions = _horde.Positions;
            for (var i = 0; i < _nearCount; i++)
            {
                var agent = _near[i];
                var distance = Vector2.Distance(positions[agent], origin) - _horde.RadiusOf(agent);
                // Seniors and champions first: they count nearer than they are.
                var score = IsSenior(agent) ? distance - SeniorMargin - 0.5f : distance;
                if (distance > range || score >= best || !_targets.IsClear(origin, positions[agent], floor)) continue;
                best = score;
                _targetAgent = agent;
            }

            if (_targetAgent >= 0 || range <= _plan.ThreatRadius) return;
            var far = _horde.FindNearest(origin, range);
            if (far >= 0 && _targets.IsClear(origin, positions[far], floor)) _targetAgent = far;
        }

        /// <summary>Every tick: the aim stick at the target where it is now (nothing in reach — let go).</summary>
        private void Aim()
        {
            Vector2 at;
            if (_targetBarrel != null && _targetBarrel.IsAlive) at = _targetBarrel.Position;
            else if (_targetAgent >= 0 && _targetAgent < _horde.HighWater && HordeSystem.IsAlive(_horde.States[_targetAgent]))
                at = _horde.Positions[_targetAgent];
            else
            {
                _input.AimStick = Vector2.zero;
                return;
            }

            var offset = at - _player.Position;
            var reach = AimRange() + (_targetAgent >= 0 ? _horde.RadiusOf(_targetAgent) : 0.5f) + 0.5f;
            _input.AimStick = offset.sqrMagnitude > 1e-4f && offset.sqrMagnitude <= reach * reach ? offset.normalized : Vector2.zero;
        }

        private void TryThrow(Vector2 origin, int floor)
        {
            var slot = _throwing.Slot;
            if (slot.IsEmpty || _throwing.Cooldown > 0f || _throwing.IsAiming || _player.Stunned || _nearCount == 0) return;
            var range = slot.Definition.Range;
            var minDistance = Mathf.Max(2f, slot.Definition.EffectRadius);
            var positions = _horde.Positions;
            var step = Mathf.Max(1, _nearCount / MaxThrowCandidates);
            var bestCount = _plan.ThrowCrowd - 1;
            var bestAt = Vector2.zero;
            for (var i = 0; i < _nearCount; i += step)
            {
                var at = positions[_near[i]];
                var distance = Vector2.Distance(at, origin);
                if (distance < minDistance || distance > range) continue;
                var count = _horde.QueryCone(at, Vector2.up, _plan.CrowdRadius, -1f, _crowd);
                if (count <= bestCount || !_targets.IsClear(origin, at, floor)) continue;
                bestCount = count;
                bestAt = at;
            }

            if (bestCount >= _plan.ThrowCrowd) _throwing.TryThrow(bestAt);
        }

        // ── Moving ──────────────────────────────────────────────────────────────────────────────────────────────────

        private void Move(Vector2 origin, int floor)
        {
            var nearest = NearestFree(origin);
            var distance = nearest >= 0 ? Vector2.Distance(_horde.Positions[nearest], origin) : float.MaxValue;
            var hasTarget = _targetAgent >= 0 || _targetBarrel != null;
            var low = _health.Current * 4 < _health.Max;

            var threat = ThreatDistance(origin);
            // Nothing to shoot for a while (a boss standing behind a tree at the edge of the keep distance waits, and so
            // would the bot — the wave never ends): to the nearest by A* until a clear line, the distance not kept.
            var blind = _horde.AliveCount > 0 && _time - _targetSeenAt > BlindTime && !low;
            // The line found blind is held: off it only when a monster comes (not a waiting boss) or the health is low.
            if (_wasBlind && !blind && hasTarget) _holding = true;
            if (!hasTarget || low || threat < KeepDistance()) _holding = false;
            _wasBlind = blind;

            Vector2 move;
            if (!blind && (MeleeInHands ? Swarmed(origin) : threat < KeepDistance()))
            {
                // Pressed into something for a while: the steadiness turned aside, or it keeps the same way for good.
                if (Activity == BotActivity.Flee && _time >= _fleeCheckAt)
                {
                    if ((origin - _fleeFrom).sqrMagnitude < StuckDistance * StuckDistance) _lastMove = new Vector2(-_lastMove.y, _lastMove.x);
                    _fleeFrom = origin;
                    _fleeCheckAt = _time + StuckTime;
                }
                else if (Activity != BotActivity.Flee)
                {
                    _fleeFrom = origin;
                    _fleeCheckAt = _time + StuckTime;
                }

                move = Flee(origin, floor);
                Activity = BotActivity.Flee;
            }
            // The boss waits in its arena until the player is in with the whole body, and the wave waits for the boss:
            // nothing else around — in, to the arena's centre, as a player would (not shooting it from outside).
            else if (BossWaits && (nearest < 0 || distance > _plan.ThreatRadius) &&
                     Follow(origin, PlayerMovement.CellOf(_bosses.ArenaCenter, floor), _bosses.ArenaCenter, chase: false, out move))
            {
                Activity = BotActivity.Arena;
            }
            else if (_holding)
            {
                move = Vector2.zero;
                Activity = BotActivity.Stand;
            }
            else if (blind && nearest < 0)
            {
                // Only waiting monsters left and no way into the arena: to the nearest of them.
                var any = _horde.FindNearest(origin, float.MaxValue);
                if (any < 0 || !Follow(origin, PlayerMovement.CellOf(_horde.Positions[any], floor), _horde.Positions[any], chase: true, out move))
                    move = Vector2.zero;
                Activity = BotActivity.Approach;
            }
            else if (distance >= _plan.SafeDistance && Errand(origin, floor, out move))
            {
                Activity = BotActivity.Errand;
            }
            // Too far — or near, but behind a tree or a wall: around it to a clear line. Never right back into the
            // distance it has just backed off from (no back and forth at its edge).
            else if (nearest >= 0 && (blind || (MeleeInHands || threat >= KeepDistance() + Hysteresis) &&
                     (distance > PreferredDistance() || !hasTarget)))
            {
                var goal = PlayerMovement.CellOf(_horde.Positions[nearest], floor);
                if (!Follow(origin, goal, _horde.Positions[nearest], chase: true, out move))
                    move = (_horde.Positions[nearest] - origin).normalized;
                Activity = BotActivity.Approach;
            }
            else
            {
                move = Vector2.zero;
                Activity = BotActivity.Stand;
            }

            move = AvoidHazards(origin, floor, move);
            _input.Move = move;
            if (move.sqrMagnitude > 0f) _lastMove = move;
        }

        /// <summary>
        /// How close the bot lets the monsters come before it walks no further: the gun's reach, within the threat radius
        /// (a short gun — a flamer — closer than the plan's keep distance).
        /// </summary>
        private float PreferredDistance() =>
            MeleeInHands ? 1f : Mathf.Min(AimRange() * 0.85f, Mathf.Max(_plan.KeepDistance + 1f, Mathf.Min(AimRange() * 0.85f, _plan.ThreatRadius)));

        /// <summary>A boss waits in its closed arena for the player (<see cref="HordeSystem.IsHeld"/>).</summary>
        /// <summary>What the last move was about (the recorder splits a wave's time by it).</summary>
        public BotActivity Activity { get; private set; }

        /// <summary>There is a target to shoot (a monster with a clear line or a barrel by a crowd).</summary>
        public bool Shooting => _targetAgent >= 0 || _targetBarrel != null;

        private bool BossWaits => _bosses.IsBossAlive && _bosses.HasArena && _horde.IsHeld(_bosses.Boss);

        /// <summary>Nearest living monster that is not waiting (a held boss is no threat and no one to walk up to).</summary>
        private int NearestFree(Vector2 origin)
        {
            var best = -1;
            var bestDistance = float.MaxValue;
            var positions = _horde.Positions;
            var states = _horde.States;
            for (var i = 0; i < _horde.HighWater; i++)
            {
                if (!HordeSystem.IsAlive(states[i]) || _horde.IsHeld(i)) continue;
                var distance = (positions[i] - origin).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// Distance to the nearest threat around: to its body, and a senior (officer, elite, boss) or a champion counts
        /// <see cref="SeniorMargin"/> nearer — they dash, slam and hit hard. A waiting boss is no threat.
        /// </summary>
        private float ThreatDistance(Vector2 origin)
        {
            var best = float.MaxValue;
            var positions = _horde.Positions;
            for (var i = 0; i < _nearCount; i++)
            {
                var agent = _near[i];
                if (_horde.IsHeld(agent)) continue;
                var distance = Vector2.Distance(positions[agent], origin) - _horde.RadiusOf(agent);
                if (IsSenior(agent)) distance -= SeniorMargin;
                if (distance < best) best = distance;
            }

            return best;
        }

        private bool IsSenior(int agent) =>
            _horde.DefinitionOf(agent).Rank != Core.Monsters.MonsterRank.Mass || _horde.IsChampion(agent);

        /// <summary>Closer than this the bot backs off: the plan's, but within a short gun's reach.</summary>
        private float KeepDistance() => Mathf.Min(_plan.KeepDistance, AimRange() * 0.6f);

        /// <summary>Melee backs off surrounded, or low on health with a threat near (nobody around — nothing to flee).</summary>
        private bool Swarmed(Vector2 origin) =>
            _horde.QueryCone(origin, Vector2.up, MeleeSwarm, -1f, _crowd) >= MeleeSwarmCount ||
            _health.Current * 4 < _health.Max && ThreatDistance(origin) < _plan.KeepDistance;

        /// <summary>The way out: of 16 directions with two free steps — the least crowded ahead, the most open, the steadiest.</summary>
        private Vector2 Flee(Vector2 origin, int floor)
        {
            var positions = _horde.Positions;
            var best = Vector2.zero;
            var bestScore = float.MinValue;
            // In the closed arena with the boss: round it (sideways to it), not into a wall of the arena and stuck there.
            var tangent = Vector2.zero;
            if (_bosses.IsBossAlive && _bosses.PlayerInArena && !_horde.IsHeld(_bosses.Boss))
            {
                var away = origin - positions[_bosses.Boss];
                if (away.sqrMagnitude > 1e-4f) tangent = new Vector2(-away.y, away.x).normalized;
            }

            for (var i = 0; i < Directions; i++)
            {
                var angle = i * (Mathf.PI * 2f / Directions);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                // The first half-step too: a corner right by the body would hold it in place for good.
                if (!Walkable(origin + direction * 0.45f, floor) || !Walkable(origin + direction * 0.9f, floor) ||
                    !Walkable(origin + direction * 1.8f, floor)) continue;

                var sample = origin + direction * 2f;
                var danger = 0f;
                for (var n = 0; n < _nearCount; n++)
                    if (!_horde.IsHeld(_near[n]))
                        danger += (IsSenior(_near[n]) ? SeniorDanger : 1f) / ((positions[_near[n]] - sample).sqrMagnitude + 1f);
                if (Hazard(origin + direction * 0.9f, floor) || Hazard(origin + direction * 1.8f, floor)) danger += HazardDanger;
                var score = -danger * 4f + Openness(origin + direction * 1.8f, floor) * 0.25f + Vector2.Dot(direction, _lastMove) * 0.3f +
                            Mathf.Abs(Vector2.Dot(direction, tangent)) * ArenaCircle;
                if (score <= bestScore) continue;
                bestScore = score;
                best = direction;
            }

            if (bestScore > float.MinValue) return best;
            var nearest = _horde.FindNearest(origin, _plan.ThreatRadius);
            return nearest >= 0 ? (origin - positions[nearest]).normalized : Vector2.zero;
        }

        // ── Hazards: hostile zones (monsters' fire, acid…) hurt the player; its own never do ──────────────────────────

        private bool Hazard(Vector2 point, int floor) => HazardAt(PlayerMovement.CellOf(point, floor));

        private bool HazardAt(GridPosition cell) => _grid.IsInside(cell) && _zones.IsHostileAt(_grid.ToIndex(cell));

        /// <summary>
        /// The last word on every move: standing in a hostile zone — out to the nearest safe cell; a step into one — the
        /// nearest direction by angle whose two steps are free and safe (none — the step as it was).
        /// </summary>
        private Vector2 AvoidHazards(Vector2 origin, int floor, Vector2 move)
        {
            if (Hazard(origin, floor)) return Escape(origin, floor);
            if (move.sqrMagnitude <= 0f || !Hazard(origin + move * 0.9f, floor) && !Hazard(origin + move * 1.6f, floor))
                return move;

            var wanted = move.normalized;
            var best = move;
            var bestDot = float.MinValue;
            for (var i = 0; i < Directions; i++)
            {
                var angle = i * (Mathf.PI * 2f / Directions);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var dot = Vector2.Dot(direction, wanted);
                if (dot <= bestDot || !Safe(origin + direction * 0.9f, floor) || !Safe(origin + direction * 1.6f, floor)) continue;
                bestDot = dot;
                best = direction;
            }

            return best;
        }

        private bool Safe(Vector2 point, int floor) => Walkable(point, floor) && !Hazard(point, floor);

        /// <summary>Towards the nearest walkable cell without a hostile zone (rings up to <see cref="EscapeRadius"/>).</summary>
        private Vector2 Escape(Vector2 origin, int floor)
        {
            var from = PlayerMovement.CellOf(origin, floor);
            var best = float.MaxValue;
            var to = origin;
            for (var radius = 1; radius <= EscapeRadius && best == float.MaxValue; radius++)
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;
                var cell = new GridPosition(from.X + dx, from.Y + dy, floor);
                if (!_passability.IsWalkable(cell) || HazardAt(cell)) continue;
                var distance = (Centre(cell) - origin).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                to = Centre(cell);
            }

            var offset = to - origin;
            return offset.sqrMagnitude > 0.01f ? offset.normalized : _lastMove;
        }

        // ── Errands ─────────────────────────────────────────────────────────────────────────────────────────────────

        private bool Errand(Vector2 origin, int floor, out Vector2 move)
        {
            move = Vector2.zero;
            if (!ErrandValid()) SetErrand(null, null);
            if (_errandPickup == null && _errandChest == null && _time >= _errandCheckAt)
            {
                _errandCheckAt = _time + ErrandCheck;
                ChooseErrand(origin, floor);
            }

            if (_errandPickup == null && _errandChest == null) return false;
            if (Stuck(origin))
            {
                Ignore(_errandPickup != null ? _errandPickup.Id : _errandChest.Id);
                SetErrand(null, null);
                return false;
            }

            if (_errandChest != null)
            {
                var chest = _errandChest;
                if (Chebyshev(_player.Cell, chest.Cell) <= ChestSystem.Reach)
                {
                    if (_chests.Opening != chest && _chests.TryUse(chest) == ChestUse.NeedsKey)
                    {
                        Ignore(chest.Id);
                        SetErrand(null, null);
                    }

                    _errandSince = _time; // standing on purpose: not stuck
                    _stuckFrom = origin;
                    return true;
                }

                if (!TryChestGoal(chest, origin, out var goal) || !Follow(origin, goal, new Vector2(goal.X, goal.Y), chase: false, out move))
                {
                    Ignore(chest.Id);
                    SetErrand(null, null);
                    return false;
                }

                return true;
            }

            var pickup = _errandPickup;
            if (_player.Cell == pickup.Cell)
            {
                if (pickup.Kind == PickupKind.Weapon) TakeWeapon(pickup);
                move = pickup.Position - origin;
                move = move.sqrMagnitude > 0.01f ? move.normalized * 0.5f : Vector2.zero;
                return true;
            }

            if (!Follow(origin, pickup.Cell, pickup.Position, chase: false, out move))
            {
                Ignore(pickup.Id);
                SetErrand(null, null);
                return false;
            }

            return true;
        }

        /// <summary>
        /// In the weapon's cell, in one step (no key presses: the weapon choice would switch the slot back before Use):
        /// the worst slot in hands when both are full (a full pair replaces the one in hands), then exactly this weapon —
        /// not whatever lies first in the cell. The weapon it replaces is not wanted back for a while.
        /// </summary>
        private void TakeWeapon(Pickup pickup)
        {
            var worst = WorstSlot();
            var full = !_weapons.IsEmpty(WeaponSlot.First) && !_weapons.IsEmpty(WeaponSlot.Second);
            if (full) _weapons.Activate(worst);
            var replaced = full ? _weapons.Get(worst) : null;
            if (_pickups.TryTakeWeapon(_player.Cell, pickup) && replaced != null) Ignore(replaced.Id);
        }

        private bool ErrandValid()
        {
            if (_errandPickup != null) return !_errandPickup.WasTaken && Wanted(_errandPickup);
            if (_errandChest == null) return false;
            var state = _errandChest.State;
            return (state == ChestState.Closed || state == ChestState.Opening) &&
                   (_errandChest.Kind != ChestKind.Locked || _inventory.ChestKeys > 0);
        }

        private void ChooseErrand(Vector2 origin, int floor)
        {
            Pickup bestPickup = null;
            Chest bestChest = null;
            var best = _plan.ErrandRadius;
            _found.Clear();
            _pickups.GetAll(_found);
            foreach (var pickup in _found)
            {
                if (pickup.WasTaken || pickup.Cell.Floor != floor || HazardAt(pickup.Cell) || !Wanted(pickup) ||
                    IsIgnored(pickup.Id)) continue;
                var distance = Vector2.Distance(pickup.Position, origin);
                if (distance >= best) continue;
                best = distance;
                bestPickup = pickup;
            }

            _found.Clear();
            foreach (var chest in _chests.Chests)
            {
                if (chest.State != ChestState.Closed || chest.Cell.Floor != floor || IsIgnored(chest.Id)) continue;
                if (chest.Kind == ChestKind.Locked && _inventory.ChestKeys <= 0) continue;
                var distance = Vector2.Distance(chest.Position, origin);
                if (distance >= best) continue;
                best = distance;
                bestChest = chest;
                bestPickup = null;
            }

            SetErrand(bestPickup, bestChest);
        }

        private bool Wanted(Pickup pickup)
        {
            switch (pickup.Kind)
            {
                case PickupKind.ChestKey:
                    return true;
                case PickupKind.Medkit:
                    return !_health.IsFull;
                case PickupKind.Throwable:
                    return _throwing.Slot.Room(pickup.Throwable) > 0;
                case PickupKind.Ammo:
                    return Uses(WeaponSlot.First, pickup.Ammo) || Uses(WeaponSlot.Second, pickup.Ammo);
                case PickupKind.Weapon:
                    return WantedWeapon(pickup.Weapon);
                default:
                    return false;
            }
        }

        /// <summary>Rarer than the worst in the slots, not one already held, and with rounds to fire.</summary>
        private bool WantedWeapon(WeaponRuntime weapon)
        {
            if (weapon == null || (int)weapon.Definition.Rarity <= WorstRarity()) return false;
            for (var slot = WeaponSlot.First; slot <= WeaponSlot.Second; slot++)
                if (_weapons.Get(slot)?.Definition == weapon.Definition) return false;
            return !weapon.Definition.UsesAmmo || weapon.Ammo > 0 || _combat.ReserveOf(weapon.Definition) > 0;
        }

        private bool Uses(WeaponSlot slot, AmmoDefinition ammo)
        {
            var weapon = _weapons.Get(slot);
            return weapon != null && !weapon.IsBase && weapon.Definition.UsesAmmo && weapon.Definition.Ammo.Ammo == ammo;
        }

        private void SetErrand(Pickup pickup, Chest chest)
        {
            if (pickup == _errandPickup && chest == _errandChest) return;
            _errandPickup = pickup;
            _errandChest = chest;
            _errandSince = _time;
            _stuckFrom = _player.Position;
            _stuckCheckAt = _time + StuckTime;
            _pathGoal = new GridPosition(-1, -1);
        }

        /// <summary>A free cell next to the chest, nearest to the player.</summary>
        private bool TryChestGoal(Chest chest, Vector2 origin, out GridPosition goal)
        {
            goal = default;
            var best = float.MaxValue;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var cell = new GridPosition(chest.Cell.X + dx, chest.Cell.Y + dy, chest.Cell.Floor);
                if (!_passability.IsWalkable(cell) || HazardAt(cell)) continue;
                var distance = (new Vector2(cell.X, cell.Y) - origin).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                goal = cell;
            }

            return best < float.MaxValue;
        }

        /// <summary>No real progress for <see cref="StuckTime"/> while on an errand.</summary>
        private bool Stuck(Vector2 origin)
        {
            if (_time < _stuckCheckAt) return false;
            var stuck = (origin - _stuckFrom).sqrMagnitude < StuckDistance * StuckDistance && _time - _errandSince >= StuckTime;
            _stuckFrom = origin;
            _stuckCheckAt = _time + StuckTime;
            return stuck;
        }

        private void Ignore(string id)
        {
            if (!string.IsNullOrEmpty(id)) _ignored[id] = _time + IgnoreTime;
        }

        private bool IsIgnored(string id) => id != null && _ignored.TryGetValue(id, out var until) && _time < until;

        // ── Paths ───────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The direction along an A* path to <paramref name="goal"/> (searched again on a new goal, a chased one moved
        /// by more than 2 cells, or after <see cref="RepathTime"/>); the last step goes straight to <paramref name="exact"/>.
        /// </summary>
        private bool Follow(Vector2 origin, GridPosition goal, Vector2 exact, bool chase, out Vector2 move)
        {
            move = Vector2.zero;
            var moved = chase ? Chebyshev(goal, _pathGoal) > 2 : goal != _pathGoal;
            if (moved || _time >= _repathAt)
            {
                _pathGoal = goal;
                _repathAt = _time + RepathTime;
                _pathIndex = 1;
                // Around hostile zones when there is a way; through them only when there is no other.
                if (!_pathfinder.TryFindPath(_player.Cell, goal, new SafeWalkability(this), _path) &&
                    !_pathfinder.TryFindPath(_player.Cell, goal, new LevelWalkability(_passability), _path))
                {
                    _path.Clear();
                    return false;
                }
            }

            while (_pathIndex < _path.Count && (Centre(_path[_pathIndex]) - origin).sqrMagnitude < ArriveDistance * ArriveDistance)
                _pathIndex++;

            var to = _pathIndex < _path.Count - 1 ? Centre(_path[_pathIndex]) : exact;
            var offset = to - origin;
            move = offset.sqrMagnitude > 0.01f ? offset.normalized : Vector2.zero;
            return true;
        }

        /// <summary>Walkable and without a hostile zone. Struct: no boxing in A*.</summary>
        private readonly struct SafeWalkability : IGridPassability
        {
            private readonly BalanceBot _bot;

            public SafeWalkability(BalanceBot bot) => _bot = bot;

            public bool IsPassable(GridPosition position) =>
                _bot._passability.IsWalkable(position) && !_bot.HazardAt(position);
        }

        private static Vector2 Centre(GridPosition cell) => new Vector2(cell.X, cell.Y);

        private static int Chebyshev(GridPosition a, GridPosition b) => Mathf.Max(Mathf.Abs(a.X - b.X), Mathf.Abs(a.Y - b.Y));

        private bool Walkable(Vector2 point, int floor) => _passability.IsWalkable(PlayerMovement.CellOf(point, floor));

        // ── Openness: steps to the nearest wall, once per floor ─────────────────────────────────────────────────────

        private int Openness(Vector2 point, int floor)
        {
            var cell = PlayerMovement.CellOf(point, floor);
            if ((uint)cell.X >= (uint)_width || (uint)cell.Y >= (uint)_height) return 0;
            return _openness[cell.Y * _width + cell.X];
        }

        private void EnsureOpenness(int floor)
        {
            if (_opennessFloor == floor && _openness != null) return;
            _opennessFloor = floor;
            _openness ??= new byte[_width * _height];
            _queue.Clear();
            for (var y = 0; y < _height; y++)
            for (var x = 0; x < _width; x++)
            {
                var index = y * _width + x;
                var walkable = _passability.IsWalkable(new GridPosition(x, y, floor));
                var edge = x == 0 || y == 0 || x == _width - 1 || y == _height - 1;
                _openness[index] = !walkable ? (byte)0 : edge ? (byte)1 : byte.MaxValue;
                if (_openness[index] != byte.MaxValue) _queue.Enqueue(index);
            }

            while (_queue.Count > 0)
            {
                var index = _queue.Dequeue();
                var next = _openness[index] + 1;
                if (next > OpenCap) continue;
                int x = index % _width, y = index / _width;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx >= (uint)_width || (uint)ny >= (uint)_height) continue;
                    var neighbour = ny * _width + nx;
                    if (_openness[neighbour] <= next) continue;
                    _openness[neighbour] = (byte)next;
                    _queue.Enqueue(neighbour);
                }
            }

            for (var i = 0; i < _openness.Length; i++)
                if (_openness[i] > OpenCap) _openness[i] = OpenCap;
        }
    }
}
