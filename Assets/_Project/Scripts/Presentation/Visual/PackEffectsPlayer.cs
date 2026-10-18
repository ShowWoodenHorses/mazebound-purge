using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Common;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Gameplay.Drops;
using Purge.Gameplay.Level;
using Purge.Gameplay.Player;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Visibility;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// One-shot effects of Art/Effects (pack effects, stage Э) that content names by reference: muzzle flashes of weapons
    /// (<see cref="WeaponEffectsModule"/>), explosions (<see cref="IExplosionLook"/>), and the defaults of
    /// <see cref="CombatVisualDefinition"/> for content without its own. Display only.
    /// <para>While loading, every effect the level can show is loaded and pooled: the defaults, the weapons on the level,
    /// in the drop pool and the base weapon, the throwables of the pool and the slot. A reference not loaded then is not
    /// shown (a warning once) — nothing is loaded or created for the first time during play. Each kind has
    /// <see cref="PoolSize"/> instances; when all are showing, the oldest is restarted in the new place.</para>
    /// Effects start only in cells the player sees. An effect with a <see cref="CombatEffect.Radius"/> is scaled to the
    /// radius it is played with (explosions).
    /// </summary>
    public sealed class PackEffectsPlayer : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        /// <summary>Instances of one effect: a rifle's flash lives ~0.7 s at 10 shots a second.</summary>
        public const int PoolSize = 6;

        private readonly CombatVisualDefinition _visual;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly VisibilitySystem _visibility;
        private readonly LevelData _level;
        private readonly DropPool _drops;
        private readonly PlayerDefinition _player;
        private readonly ThrowableSlot _slot;

        private readonly Dictionary<string, Kind> _kinds = new Dictionary<string, Kind>();
        private readonly List<Shown> _shown = new List<Shown>();
        private readonly List<GameObject> _warmup = new List<GameObject>();
        private readonly HashSet<string> _missing = new HashSet<string>();
        private Transform _parent;

        public PackEffectsPlayer(CombatVisualDefinition visual, IAssetOwner assets, LevelViewRoot root,
            VisibilitySystem visibility, LevelData level, DropPool drops = null, PlayerDefinition player = null,
            ThrowableSlot slot = null)
        {
            _visual = visual;
            _assets = assets;
            _root = root;
            _visibility = visibility;
            _level = level;
            _drops = drops;
            _player = player;
            _slot = slot;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>Effects being shown now.</summary>
        public int ActiveEffects => _shown.Count;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var wanted = new List<AssetReferenceGameObject>();
            CollectWanted(wanted);
            if (wanted.Count == 0) return;

            _parent = new GameObject("Pack Effects").transform;
            _parent.SetParent(_root != null ? _root.transform : null, false);
            foreach (var reference in wanted)
            {
                var key = reference.AssetGUID;
                if (_kinds.ContainsKey(key)) continue;
                var prefab = await _assets.LoadAsync<GameObject>(reference, cancellation);
                cancellation.ThrowIfCancellationRequested();
                var kind = new Kind { Instances = new Instance[PoolSize] };
                for (var i = 0; i < PoolSize; i++)
                    kind.Instances[i] = Create(prefab);
                _kinds[key] = kind;
                _warmup.Add(kind.Instances[0].View);
            }
        }

        public void CollectWarmup(List<GameObject> objects) => objects.AddRange(_warmup);

        /// <summary>
        /// Plays the effect at a point of the view root (+Z of <paramref name="rotation"/> = the effect's forward).
        /// <paramref name="radius"/> &gt; 0 scales an effect that has a radius to it. False: no such effect loaded,
        /// or the cell is not seen.
        /// </summary>
        public bool Play(AssetReferenceGameObject reference, Vector3 position, Quaternion rotation, float radius = 0f)
        {
            if (!CombatVisualDefinition.IsSet(reference)) return false;
            if (!_kinds.TryGetValue(reference.AssetGUID, out var kind))
            {
                if (_missing.Add(reference.AssetGUID))
                    GameLog.Warning(LogChannel.Visual, $"Effect {reference.AssetGUID} was not loaded with the level: not shown.");
                return false;
            }

            if (!_visibility.IsVisible(PlayerMovement.CellOf(new Vector2(position.x, position.z))))
                return false;

            var instance = Take(kind);
            var transform = instance.View.transform;
            transform.SetLocalPositionAndRotation(position, rotation);
            var scale = radius > 0f && instance.Effect.Radius > 0f ? radius / instance.Effect.Radius : 1f;
            transform.localScale = new Vector3(scale, scale, scale);
            instance.View.SetActive(true);
            instance.Effect.Play();
            _shown.Add(new Shown { Instance = instance, Left = instance.Effect.Duration });
            return true;
        }

        public void LateTick(float deltaTime)
        {
            for (var i = _shown.Count - 1; i >= 0; i--)
            {
                var shown = _shown[i];
                shown.Left -= deltaTime;
                if (shown.Left > 0f)
                {
                    _shown[i] = shown;
                    continue;
                }

                shown.Instance.View.SetActive(false);
                shown.Instance.Busy = false;
                _shown.RemoveAt(i);
            }
        }

        public void Dispose()
        {
            foreach (var kind in _kinds.Values)
            foreach (var instance in kind.Instances)
                if (instance != null) UnityObjects.Destroy(instance.View);
            _kinds.Clear();
            _shown.Clear();
            _warmup.Clear();
            _missing.Clear();
            if (_parent != null) UnityObjects.Destroy(_parent.gameObject);
            _parent = null;
        }

        private void CollectWanted(List<AssetReferenceGameObject> wanted)
        {
            if (_visual != null)
            {
                foreach (var entry in _visual.MuzzleFlashes)
                    if (entry != null) Add(wanted, entry.Effect);
                Add(wanted, _visual.ExplosionSmall);
                Add(wanted, _visual.ExplosionLarge);
            }

            if (_level != null)
                foreach (var pickup in _level.Weapons) AddWeapon(wanted, pickup.Definition);
            if (_drops != null)
            {
                foreach (var weapon in _drops.Weapons) AddWeapon(wanted, weapon);
                foreach (var throwable in _drops.Throwables) AddThrowable(wanted, throwable);
            }
            if (_player != null) AddWeapon(wanted, _player.BaseWeapon);
            if (_slot != null) AddThrowable(wanted, _slot.Definition);
        }

        private static void AddWeapon(List<AssetReferenceGameObject> wanted, WeaponDefinition weapon)
        {
            if (weapon == null) return;
            if (weapon.Effects != null) Add(wanted, weapon.Effects.MuzzleFlash);
            if (weapon.Attack is ExplosiveAttack explosive) Add(wanted, explosive.Look);
        }

        private static void AddThrowable(List<AssetReferenceGameObject> wanted, ThrowableDefinition throwable)
        {
            if (throwable == null) return;
            foreach (var effect in throwable.Effects)
                if (effect is IExplosionLook look) Add(wanted, look.Look);
        }

        private static void Add(List<AssetReferenceGameObject> wanted, AssetReferenceGameObject reference)
        {
            if (CombatVisualDefinition.IsSet(reference)) wanted.Add(reference);
        }

        private Instance Create(GameObject prefab)
        {
            var view = UnityEngine.Object.Instantiate(prefab, _parent);
            view.name = prefab.name;
            view.SetActive(false);
            var effect = view.GetComponent<CombatEffect>() ?? view.AddComponent<CombatEffect>();
            return new Instance { View = view, Effect = effect };
        }

        /// <summary>A free instance, or the one shown longest (restarted in the new place).</summary>
        private Instance Take(Kind kind)
        {
            foreach (var instance in kind.Instances)
                if (!instance.Busy)
                {
                    instance.Busy = true;
                    return instance;
                }

            for (var i = 0; i < _shown.Count; i++)
            {
                var oldest = _shown[i].Instance;
                if (Array.IndexOf(kind.Instances, oldest) < 0) continue;
                _shown.RemoveAt(i); // the list is in start order: the first of the kind is the oldest
                return oldest;
            }

            return kind.Instances[0];
        }

        private sealed class Kind
        {
            public Instance[] Instances;
        }

        private sealed class Instance
        {
            public GameObject View;
            public CombatEffect Effect;
            public bool Busy;
        }

        private struct Shown
        {
            public Instance Instance;
            public float Left;
        }
    }
}
