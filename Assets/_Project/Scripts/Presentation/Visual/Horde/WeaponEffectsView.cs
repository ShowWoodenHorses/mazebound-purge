using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Effects;
using Purge.Gameplay.Horde;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using Purge.Gameplay.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace Purge.Presentation.Visual.Horde
{
    /// <summary>
    /// Draft looks of the player's attacks that have no effect prefabs yet (stage 4а; the feel comes with 4в): the flame
    /// of a stream, the bolts of chain lightning (one dynamic mesh of jagged quads), explosions and sparks where the horde
    /// is hit, the blasts of thrown items (<see cref="EffectSystem.Exploded"/>), flames on burning monsters and stars over
    /// stunned ones (<see cref="StatusSystem"/>, in the states' colours). Particles are fed by <c>Emit</c>, only in visible
    /// cells; nothing is created per effect. Materials — <see cref="CombatVisualDefinition.BoltMaterial"/> and
    /// <see cref="CombatVisualDefinition.EffectParticles"/> (Build Combat Effects; on every level since stage 4в), else
    /// those of the sandbox settings; off without either.
    /// <para>Pack looks (stage Э2) replace the drafts where set: explosions and look-only effects (a smoke grenade's
    /// cloud, <see cref="EffectSystem.LookShown"/>) through <see cref="PackEffectsPlayer"/>; the stream's flame is
    /// <see cref="CombatViewPresenter"/>'s then; bolts from <see cref="CombatVisualDefinition.ChainBolt"/> (one textured
    /// quad per jump) with a flash on every target; sparks of hits and flames of states (<c>StatusDefinition.Particles</c>)
    /// emitted from pack effects (<see cref="PackEmitters"/>).</para>
    /// </summary>
    public sealed class WeaponEffectsView : ILevelLoadStep, ILevelTickable, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private const int MaxBoltQuads = 256;
        private const int SegmentsPerJump = 5;
        private const float BoltTime = 0.16f; // stage Э3: 0.12 s was hard to see
        private const float BoltWidth = 0.09f;
        private const float GunHeight = 0.75f;
        private const float StatusInterval = 0.12f;
        private const float StatusHeight = 0.5f;
        private const int StunStars = 3;
        private const float StunRing = 0.28f;
        private const float StunTurn = 5f;

        private readonly LevelData _level;
        private readonly PlayerCombat _combat;
        private readonly UpgradeCombat _upgrades;
        private readonly PlayerSystem _player;
        private readonly BulletSystem _bullets;
        private readonly CombatTargets _targets;
        private readonly VisibilitySystem _visibility;
        private readonly LevelViewRoot _root;
        private readonly PlayerWeaponPresenter _weaponView;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly Vector2[] _bolt = new Vector2[PlayerCombat.MaxChainPoints];
        private readonly EffectSystem _effects;
        private readonly StatusSystem _statuses;
        private readonly HordeSystem _horde;
        private readonly CombatVisualDefinition _visual;
        private readonly PackEffectsPlayer _packEffects;
        private readonly PackEmitters _emitters;
        private PackEmitters.Emitter _hitSparks;
        private bool _texturedBolt;
        private float _statusTimer;

        private Material _boltMaterial;
        private Mesh _boltMesh;
        private Vector3[] _boltVertices;
        private int[] _boltIndices;
        private ParticleSystem _sparks;
        private ParticleSystem _flame;
        private ParticleSystem _blast;
        private Bounds _bounds;
        private bool _bound;
        private int _boltCount;
        private float _boltLeft;
        private Mesh _groundQuad;
        private Mesh _lightningMesh;
        private MaterialPropertyBlock _landingLook;
        private MaterialPropertyBlock _shadowLook;
        private MaterialPropertyBlock _energyLook;
        private Material _energyCore;
        private Vector3 _railOrigin;
        private Vector2 _railEnd;
        private int _railFloor;
        private float _railLeft;
        private bool _railPending;
        private bool _chargedBeam;
        private bool _chainMuzzlePending;
        private float _boltHeight = GunHeight;
        private int _boltFloor;
        private Vector3 _plasmaEnd;
        private int _plasmaFloor;
        private float _plasmaImpactLeft;
        private float _flashLeft;
        private float _shockLeft;
        private float _shockRange;
        private float _shockAngle;
        private Vector2 _energyAim;
        private Color _flashColor;
        private static readonly Color LaserColor = new Color(0.25f, 1.05f, 1.65f, 0.9f);
        private static readonly Color RailColor = new Color(0.46f, 0.72f, 1.85f, 0.9f);
        private static readonly Color PlasmaColor = new Color(0.42f, 0.73f, 1.65f, 0.95f);
        private static readonly Color ShockColor = new Color(0.75f, 0.48f, 1.4f, 0.95f);
        private static readonly Color FrostColor = new Color(0.6f, 0.95f, 1.5f, 0.85f);

        private readonly SmokeScreen _smoke;
        private readonly Purge.Core.Content.ContentCatalog _content;
        private List<SmokeCloud> _resumeSmoke;
        private SmokeEffect _smokeLook;

        public WeaponEffectsView(LevelData level, PlayerCombat combat, PlayerSystem player, BulletSystem bullets,
            CombatTargets targets, VisibilitySystem visibility, LevelViewRoot root, EffectSystem effects = null,
            StatusSystem statuses = null, HordeSystem horde = null, CombatVisualDefinition visual = null,
            PackEffectsPlayer packEffects = null, PackEmitters emitters = null, UpgradeCombat upgrades = null,
            PlayerWeaponPresenter weaponView = null, SmokeScreen smoke = null,
            Purge.Core.Content.ContentCatalog content = null)
        {
            _emitters = emitters; _upgrades=upgrades;
            _visual = visual;
            _packEffects = packEffects;
            _effects = effects;
            _statuses = statuses;
            _horde = horde;
            _level = level;
            _combat = combat;
            _player = player;
            _bullets = bullets;
            _targets = targets;
            _visibility = visibility;
            _root = root;
            _weaponView = weaponView;
            _smoke = smoke;
            _content = content;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnZombies;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var settings = _level.StressTest;
            var sandbox = settings != null && settings.IsSandbox ? settings : null;
            // A pack's bolt texture (stage Э2) over the draft jagged bolt.
            _texturedBolt = _visual != null && _visual.ChainBolt != null;
            var boltMaterial = _texturedBolt ? _visual.ChainBolt
                : _visual != null && _visual.BoltMaterial != null ? _visual.BoltMaterial : sandbox?.TracerMaterial;
            var particles = _visual != null && _visual.EffectParticles != null ? _visual.EffectParticles : sandbox?.ParticleMaterial;
            if (boltMaterial == null && particles == null)
                return UniTask.CompletedTask;

            var width = _level.Geometry.Width;
            var height = _level.Geometry.Height;
            _bounds = new Bounds(new Vector3(width * 0.5f, 1f, height * 0.5f), new Vector3(width + 4f, 4f, height + 4f));

            _boltMaterial = boltMaterial;
            _groundQuad = new Mesh { name = "Weapon Markers", hideFlags = HideFlags.DontSave };
            _groundQuad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f),
            };
            _groundQuad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            _groundQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _groundQuad.RecalculateBounds();
            _landingLook = new MaterialPropertyBlock();
            _landingLook.SetColor("_Color", new Color(1f, 0.62f, 0.25f, 0.9f));
            _landingLook.SetFloat("_Thickness", 0.085f);
            _shadowLook = new MaterialPropertyBlock();
            _shadowLook.SetColor("_Color", new Color(0f, 0f, 0f, 0.5f));
            _shadowLook.SetFloat("_Thickness", 0.45f);
            _energyLook = new MaterialPropertyBlock();
            if (_visual?.BoltMaterial != null)
            {
                _energyCore = new Material(_visual.BoltMaterial) { hideFlags = HideFlags.DontSave };
                _energyCore.SetTexture("_MainTex", Texture2D.whiteTexture);
            }
            if (_boltMaterial != null)
            {
                _boltVertices = new Vector3[MaxBoltQuads * 4];
                _boltIndices = new int[MaxBoltQuads * 6];
                for (var q = 0; q < MaxBoltQuads; q++)
                {
                    int v = q * 4, i = q * 6;
                    _boltIndices[i] = v;
                    _boltIndices[i + 1] = v + 1;
                    _boltIndices[i + 2] = v + 2;
                    _boltIndices[i + 3] = v;
                    _boltIndices[i + 4] = v + 2;
                    _boltIndices[i + 5] = v + 3;
                }

                var uvs = new Vector2[_boltVertices.Length];
                for (var q = 0; q < MaxBoltQuads; q++)
                {
                    uvs[q * 4] = new Vector2(0f, 0f);
                    uvs[q * 4 + 1] = new Vector2(0f, 1f);
                    uvs[q * 4 + 2] = new Vector2(1f, 1f);
                    uvs[q * 4 + 3] = new Vector2(1f, 0f);
                }

                _boltMesh = new Mesh { name = "Chain Bolts", hideFlags = HideFlags.DontSave };
                _boltMesh.MarkDynamic();
                _boltMesh.vertices = _boltVertices;
                _boltMesh.uv = uvs;
                _boltMesh.SetIndices(_boltIndices, MeshTopology.Triangles, 0, false);
                _boltMesh.bounds = _bounds;
                _lightningMesh = new Mesh { name = "Beam Lightning", hideFlags = HideFlags.DontSave };
                _lightningMesh.MarkDynamic();
                _lightningMesh.vertices = _boltVertices;
                _lightningMesh.bounds = _bounds;
            }

            if (particles != null)
            {
                _sparks = CreateParticles("Hit Sparks", particles, 600, new Color(1f, 0.85f, 0.4f), 0.25f, 0.12f);
                _flame = CreateParticles("Stream Flame", particles, 500, new Color(1f, 0.55f, 0.15f), 0.4f, 0.5f);
                _blast = CreateParticles("Explosions", particles, 600, new Color(1f, 0.7f, 0.25f), 0.5f, 0.6f);
            }

            _hitSparks = _emitters?.Get(_visual?.HitSparks);
            _combat.Attacked += OnAttacked;
            _bullets.Exploded += OnExploded;
            _bullets.Ended += OnBulletEnded;
            if(_upgrades!=null)_upgrades.Ricochet+=OnRicochet;
            if (_effects != null)
            {
                _effects.Exploded += OnExploded;
                _effects.LookShown += OnLookShown;
                _effects.SmokeShown += OnSmokeShown;
            }
            _targets.Damaged += OnDamaged;
            if (_smoke != null && _smoke.Clouds.Count > 0 && _content?.Find<ThrowableDefinition>("grenade_smoke") is { } grenade)
            {
                foreach (var effect in grenade.Effects)
                    if (effect is SmokeEffect look)
                    {
                        _smokeLook = look;
                        _resumeSmoke = new List<SmokeCloud>(_smoke.Clouds);
                        break;
                    }
            }
            _bound = true;
            return UniTask.CompletedTask;
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_objects);

        public void Tick(float deltaTime)
        {
            // The level warmup stops every particle system once it has drawn them.
            if (_sparks != null && !_sparks.isPlaying)
            {
                _sparks.Play();
                _flame.Play();
                _blast.Play();
            }

            RestoreSmokeLooks();
            if (_boltLeft > 0f) _boltLeft -= deltaTime;
            if (_railLeft > 0f) _railLeft -= deltaTime;
            if (_flashLeft > 0f) _flashLeft -= deltaTime;
            if (_shockLeft > 0f) _shockLeft -= deltaTime;
            if (_plasmaImpactLeft > 0f) _plasmaImpactLeft -= deltaTime;
            _statusTimer -= deltaTime;
            if (_statusTimer <= 0f)
            {
                _statusTimer += StatusInterval;
                EmitStatuses();
            }
        }

        private void RestoreSmokeLooks()
        {
            if (_resumeSmoke == null) return;
            foreach (var cloud in _resumeSmoke)
                ShowSmoke(cloud.Position, cloud.Floor, cloud.Radius, cloud.Seconds, _smokeLook);
            _resumeSmoke = null;
        }

        /// <summary>A flame over every visible monster with damage over time, a spark circling a stunned one.</summary>
        private void EmitStatuses()
        {
            if (_statuses == null || _horde == null || !_statuses.IsActive || _sparks == null) return;
            var kinds = _statuses.Kinds;
            var positions = _horde.Positions;
            var states = _horde.States;
            var emit = new ParticleSystem.EmitParams();
            for (var agent = 0; agent < _horde.HighWater; agent++)
            {
                if (!HordeSystem.IsAlive(states[agent])) continue;
                for (var kind = 0; kind < kinds.Count; kind++)
                {
                    if (_statuses.Left(agent, kind) <= 0f) continue;
                    var at = positions[agent];
                    if (!Visible(at)) break;
                    var status = kinds[kind];
                    var look = _emitters?.Get(status.Particles);
                    if (look != null && status.ParticlesAtHead)
                    {
                        // Stars circling the head (stage Э3): short-lived points along a turning ring.
                        var angle = Time.time * StunTurn + agent * 2.4f;
                        var head = StunHeightOf(agent);
                        for (var n = 0; n < StunStars; n++, angle += Mathf.PI * 2f / StunStars)
                            look.EmitPoint(new Vector3(at.x + Mathf.Cos(angle) * StunRing, head, at.y + Mathf.Sin(angle) * StunRing),
                                StatusInterval * 1.6f);
                        continue;
                    }

                    if (look != null)
                    {
                        look.Emit(new Vector3(at.x, StatusHeight, at.y), 1f);
                        continue;
                    }

                    emit.startColor = status.Color;
                    if (status.Stuns)
                    {
                        var angle = Time.time * 8f + agent;
                        emit.position = new Vector3(at.x + Mathf.Cos(angle) * 0.3f, 1.5f, at.y + Mathf.Sin(angle) * 0.3f);
                        emit.velocity = Vector3.zero;
                        emit.startLifetime = 0.2f;
                        emit.startSize = 0.14f;
                        _sparks.Emit(emit, 1);
                    }
                    else if (_flame != null && status.DamagePerSecond > 0f)
                    {
                        emit.position = new Vector3(at.x + UnityEngine.Random.Range(-0.2f, 0.2f), 0.6f,
                            at.y + UnityEngine.Random.Range(-0.2f, 0.2f));
                        emit.velocity = Vector3.up * 1.6f;
                        emit.startLifetime = 0.35f;
                        emit.startSize = 0.35f;
                        _flame.Emit(emit, 1);
                    }
                }
            }
        }

        /// <summary>Height of a stunned monster's stars: taller ranks and champions higher.</summary>
        private float StunHeightOf(int agent)
        {
            var height = _visual != null ? _visual.StunHeight : 1.45f;
            var definition = _horde.DefinitionOf(agent);
            if (definition != null && definition.Rank >= Core.Monsters.MonsterRank.Elite) height *= 1.12f;
            if (_horde.IsChampion(agent) && _horde.Champions != null) height *= _horde.Champions.Scale;
            return height;
        }

        public void LateTick(float deltaTime)
        {
            DrawWeaponMarkers();
            if (_boltMesh == null || _boltLeft <= 0f || _boltCount < 2 || _boltFloor != _player.Floor) return;

            if (_energyCore != null)
            {
                for (var i = 1; i < _boltCount; i++)
                    DrawEnergyLine(AtHeight(_bolt[i - 1], _boltHeight), AtHeight(_bolt[i], _boltHeight), 0.035f, RailColor, false);
                DrawEnergyImpact(AtHeight(_bolt[_boltCount - 1], _boltHeight), RailColor);
            }

            var quads = 0;
            for (var p = 1; p < _boltCount && _texturedBolt && quads < MaxBoltQuads; p++)
                AddTexturedQuad(quads++, _bolt[p - 1], _bolt[p]);
            for (var p = 1; p < _boltCount && !_texturedBolt; p++)
            {
                var from = _bolt[p - 1];
                var to = _bolt[p];
                var previous = from;
                for (var s = 1; s <= SegmentsPerJump && quads < MaxBoltQuads; s++)
                {
                    var t = (float)s / SegmentsPerJump;
                    var along = Vector2.Lerp(from, to, t);
                    var direction = (to - from).normalized;
                    // Jagged: every inner point jumps aside, new shape each frame.
                    var next = s < SegmentsPerJump
                        ? along + new Vector2(-direction.y, direction.x) * UnityEngine.Random.Range(-0.18f, 0.18f)
                        : to;
                    AddQuad(quads++, previous, next);
                    previous = next;
                }
            }

            _boltMesh.SetVertices(_boltVertices, 0, _boltVertices.Length,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _boltMesh.SetIndices(_boltIndices, 0, quads * 6, MeshTopology.Triangles, 0, false);
            Graphics.RenderMesh(new RenderParams(_boltMaterial)
            {
                worldBounds = _bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            }, _boltMesh, 0, Matrix4x4.identity);
        }

        private void DrawWeaponMarkers()
        {
            if (_groundQuad == null || _visual == null) return;
            var muzzle = MuzzlePosition();
            if (_railPending)
            {
                _railOrigin = muzzle;
                _railPending = false;
            }
            if (_railLeft > 0f && _player.Floor == _railFloor)
            {
                var end = AtHeight(_railEnd, _railOrigin.y);
                var color = _chargedBeam ? RailColor : LaserColor;
                DrawLightning(_railOrigin, end, _chargedBeam, color);
                DrawEnergyImpact(end, color);
            }
            if (_chainMuzzlePending && _boltCount > 0)
            {
                _bolt[0] = new Vector2(muzzle.x, muzzle.z);
                _boltHeight = muzzle.y;
                _chainMuzzlePending = false;
            }
            if (_flashLeft > 0f) DrawEnergyFlash(muzzle);
            if (_shockLeft > 0f) DrawShockCone(muzzle);
            if (_plasmaImpactLeft > 0f && _plasmaFloor == _player.Floor && Visible(new Vector2(_plasmaEnd.x, _plasmaEnd.z), _plasmaFloor))
                DrawEnergyImpact(_plasmaEnd, PlasmaColor);
            if (_visual.StatusRing == null) return;
            foreach (var bullet in _bullets.Active)
            {
                if (!bullet.IsLobbed || bullet.Floor != _player.Floor) continue;
                var height = bullet.Floor * GridPosition.FloorHeight + 0.04f;
                if (Visible(bullet.LandingPoint, bullet.Floor))
                    DrawMarker(_visual.StatusRing, bullet.LandingPoint, height, bullet.Explosive.Radius * 2f, _landingLook);
                if (Visible(bullet.Position, bullet.Floor))
                    DrawMarker(_visual.StatusRing, bullet.Position, height, 0.45f, _shadowLook);
            }
        }

        private Vector3 MuzzlePosition()
        {
            var model = _weaponView?.ShownModel;
            if (model?.Muzzle != null && _root != null)
                return _root.transform.InverseTransformPoint(model.MuzzlePosition);
            return AtHeight(_player.Position + _combat.AimDirection * 0.5f,
                _player.Floor * GridPosition.FloorHeight + GunHeight);
        }

        private static Vector3 AtHeight(Vector2 point, float height) => new Vector3(point.x, height, point.y);

        private void DrawEnergyLine(Vector3 from, Vector3 to, float width, Color color, bool jagged)
        {
            var material = jagged ? _visual.ChainBolt : _energyCore;
            var delta = to - from;
            var length = delta.magnitude;
            if (material == null || length < 0.01f) return;
            _energyLook.SetColor("_Color", color);
            Graphics.RenderMesh(new RenderParams(material) { matProps = _energyLook, shadowCastingMode = ShadowCastingMode.Off },
                _groundQuad, 0, Matrix4x4.TRS((from + to) * 0.5f, Quaternion.LookRotation(delta), new Vector3(width, 1f, length)));
        }

        private void DrawEnergyImpact(Vector3 point, Color color)
        {
            DrawEnergyLine(point - Vector3.right * 0.09f, point + Vector3.right * 0.09f, 0.055f, color, false);
            DrawEnergyLine(point - Vector3.forward * 0.09f, point + Vector3.forward * 0.09f, 0.055f, color, false);
        }

        private void DrawLightning(Vector3 origin, Vector3 end, bool charged, Color color)
        {
            if (_lightningMesh == null || _energyCore == null) return;
            var offset = end - origin;
            if (offset.sqrMagnitude < 0.0001f) return;
            var side = Vector3.Cross(Vector3.up, offset).normalized;
            var strands = charged ? 5 : 2;
            var segments = Mathf.Clamp(Mathf.CeilToInt(offset.magnitude * 2f), 3, 22);
            var flicker = 1f - Mathf.Clamp01(_railLeft / BoltTime);
            var quads = 0;
            for (var strand = 0; strand < strands; strand++)
            {
                var previous = origin;
                for (var segment = 1; segment <= segments; segment++)
                {
                    var fraction = (float)segment / segments;
                    var displacement = Mathf.Sin(fraction * Mathf.PI) *
                        (Mathf.Sin(segment * 2.2f + strand * 1.8f + flicker * 7f) * (charged ? 0.23f : 0.055f)
                        + (strand - (strands - 1) * 0.5f) * (charged ? 0.12f : 0.025f));
                    var point = Vector3.Lerp(origin, end, fraction) + side * displacement;
                    var halfWidth = side * (charged ? 0.0135f : 0.016f);
                    var vertex = 4 * quads++;
                    _boltVertices[vertex] = previous - halfWidth;
                    _boltVertices[vertex + 1] = point - halfWidth;
                    _boltVertices[vertex + 2] = point + halfWidth;
                    _boltVertices[vertex + 3] = previous + halfWidth;
                    previous = point;
                }
            }
            _lightningMesh.SetVertices(_boltVertices, 0, _boltVertices.Length, MeshUpdateFlags.DontRecalculateBounds);
            _lightningMesh.SetIndices(_boltIndices, 0, quads * 6, MeshTopology.Triangles, 0, false);
            _energyLook.SetColor("_Color", color);
            Graphics.RenderMesh(new RenderParams(_energyCore)
            {
                matProps = _energyLook,
                shadowCastingMode = ShadowCastingMode.Off,
                worldBounds = _bounds,
            }, _lightningMesh, 0, Matrix4x4.identity);
        }


        private void DrawEnergyFlash(Vector3 muzzle)
        {
            var forward = AtHeight(_energyAim, 0f);
            var spread = 1f - Mathf.Clamp01(_flashLeft / 0.12f);
            var color = _flashColor;
            color.a *= 1f - spread;
            for (var i = 0; i < 5; i++)
            {
                var ray = Quaternion.Euler(0f, (i - 2) * 21f, 0f) * forward;
                var reach = (0.38f + 0.34f * spread) * (i % 2 == 0 ? 0.75f : 1f);
                DrawEnergyLine(muzzle, muzzle + ray * reach, 0.2f, color, true);
            }
        }

        private void DrawShockCone(Vector3 muzzle)
        {
            var origin = _player.Position;
            for (var i = 0; i < 5; i++)
            {
                var angle = _shockAngle * (i * 0.25f - 0.5f);
                var ray = Quaternion.Euler(0f, angle, 0f) * AtHeight(_energyAim, 0f);
                var end = _targets.Spatial.QueryBlocker(origin,
                    origin + new Vector2(ray.x, ray.z) * _shockRange, _player.Floor).Point;
                var hit = AtHeight(end, muzzle.y);
                DrawEnergyLine(muzzle, hit, 0.035f, ShockColor, false);
                var flicker = 0.05f * Mathf.Sin(i + _shockLeft * 34f);
                DrawEnergyLine(muzzle, hit, (i == 2 ? 0.54f : 0.35f) + flicker, ShockColor, true);
                DrawEnergyImpact(hit, ShockColor);
            }
        }

        private void DrawMarker(Material material, Vector2 point, float height, float size, MaterialPropertyBlock appearance)
        {
            Graphics.RenderMesh(new RenderParams(material) { matProps = appearance, shadowCastingMode = ShadowCastingMode.Off },
                _groundQuad, 0, Matrix4x4.TRS(new Vector3(point.x, height, point.y), Quaternion.identity,
                    new Vector3(size, 1f, size)));
        }

        public void Dispose()
        {
            if (_bound)
            {
                _combat.Attacked -= OnAttacked;
                _bullets.Exploded -= OnExploded;
                _bullets.Ended -= OnBulletEnded;
                if(_upgrades!=null)_upgrades.Ricochet-=OnRicochet;
                if (_effects != null)
                {
                    _effects.Exploded -= OnExploded;
                    _effects.LookShown -= OnLookShown;
                    _effects.SmokeShown -= OnSmokeShown;
                }
                _targets.Damaged -= OnDamaged;
                _bound = false;
            }

            UnityObjects.Destroy(_boltMesh);
            UnityObjects.Destroy(_groundQuad);
            UnityObjects.Destroy(_lightningMesh);
            UnityObjects.Destroy(_energyCore);
            foreach (var go in _objects)
                UnityObjects.Destroy(go);
            _objects.Clear();
        }

        private void OnAttacked(WeaponRuntime weapon, Vector2 direction)
        {
            var effects = weapon.Definition.Effects;
            _energyAim = direction;
            if (effects?.ElectricProjectile == true)
            {
                _flashColor = PlasmaColor;
                _flashLeft = 0.12f;
            }
            switch (weapon.Definition.Attack)
            {
                case StreamAttack _ when effects?.FrostStream == true:
                    _flashColor = FrostColor;
                    _flashLeft = 0.12f;
                    break;
                case StreamAttack shock when effects?.ElectricCone == true:
                    _flashColor = ShockColor;
                    _flashLeft = 0.12f;
                    _shockRange = shock.Range;
                    _shockAngle = shock.Angle;
                    _shockLeft = 0.18f;
                    break;
                // The pack's stream is held by CombatViewPresenter; the draft flame only without it.
                case StreamAttack stream when !stream.SuppressFallbackFlame && _flame != null && !HasPackStream(weapon.Definition):
                    EmitFlame(stream, direction);
                    break;
                case BeamAttack beam:
                    _railEnd = _combat.BeamEnd;
                    _railFloor = _player.Floor;
                    _railLeft = BoltTime;
                    _railPending = true;
                    _chargedBeam = beam.ChargeTime > 0f;
                    _flashColor = _chargedBeam ? RailColor : LaserColor;
                    _flashLeft = 0.12f;
                    break;
                case ChainAttack _:
                    var points = _combat.ChainPoints;
                    _boltCount = points.Length;
                    for (var i = 0; i < _boltCount; i++) _bolt[i] = points[i];
                    _chainMuzzlePending = true;
                    _boltFloor = _player.Floor;
                    _boltLeft = BoltTime;
                    _flashColor = RailColor;
                    _flashLeft = 0.12f;
                    PlayChainHits(weapon.Definition, points);
                    break;
            }
        }

        private void OnRicochet(Vector2 from,Vector2 to,WeaponDefinition weapon)
        {
            _chainMuzzlePending = false;
            _boltFloor = _player.Floor;
            _boltHeight = GunHeight + _boltFloor * GridPosition.FloorHeight;
            _boltCount=2;_bolt[0]=from;_bolt[1]=to;_boltLeft=BoltTime;
            PlayChainHits(weapon,new ReadOnlySpan<Vector2>(_bolt,0,2));
        }

        private void OnBulletEnded(Bullet bullet, Vector2 point, BulletEnd reason)
        {
            if (reason == BulletEnd.Cleared || bullet.Weapon?.Effects?.ElectricProjectile != true) return;
            _plasmaFloor = bullet.Floor;
            _plasmaEnd = AtHeight(point, GunHeight + bullet.Floor * GridPosition.FloorHeight);
            _plasmaImpactLeft = 0.16f;
        }
        private void EmitFlame(StreamAttack stream, Vector2 aim)
        {
            var player = _player.Position;
            var emit = new ParticleSystem.EmitParams();
            var count = Mathf.CeilToInt(stream.Interval * 70f);
            for (var i = 0; i < count; i++)
            {
                var spread = (UnityEngine.Random.value - 0.5f) * stream.Angle * Mathf.Deg2Rad;
                var dir = new Vector2(aim.x * Mathf.Cos(spread) - aim.y * Mathf.Sin(spread),
                    aim.x * Mathf.Sin(spread) + aim.y * Mathf.Cos(spread));
                emit.position = new Vector3(player.x + aim.x * 0.5f, GunHeight * 0.8f, player.y + aim.y * 0.5f);
                emit.velocity = new Vector3(dir.x, 0.15f, dir.y) * (stream.Range / 0.4f);
                emit.startLifetime = 0.4f;
                emit.startSize = 0.35f;
                _flame.Emit(emit, 1);
            }
        }

        private void OnExploded(Vector2 at, int floor, float radius, IExplosionLook source)
        {
            if (!Visible(at, floor)) return;
            var height = floor * GridPosition.FloorHeight;
            // The pack effect of the source, else the default by size; the draft particles only without both.
            var look = source != null && CombatVisualDefinition.IsSet(source.Look) ? source.Look : _visual?.ExplosionOf(radius);
            if (look != null && _packEffects != null &&
                _packEffects.Play(look, new Vector3(at.x, height, at.y), Quaternion.identity, radius, floor))
                return;
            if (_blast == null) return;
            var emit = new ParticleSystem.EmitParams { position = new Vector3(at.x, height + 0.4f, at.y) };
            var count = Mathf.Clamp(Mathf.RoundToInt(radius * 25f), 20, 120);
            for (var i = 0; i < count; i++)
            {
                var flat = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(0.4f, 1f);
                emit.velocity = new Vector3(flat.x, UnityEngine.Random.Range(0.1f, 0.6f), flat.y) * (radius / 0.45f);
                emit.startLifetime = 0.45f;
                emit.startSize = UnityEngine.Random.Range(0.4f, 0.9f);
                _blast.Emit(emit, 1);
            }
        }

        private void OnDamaged(CombatTarget target, Vector2 at, float damage, Vector2 direction, bool killed)
        {
            if (target.Agent < 0 || !Visible(at)) return;
            if (_hitSparks != null)
            {
                // Zones and burning hit without a direction: their own looks show them, no sparks.
                if (direction == Vector2.zero) return;
                _hitSparks.Emit(new Vector3(at.x - direction.x * 0.25f, GunHeight, at.y - direction.y * 0.25f),
                    killed ? _visual.KillSparksCount : _visual.HitSparksCount);
                return;
            }

            if (_sparks == null) return;
            var emit = new ParticleSystem.EmitParams
            {
                position = new Vector3(at.x - direction.x * 0.25f, GunHeight, at.y - direction.y * 0.25f),
                startLifetime = 0.25f,
            };
            var count = killed ? 8 : 3;
            for (var i = 0; i < count; i++)
            {
                emit.velocity = UnityEngine.Random.insideUnitSphere * 3f + Vector3.up;
                _sparks.Emit(emit, 1);
            }
        }

        private bool HasPackStream(WeaponDefinition weapon) =>
            _visual != null && _packEffects != null && _packEffects.IsLoaded(_visual.StreamOf(weapon));

        /// <summary>A flash of the pack on every monster the bolt hit (the first point is the gun).</summary>
        private void PlayChainHits(WeaponDefinition weapon, ReadOnlySpan<Vector2> points)
        {
            var look = _visual?.ChainHitOf(weapon);
            if (look == null || _packEffects == null) return;
            for (var i = 1; i < points.Length; i++)
                _packEffects.Play(look, AtHeight(points[i], GunHeight + _boltFloor * GridPosition.FloorHeight),
                    Quaternion.identity, floor: _boltFloor);
        }

        private void OnSmokeShown(Vector2 at, int floor, float radius, SmokeEffect smoke) =>
            ShowSmoke(at, floor, radius, smoke.Duration, smoke);

        private void ShowSmoke(Vector2 at, int floor, float radius, float seconds, SmokeEffect smoke)
        {
            _packEffects?.Play(smoke.Look, new Vector3(at.x, floor * GridPosition.FloorHeight, at.y),
                Quaternion.identity, radius, floor, duration: seconds, showInFog: true);
        }

        private void OnLookShown(Vector2 at, int floor, float radius, IExplosionLook source)
        {
            if (_packEffects != null && source != null)
                _packEffects.Play(source.Look, new Vector3(at.x, floor * GridPosition.FloorHeight, at.y), Quaternion.identity, radius, floor);
        }

        /// <summary>One quad per jump, the texture's bolt along V, mirrored at random every frame (it flickers).</summary>
        private void AddTexturedQuad(int quad, Vector2 from, Vector2 to)
        {
            var direction = (to - from).normalized;
            var mirror = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            var side = new Vector2(-direction.y, direction.x) * (_visual.ChainBoltWidth * 0.5f * mirror);
            var v = quad * 4;
            // The quads' UVs are (0,0) (0,1) (1,1) (1,0): U runs across here, V along.
            _boltVertices[v] = new Vector3(from.x - side.x, _boltHeight, from.y - side.y);
            _boltVertices[v + 1] = new Vector3(to.x - side.x, _boltHeight, to.y - side.y);
            _boltVertices[v + 2] = new Vector3(to.x + side.x, _boltHeight, to.y + side.y);
            _boltVertices[v + 3] = new Vector3(from.x + side.x, _boltHeight, from.y + side.y);
        }

        private void AddQuad(int quad, Vector2 from, Vector2 to)
        {
            var direction = (to - from).normalized;
            var side = new Vector2(-direction.y, direction.x) * (BoltWidth * 0.5f);
            var v = quad * 4;
            _boltVertices[v] = new Vector3(from.x - side.x, _boltHeight, from.y - side.y);
            _boltVertices[v + 1] = new Vector3(from.x + side.x, _boltHeight, from.y + side.y);
            _boltVertices[v + 2] = new Vector3(to.x + side.x, _boltHeight, to.y + side.y);
            _boltVertices[v + 3] = new Vector3(to.x - side.x, _boltHeight, to.y - side.y);
        }

        private bool Visible(Vector2 point, int floor = 0) =>
            _visibility.IsVisible(PlayerMovement.CellOf(point, floor));

        private ParticleSystem CreateParticles(string name, Material material, int max, Color color, float lifetime, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root != null ? _root.transform : null, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startSpeed = 0f;
            main.startColor = color;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.3f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;
            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play();
            _objects.Add(go);
            return system;
        }
    }
}
