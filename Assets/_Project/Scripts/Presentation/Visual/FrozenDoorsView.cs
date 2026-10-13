using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Core.Definitions;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Doors;
using Maze.Gameplay.Level;
using Maze.Gameplay.Weapons;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Ice of the frozen doors (display only): the theme's <see cref="ThemeWeather.IceOverlay"/> on every frozen door's
    /// view (a child — it is shown and hidden with the door), loaded through the level's asset owner only when the
    /// level has frozen doors. A melee hit on the ice shows at the attack clip's contact moment, like hits on zombies
    /// (<see cref="PlayerViewPresenter.LastMeleeContactDelay"/>): pieces chip off (<see cref="FrozenDoorIce"/>), a few
    /// splinters fly; the last hit hides the ice with a burst of shards. One particle system for all splinters.
    /// Must be registered after <see cref="DoorViewPresenter"/> (same load stage).
    /// </summary>
    public sealed class FrozenDoorsView : ILevelLoadStep, ILevelLateTickable, IViewWarmup, IDisposable
    {
        private const int HitSplinters = 5;
        private const int BreakSplinters = 22;
        private static readonly Color SplinterColor = new Color(0.82f, 0.93f, 1f, 0.95f);

        private readonly LevelData _level;
        private readonly DoorSystem _doors;
        private readonly EntityViewRegistry _views;
        private readonly IAssetOwner _assets;
        private readonly PlayerCombat _combat;
        private readonly PlayerViewPresenter _playerView;
        private readonly LevelViewRoot _root;
        private readonly Dictionary<string, Ice> _ice = new Dictionary<string, Ice>();
        private readonly List<Pending> _pending = new List<Pending>();
        private ParticleSystem _splinters;
        private bool _subscribed;

        public FrozenDoorsView(LevelData level, DoorSystem doors, EntityViewRegistry views, IAssetOwner assets,
            PlayerCombat combat, PlayerViewPresenter playerView, LevelViewRoot root)
        {
            _level = level;
            _doors = doors;
            _views = views;
            _assets = assets;
            _combat = combat;
            _playerView = playerView;
            _root = root;
        }

        public LevelLoadStage Stage => LevelLoadStage.BuildVisuals;

        /// <summary>(door) when a hit on its ice shows (sounds).</summary>
        public event Action<DoorData> IceHitShown;

        /// <summary>(door) when its ice breaks on screen (sounds).</summary>
        public event Action<DoorData> IceBrokenShown;

        /// <summary>Ice views by door id (tests).</summary>
        public int IceCount => _ice.Count;

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_subscribed)
            {
                _combat.Attacked += OnAttacked;
                _subscribed = true;
            }

            var weather = _level.VisualTheme != null ? _level.VisualTheme.Weather : null;
            var frozen = false;
            foreach (var door in _doors.Doors)
                frozen |= _doors.IsFrozen(door.Id);
            if (!frozen || weather == null)
                return;

            if (weather.HasIceOverlay)
            {
                var prefab = await _assets.LoadAsync<GameObject>(weather.IceOverlay, cancellation);
                cancellation.ThrowIfCancellationRequested();
                foreach (var door in _doors.Doors)
                    if (_doors.IsFrozen(door.Id) && _views.TryGet(door.Id, out var view) && view.GameObject != null)
                    {
                        var instance = UnityEngine.Object.Instantiate(prefab, view.GameObject.transform, false);
                        instance.name = "Ice";
                        _ice[door.Id] = new Ice
                        {
                            View = view,
                            Root = instance,
                            Overlay = instance.GetComponent<FrozenDoorIce>(),
                            Hits = Mathf.Max(door.IceHits, 1),
                        };
                    }
            }

            var material = weather.SnowMaterial != null ? weather.SnowMaterial : weather.BreathMaterial;
            if (material != null)
            {
                _splinters = WeatherParticles.Create("IceSplinters", _root.transform, material, 96, fadeIn: 0.02f, fadeOut: 0.6f);
                var main = _splinters.main;
                main.gravityModifier = 1.6f; // particles only, no physics
            }
        }

        public void LateTick(float deltaTime)
        {
            for (var i = 0; i < _pending.Count;)
            {
                var pending = _pending[i];
                if (!pending.Timed)
                {
                    pending.Left = _playerView != null ? _playerView.LastMeleeContactDelay : 0f;
                    pending.Timed = true;
                }
                else
                {
                    pending.Left -= deltaTime;
                }

                if (pending.Left > 0f)
                {
                    _pending[i] = pending;
                    i++;
                    continue;
                }

                _pending.RemoveAt(i);
                Show(pending);
            }
        }

        public void CollectWarmup(List<GameObject> objects)
        {
            foreach (var ice in _ice.Values)
                if (ice.Root != null) objects.Add(ice.Root);
            if (_splinters != null) objects.Add(_splinters.gameObject);
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _combat.Attacked -= OnAttacked;
                _subscribed = false;
            }

            _ice.Clear(); // destroyed with the door views
            _pending.Clear();
            UnityObjects.DestroyObjectOf(_splinters);
            _splinters = null;
        }

        /// <summary>Gameplay already counted the hit (this tick); the view waits for the clip's hit.</summary>
        private void OnAttacked(WeaponRuntime weapon, Vector2 direction)
        {
            var door = _combat.LastIceHit;
            if (weapon.Slot != WeaponSlot.Melee || door == null) return;
            _pending.Add(new Pending
            {
                Door = door,
                HitsLeft = _doors.IceLeft(door.Id),
                Direction = direction,
            });
        }

        private void Show(Pending pending)
        {
            var door = pending.Door;
            var broken = pending.HitsLeft <= 0;
            var shown = true;
            if (_ice.TryGetValue(door.Id, out var ice))
            {
                shown = ice.View.IsVisible;
                if (broken) ice.Root.SetActive(false);
                else if (ice.Overlay != null) ice.Overlay.SetDamage(1f - (float)pending.HitsLeft / ice.Hits);
            }

            if (shown)
                Splinters(door, pending.Direction, broken ? BreakSplinters : HitSplinters, broken);

            if (broken) IceBrokenShown?.Invoke(door);
            else IceHitShown?.Invoke(door);
        }

        /// <summary>From the door's face toward the player, falling on the floor.</summary>
        private void Splinters(DoorData door, Vector2 direction, int count, bool burst)
        {
            if (_splinters == null) return;
            if (!_splinters.isPlaying) _splinters.Play(); // the warmup stops every particle system

            var face = new Vector2(door.Position.X, door.Position.Y) - direction * 0.45f;
            for (var i = 0; i < count; i++)
            {
                var spread = UnityEngine.Random.insideUnitCircle * (burst ? 1.6f : 0.8f);
                var back = -direction * UnityEngine.Random.Range(0.6f, burst ? 2.2f : 1.4f);
                _splinters.Emit(new ParticleSystem.EmitParams
                {
                    position = _root.transform.TransformPoint(new Vector3(
                        face.x + UnityEngine.Random.Range(-0.3f, 0.3f), UnityEngine.Random.Range(0.3f, 1.3f),
                        face.y + UnityEngine.Random.Range(-0.3f, 0.3f))),
                    velocity = new Vector3(back.x + spread.x, UnityEngine.Random.Range(0.8f, 2.4f), back.y + spread.y),
                    startSize = UnityEngine.Random.Range(0.05f, burst ? 0.16f : 0.1f),
                    startLifetime = UnityEngine.Random.Range(0.45f, 0.8f),
                    rotation = UnityEngine.Random.Range(0f, 360f),
                    startColor = SplinterColor,
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        private sealed class Ice
        {
            public EntityView View;
            public GameObject Root;
            public FrozenDoorIce Overlay;
            public int Hits;
        }

        private struct Pending
        {
            public DoorData Door;
            public int HitsLeft;
            public Vector2 Direction;
            public bool Timed;
            public float Left;
        }
    }
}
