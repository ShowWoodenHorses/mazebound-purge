using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Monsters;
using Purge.Core.Visual;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Warnings of the monsters' abilities (ТЗ: каждая опасная способность сначала показывает зону/линию, 0,5–1 с): for
    /// every cast in <see cref="AbilityPhase.Warning"/> (<see cref="MonsterAbilities"/>) a flat quad on the floor with
    /// <see cref="CombatVisualDefinition.AbilityWarning"/> (shader Purge/Warning) — lines along the shot (one per
    /// projectile of a fan), a circle where an arc lands, a sector in front of a strike, a body-wide line as far as a
    /// charge rushes, a circle around a monster that shoves (stage 5d), a circle under the player for an area strike and
    /// small circles on the fallen a raise lifts (stage 5e; the rage shows nothing — the monster roars), the line of a hook,
    /// small circles where the summoned climb out and the cone of a gaze (stage 8b), a circle where an
    /// explosive champion died (<see cref="ChampionSystem"/>, stage 5e2); the fill grows until it goes off. Drawn whether or not the monster is in view: a shot out of the fog must be seen.
    /// Allocation-free per frame.
    /// </summary>
    public sealed class AbilityWarningView : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        private const float LineWidth = 0.45f;
        private const float Height = 0.04f;
        private const float RaiseMark = 0.45f;

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int ShapeId = Shader.PropertyToID("_Shape");
        private static readonly int HalfAngleId = Shader.PropertyToID("_HalfAngle");
        private static readonly int EdgeId = Shader.PropertyToID("_Edge");

        private readonly LevelData _level;
        private readonly HordeSystem _horde;
        private readonly MonsterAbilities _abilities;
        private readonly PlayerSystem _player;
        private readonly CombatVisualDefinition _visual;
        private readonly ChampionSystem _champions;
        private Mesh _quad;
        private MaterialPropertyBlock _block;
        private Bounds _bounds;

        public AbilityWarningView(LevelData level, HordeSystem horde, MonsterAbilities abilities, PlayerSystem player,
            CombatVisualDefinition visual = null, ChampionSystem champions = null)
        {
            _champions = champions;
            _level = level;
            _horde = horde;
            _abilities = abilities;
            _player = player;
            _visual = visual;
        }

        // After MonsterAbilities (stage SpawnZombies, registered before the views): it knows its casters then.
        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_visual == null || _visual.AbilityWarning == null || !_abilities.IsActive && (_champions == null || !_champions.IsActive))
                return UniTask.CompletedTask;
            var geometry = _level.Geometry;
            _bounds = new Bounds(new Vector3(geometry.Width * 0.5f, 1f, geometry.Height * 0.5f),
                new Vector3(geometry.Width + 20f, 10f, geometry.Height + 20f));
            _quad = new Mesh { name = "Ability Warning", hideFlags = HideFlags.DontSave };
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f),
            };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateBounds();
            _block = new MaterialPropertyBlock();
            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            if (_quad == null) return;
            DrawBlasts();
            if (!_abilities.IsActive) return;
            var phases = _abilities.Phases;
            var starts = _abilities.PhaseStart;
            var origins = _abilities.Origins;
            var aims = _abilities.Aims;
            var time = _horde.Time;
            var height = _player.Floor * GridPosition.FloorHeight + Height;
            for (var i = 0; i < _horde.HighWater; i++)
            {
                if (phases[i] != AbilityPhase.Warning) continue;
                var ability = _abilities.CastOf(i);
                if (ability == null) continue;
                var progress = Mathf.Clamp01((time - starts[i]) / Mathf.Max(0.05f, ability.Warning));
                var origin = origins[i];
                switch (ability)
                {
                    case ProjectileAbility projectile when projectile.Form == ProjectileForm.Arc:
                        var radius = projectile.LandingRadius;
                        Draw(0f, progress, 0f, 0.07f / radius,
                            Matrix4x4.TRS(new Vector3(aims[i].x, height, aims[i].y), Quaternion.identity, new Vector3(radius * 2f, 1f, radius * 2f)));
                        break;
                    case ProjectileAbility projectile:
                        var count = projectile.Count;
                        for (var n = 0; n < count; n++)
                        {
                            var angle = count > 1 ? Mathf.Lerp(-0.5f, 0.5f, n / (count - 1f)) * projectile.FanAngle : 0f;
                            var direction = Quaternion.Euler(0f, angle, 0f) * new Vector3(aims[i].x, 0f, aims[i].y);
                            var length = Mathf.Max(0.3f, _abilities.ShotReach(origin, new Vector2(direction.x, direction.z), projectile.Range));
                            var center = new Vector3(origin.x, height, origin.y) + direction * (length * 0.5f);
                            Draw(1f, progress, 0f, 0.18f, Matrix4x4.TRS(center, Quaternion.LookRotation(direction),
                                new Vector3(LineWidth, 1f, length)));
                        }

                        break;
                    case AreaStrikeAbility area:
                        var areaRadius = area.WarningRadius;
                        Draw(0f, progress, 0f, 0.07f / areaRadius,
                            Matrix4x4.TRS(new Vector3(aims[i].x, height, aims[i].y), Quaternion.identity,
                                new Vector3(areaRadius * 2f, 1f, areaRadius * 2f)));
                        break;
                    case RaiseAbility _:
                        // A small circle on each of the fallen that will get up.
                        for (var n = _abilities.RaiseCount(i) - 1; n >= 0; n--)
                        {
                            var spot = _abilities.RaisePoint(i, n);
                            Draw(0f, progress, 0f, 0.07f / RaiseMark, Matrix4x4.TRS(new Vector3(spot.x, height, spot.y),
                                Quaternion.identity, new Vector3(RaiseMark * 2f, 1f, RaiseMark * 2f)));
                        }

                        break;
                    case PushAbility push:
                        Draw(0f, progress, 0f, 0.07f / push.Radius,
                            Matrix4x4.TRS(new Vector3(aims[i].x, height, aims[i].y), Quaternion.identity,
                                new Vector3(push.Radius * 2f, 1f, push.Radius * 2f)));
                        break;
                    case ChargeAbility _:
                        // As wide as the body, as long as it can rush.
                        var forwardLine = new Vector3(aims[i].x, 0f, aims[i].y);
                        var reach = Mathf.Max(0.3f, _abilities.ChargeLength(i));
                        var width = Mathf.Max(LineWidth, _horde.RadiusOf(i) * 2f);
                        Draw(1f, progress, 0f, 0.18f, Matrix4x4.TRS(new Vector3(origin.x, height, origin.y) + forwardLine * (reach * 0.5f),
                            Quaternion.LookRotation(forwardLine), new Vector3(width, 1f, reach)));
                        break;
                    case StrikeAbility strike:
                        var forward = new Vector3(aims[i].x, 0f, aims[i].y);
                        Draw(2f, progress, Mathf.Min(strike.Angle, 359.9f) * 0.5f * Mathf.Deg2Rad, 0.06f,
                            Matrix4x4.TRS(new Vector3(origin.x, height, origin.y), Quaternion.LookRotation(forward),
                                new Vector3(strike.Reach * 2f, 1f, strike.Reach * 2f)));
                        break;
                    case PullAbility _:
                        // The hook's line, as far as the head flies (stage 8b).
                        var hookLine = new Vector3(aims[i].x, 0f, aims[i].y);
                        var hookReach = Mathf.Max(0.3f, _abilities.ChargeLength(i));
                        Draw(1f, progress, 0f, 0.18f, Matrix4x4.TRS(new Vector3(origin.x, height, origin.y) + hookLine * (hookReach * 0.5f),
                            Quaternion.LookRotation(hookLine), new Vector3(LineWidth, 1f, hookReach)));
                        break;
                    case SummonAbility _:
                        // A small circle where each summoned climbs out (stage 8b).
                        for (var n = _abilities.SummonCount(i) - 1; n >= 0; n--)
                        {
                            var spot = _abilities.SummonPoint(i, n);
                            Draw(0f, progress, 0f, 0.07f / RaiseMark, Matrix4x4.TRS(new Vector3(spot.x, height, spot.y),
                                Quaternion.identity, new Vector3(RaiseMark * 2f, 1f, RaiseMark * 2f)));
                        }

                        break;
                    case GazeAbility gaze:
                        // The cone of the gaze fills (stage 8b).
                        var look = new Vector3(aims[i].x, 0f, aims[i].y);
                        Draw(2f, progress, gaze.Angle * 0.5f * Mathf.Deg2Rad, 0.03f,
                            Matrix4x4.TRS(new Vector3(origin.x, height, origin.y), Quaternion.LookRotation(look),
                                new Vector3(gaze.Reach * 2f, 1f, gaze.Reach * 2f)));
                        break;
                }
            }
        }

        /// <summary>Explosive champions' death blasts: a circle filling until it goes off.</summary>
        private void DrawBlasts()
        {
            if (_champions == null || _champions.BlastCount == 0) return;
            var height = _player.Floor * GridPosition.FloorHeight + Height;
            for (var b = 0; b < _champions.BlastCount; b++)
            {
                var at = _champions.BlastPosition(b);
                var radius = _champions.BlastRadius(b);
                Draw(0f, _champions.BlastProgress(b), 0f, 0.07f / radius,
                    Matrix4x4.TRS(new Vector3(at.x, height, at.y), Quaternion.identity, new Vector3(radius * 2f, 1f, radius * 2f)));
            }
        }

        public void Dispose()
        {
            UnityObjects.Destroy(_quad);
            _quad = null;
        }

        private void Draw(float shape, float progress, float halfAngle, float edge, Matrix4x4 matrix)
        {
            _block.SetFloat(ShapeId, shape);
            _block.SetFloat(ProgressId, progress);
            _block.SetFloat(HalfAngleId, halfAngle);
            _block.SetFloat(EdgeId, edge);
            var renderParams = new RenderParams(_visual.AbilityWarning)
            {
                worldBounds = _bounds,
                matProps = _block,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };
            Graphics.RenderMesh(renderParams, _quad, 0, matrix);
        }
    }
}
