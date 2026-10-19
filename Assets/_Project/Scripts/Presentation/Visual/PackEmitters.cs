using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Content;
using Purge.Core.Effects;
using Purge.Core.Visual;
using Purge.Gameplay.Level;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Mass effects in the look of pack effects (stage Э2): fire on cells, flames on burning monsters, sparks of hits on
    /// the horde — too many at once for instances. One copy of each effect is an emitter: its particle systems emit
    /// nothing by themselves (world space, a larger particle cap) and the views feed them by <see cref="Emitter.Emit"/>
    /// where they need (only in visible cells — the caller checks). Each system gets its share of a call by how much it
    /// emitted in the pack (flames more than embers). Display only.
    /// <para>Loaded with the level: <see cref="CombatVisualDefinition.HitSparks"/>, the monsters' dust of climbing out
    /// and puff of sinking corpses (stage Э3) and the looks of every zone and state
    /// of the content catalog (<see cref="ZoneDefinition.Particles"/>, <see cref="StatusDefinition.Particles"/> —
    /// a handful). Nothing is created during play.</para>
    /// </summary>
    public sealed class PackEmitters : ILevelLoadStep, IViewWarmup, IDisposable
    {
        /// <summary>Particles one system of an emitter shows at once (all cells and monsters together).</summary>
        public const int Capacity = 600;

        private readonly CombatVisualDefinition _visual;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly ContentCatalog _content;
        private readonly Dictionary<string, Emitter> _emitters = new Dictionary<string, Emitter>();
        private readonly List<GameObject> _warmup = new List<GameObject>();
        private Transform _parent;

        public PackEmitters(CombatVisualDefinition visual, IAssetOwner assets, LevelViewRoot root, ContentCatalog content = null)
        {
            _visual = visual;
            _assets = assets;
            _root = root;
            _content = content;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var wanted = new List<AssetReferenceGameObject>();
            if (_visual != null)
            {
                Add(wanted, _visual.HitSparks);
                Add(wanted, _visual.Emerge);
                Add(wanted, _visual.CorpsePuff);
                Add(wanted, _visual.DropVanishOf());
            }
            if (_content != null)
                foreach (var item in _content.Items)
                    switch (item)
                    {
                        case ZoneDefinition zone: Add(wanted, zone.Particles); break;
                        case StatusDefinition status: Add(wanted, status.Particles); break;
                    }
            if (wanted.Count == 0) return;

            _parent = new GameObject("Pack Emitters").transform;
            _parent.SetParent(_root != null ? _root.transform : null, false);
            foreach (var reference in wanted)
            {
                if (_emitters.ContainsKey(reference.AssetGUID)) continue;
                var prefab = await _assets.LoadAsync<GameObject>(reference, cancellation);
                cancellation.ThrowIfCancellationRequested();
                var view = UnityEngine.Object.Instantiate(prefab, _parent);
                view.name = prefab.name;
                var emitter = new Emitter(view);
                _emitters[reference.AssetGUID] = emitter;
                _warmup.Add(view);
            }
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_warmup);

        /// <summary>The emitter of a pack effect, or null (empty reference, or not loaded with the level).</summary>
        public Emitter Get(AssetReferenceGameObject reference) =>
            CombatVisualDefinition.IsSet(reference) && _emitters.TryGetValue(reference.AssetGUID, out var emitter)
                ? emitter
                : null;

        public void Dispose()
        {
            _emitters.Clear();
            _warmup.Clear();
            if (_parent != null) UnityObjects.Destroy(_parent.gameObject);
            _parent = null;
        }

        private static void Add(List<AssetReferenceGameObject> wanted, AssetReferenceGameObject reference)
        {
            if (CombatVisualDefinition.IsSet(reference)) wanted.Add(reference);
        }

        /// <summary>One copy of a pack effect whose particle systems emit only when told.</summary>
        public sealed class Emitter
        {
            private readonly ParticleSystem[] _systems;
            private readonly float[] _shares;
            private readonly float[] _carry;

            internal Emitter(GameObject view)
            {
                _systems = view.GetComponentsInChildren<ParticleSystem>(true);
                _shares = new float[_systems.Length];
                _carry = new float[_systems.Length];
                var most = 0f;
                for (var i = 0; i < _systems.Length; i++)
                {
                    var system = _systems[i];
                    var main = system.main;
                    main.loop = false;
                    main.playOnAwake = false;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.maxParticles = Capacity;
                    var emission = system.emission;
                    _shares[i] = Emitted(system);
                    most = Mathf.Max(most, _shares[i]);
                    emission.enabled = false;
                }

                for (var i = 0; i < _shares.Length; i++)
                    _shares[i] = most > 0f ? _shares[i] / most : 1f;
            }

            /// <summary>
            /// About <paramref name="count"/> particles of the main system at a point (the effect's shape around it; the
            /// other systems — their share).
            /// </summary>
            public void Emit(Vector3 position, float count)
            {
                var emit = new ParticleSystem.EmitParams { position = position, applyShapeToPosition = true };
                for (var i = 0; i < _systems.Length; i++)
                {
                    _carry[i] += count * _shares[i];
                    var whole = (int)_carry[i];
                    if (whole <= 0) continue;
                    _carry[i] -= whole;
                    var system = _systems[i];
                    // The level warmup stops every particle system once it has drawn them.
                    if (!system.isPlaying) system.Play(false);
                    system.Emit(emit, whole);
                }
            }

            /// <summary>
            /// One particle of the main system exactly at <paramref name="position"/> (no shape) living
            /// <paramref name="lifetime"/> s — the caller draws the figure (stars circling a stunned head); the other
            /// systems — their share.
            /// </summary>
            public void EmitPoint(Vector3 position, float lifetime)
            {
                var emit = new ParticleSystem.EmitParams { position = position, applyShapeToPosition = false, startLifetime = lifetime };
                for (var i = 0; i < _systems.Length; i++)
                {
                    _carry[i] += _shares[i];
                    var whole = (int)_carry[i];
                    if (whole <= 0) continue;
                    _carry[i] -= whole;
                    var system = _systems[i];
                    if (!system.isPlaying) system.Play(false);
                    system.Emit(emit, whole);
                }
            }

            /// <summary>How many particles the system emitted in one cycle of the pack effect.</summary>
            private static float Emitted(ParticleSystem system)
            {
                var emission = system.emission;
                if (!emission.enabled) return 0f;
                var total = emission.rateOverTime.constantMax * system.main.duration;
                for (var i = 0; i < emission.burstCount; i++)
                    total += emission.GetBurst(i).count.constantMax * Mathf.Max(1, emission.GetBurst(i).cycleCount);
                return total;
            }
        }
    }
}
