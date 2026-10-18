using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// What holds the player (stage 5c): a ring on the floor under them in the colour of the state that lasts longest
    /// (<see cref="PlayerStatusSystem"/>; <see cref="Purge.Core.Effects.StatusDefinition.Color"/>), and a pale pulsing
    /// ring while the immunity to control lasts (ТЗ: 2–3 с иммунитета). Material
    /// <see cref="CombatVisualDefinition.StatusRing"/> (Purge/Ring); without it nothing is drawn.
    /// <para>Stage Э3: the pack look of every state on the player (<c>StatusDefinition.Particles</c>, emitted —
    /// <see cref="PackEmitters"/>): frost of a freeze, flames of burning; a stun's stars circle the head.</para>
    /// </summary>
    public sealed class PlayerStatusView : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        private const float Radius = 0.55f;
        private const float ImmunityRadius = 0.7f;
        private const float ParticlesInterval = 0.12f;
        private const float BodyHeight = 0.6f;
        private const float HeadHeight = 1.75f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        private static readonly Color ImmunityColor = new Color(0.85f, 0.95f, 1f, 0.8f);

        private readonly LevelData _level;
        private readonly PlayerSystem _player;
        private readonly PlayerStatusSystem _statuses;
        private readonly CombatVisualDefinition _visual;
        private readonly PackEmitters _emitters;
        private float _particlesTimer;
        private Mesh _quad;
        private MaterialPropertyBlock _block;
        private MaterialPropertyBlock _immunityBlock; // a block per ring drawn in the same frame
        private Bounds _bounds;
        private float _time;

        public PlayerStatusView(LevelData level, PlayerSystem player, PlayerStatusSystem statuses, CombatVisualDefinition visual = null,
            PackEmitters emitters = null)
        {
            _emitters = emitters;
            _level = level;
            _player = player;
            _statuses = statuses;
            _visual = visual;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (_visual == null || _visual.StatusRing == null) return UniTask.CompletedTask;
            var geometry = _level.Geometry;
            _bounds = new Bounds(new Vector3(geometry.Width * 0.5f, 1f, geometry.Height * 0.5f),
                new Vector3(geometry.Width + 20f, 10f, geometry.Height + 20f));
            _quad = new Mesh { name = "Player Status Ring", hideFlags = HideFlags.DontSave };
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f),
            };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateBounds();
            _block = new MaterialPropertyBlock();
            _immunityBlock = new MaterialPropertyBlock();
            return UniTask.CompletedTask;
        }

        public void LateTick(float deltaTime)
        {
            if (_quad == null || !_player.IsSpawned) return;
            _time += deltaTime;
            var at = new Vector3(_player.Position.x, _player.Floor * GridPosition.FloorHeight + 0.03f, _player.Position.y);

            var longest = -1;
            var left = 0f;
            var kinds = _statuses.Kinds;
            for (var k = 0; k < kinds.Count; k++)
            {
                var l = _statuses.Left(k);
                if (l > left)
                {
                    left = l;
                    longest = k;
                }
            }

            if (longest >= 0)
            {
                var color = kinds[longest].Color;
                color.a = 0.75f * Mathf.Clamp01(left * 3f); // fades out in its last third of a second
                Draw(_block, at, Radius, color, 0.18f);
            }

            if (_statuses.IsImmune)
            {
                var color = ImmunityColor;
                color.a *= (0.55f + 0.45f * Mathf.Sin(_time * 12f)) * Mathf.Clamp01(_statuses.ImmunityLeft * 2f);
                Draw(_immunityBlock, at, ImmunityRadius, color, 0.1f);
            }

            _particlesTimer -= deltaTime;
            if (_particlesTimer <= 0f && longest >= 0 && _emitters != null)
            {
                _particlesTimer = ParticlesInterval;
                EmitParticles(at);
            }
        }

        /// <summary>The pack look of every state on the player: stars circling the head for a stun, else around the body.</summary>
        private void EmitParticles(Vector3 floor)
        {
            var kinds = _statuses.Kinds;
            for (var k = 0; k < kinds.Count; k++)
            {
                if (_statuses.Left(k) <= 0f) continue;
                var look = _emitters.Get(kinds[k].Particles);
                if (look == null) continue;
                if (!kinds[k].ParticlesAtHead)
                {
                    look.Emit(floor + Vector3.up * BodyHeight, 1f);
                    continue;
                }

                var angle = _time * 5f;
                for (var n = 0; n < 3; n++, angle += Mathf.PI * 2f / 3f)
                    look.EmitPoint(floor + new Vector3(Mathf.Cos(angle) * 0.3f, HeadHeight, Mathf.Sin(angle) * 0.3f), ParticlesInterval * 1.6f);
            }
        }

        public void Dispose()
        {
            UnityObjects.Destroy(_quad);
            _quad = null;
        }

        private void Draw(MaterialPropertyBlock block, Vector3 at, float radius, Color color, float thickness)
        {
            block.SetColor(ColorId, color);
            block.SetFloat(ThicknessId, thickness);
            var renderParams = new RenderParams(_visual.StatusRing)
            {
                worldBounds = _bounds,
                matProps = block,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };
            Graphics.RenderMesh(renderParams, _quad, 0, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(radius * 2f, 1f, radius * 2f)));
        }
    }
}
