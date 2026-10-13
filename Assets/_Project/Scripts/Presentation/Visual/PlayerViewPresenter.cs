using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Core.Definitions;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Level;
using Maze.Gameplay.Player;
using Maze.Gameplay.Weapons;
using UnityEngine;
using P = Maze.Presentation.Visual.PlayerAnimatorParameters;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Player view (ТЗ §79: PlayerRuntime → PlayerView): instantiates the player prefab after the player spawned and
    /// mirrors gameplay state every frame — position, facing, Animator parameters — and keeps the camera on the
    /// player. The Animator only shows state (<see cref="PlayerAnimatorParameters"/>): speed, weapon in hands,
    /// reload; attacks, shots, hits, interactions and death come from gameplay events. Parameters missing from the
    /// controller are skipped. <see cref="LastMeleeContactDelay"/> tells other views when the last melee attack's clip
    /// actually hits (the clip starts with a wind-up). Damage flashes the model (<see cref="HitFlash"/>). Must be registered after <see cref="PlayerSystem"/> (same load stage, registration order).
    /// </summary>
    public sealed class PlayerViewPresenter : ILevelLoadStep, ILevelLateTickable, IDisposable
    {
        private const float TurnSpeed = 900f; // degrees per second
        private const string PlayerHeadId = "player";

        private readonly PlayerSystem _player;
        private readonly PlayerVisualDefinition _visual;
        private readonly IAssetOwner _assets;
        private readonly LevelViewRoot _root;
        private readonly TopDownCamera _camera;
        private readonly PlayerCombat _combat;
        private readonly WeaponSystem _weapons;
        private readonly PlayerHealth _health;
        private readonly PlayerInteraction _interaction;
        private readonly BlobShadows _shadows;
        private readonly CombatVisualDefinition _combatVisual;
        private readonly LevelData _level;
        private readonly HashSet<int> _parameters = new HashSet<int>();
        private readonly float[] _attackContacts = new float[P.AttackVariants];
        private readonly float[] _attackSweeps = new float[P.AttackVariants];
        private HitFlash _flash;

        private GameObject _view;
        private Animator _animator;
        private int _upperBodyLayer = -1;
        private int _attackCounter;
        private int _lastHealth;
        private bool _subscribed;

        public PlayerViewPresenter(PlayerSystem player, PlayerVisualDefinition visual, IAssetOwner assets,
            LevelViewRoot root, TopDownCamera camera, PlayerCombat combat, WeaponSystem weapons, PlayerHealth health,
            PlayerInteraction interaction, BlobShadows shadows, CombatVisualDefinition combatVisual, LevelData level)
        {
            _level = level;
            _combatVisual = combatVisual;
            _shadows = shadows;
            _combat = combat;
            _player = player;
            _visual = visual;
            _assets = assets;
            _root = root;
            _camera = camera;
            _weapons = weapons;
            _health = health;
            _interaction = interaction;
        }

        public LevelLoadStage Stage => LevelLoadStage.SpawnPlayer;

        public GameObject View => _view;

        /// <summary>The player's head (breath); default until the view is created.</summary>
        public CharacterHead Head { get; private set; }

        /// <summary>
        /// Seconds from the last melee attack until its clip hits (from the controller's AttackContact data and the
        /// weapon's cooldown, which the clip is stretched to); 0 without that data. Set when the attack happens.
        /// </summary>
        public float LastMeleeContactDelay { get; private set; }

        /// <summary>
        /// Which way the last melee attack's clip swings: +1 = to the player's right (left to right), -1 = to the left,
        /// 0 = unknown (no such data in the controller). Set when the attack happens.
        /// </summary>
        public float LastMeleeSweep { get; private set; }

        public async UniTask ExecuteAsync(CancellationToken cancellation)
        {
            if (!_player.IsSpawned)
                throw new InvalidOperationException($"{nameof(PlayerViewPresenter)} must run after {nameof(PlayerSystem)} spawned the player.");

            var prefab = await _assets.LoadAsync<GameObject>(_visual.Prefab, cancellation);
            cancellation.ThrowIfCancellationRequested();

            _view = UnityEngine.Object.Instantiate(prefab, _root.transform);
            _view.name = "Player";
            _shadows.Attach(_view);
            _flash = HitFlash.Create(_view); // before weapons are put in hands: the body flashes, not the gun
            _animator = _view.GetComponentInChildren<Animator>();
            if (_level != null && _level.VisualTheme != null)
                _flash?.SetFrost(_level.VisualTheme.Weather.PlayerFrost);
            Head = CharacterHead.Of(PlayerHeadId, null, _view, _animator);
            CacheParameters();

            _lastHealth = _health.Current;
            _combat.Attacked += OnAttacked;
            _combat.ReloadChanged += OnReloadChanged;
            _health.Changed += OnHealthChanged;
            _interaction.Interacted += OnInteracted;
            _subscribed = true;
            Sync(0f, snap: true);
        }

        public void LateTick(float deltaTime)
        {
            Sync(deltaTime, snap: false);
            _flash?.Tick(deltaTime);
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                _combat.Attacked -= OnAttacked;
                _combat.ReloadChanged -= OnReloadChanged;
                _health.Changed -= OnHealthChanged;
                _interaction.Interacted -= OnInteracted;
                _subscribed = false;
            }

            UnityObjects.Destroy(_view);
            _view = null;
            _animator = null;
            _flash = null;
            Head = default;
        }

        private void Sync(float deltaTime, bool snap)
        {
            if (_view == null) return;

            var position = new Vector3(_player.Position.x, 0f, _player.Position.y);
            var facing = Quaternion.LookRotation(new Vector3(_player.Facing.x, 0f, _player.Facing.y), Vector3.up);
            var transform = _view.transform;
            transform.localPosition = position;
            transform.localRotation = snap
                ? facing
                : Quaternion.RotateTowards(transform.localRotation, facing, TurnSpeed * deltaTime);

            if (_parameters.Count > 0)
            {
                var weapon = _weapons.Active;
                SetFloat(P.SpeedHash, _player.SpeedFactor);
                SetInt(P.WeaponHash, weapon == null ? P.WeaponNone : weapon.Slot == WeaponSlot.Melee ? P.WeaponMelee : P.WeaponRanged);
                SetBool(P.AutomaticHash, weapon != null && weapon.Slot == WeaponSlot.Ranged && weapon.Definition.FireMode == FireMode.Automatic);
                SetBool(P.ReloadingHash, weapon != null && weapon.IsReloading);
            }

            _camera.Follow(_root.transform.TransformPoint(position), deltaTime, snap);
        }

        private void OnAttacked(WeaponRuntime weapon, Vector2 direction)
        {
            if (weapon.Slot == WeaponSlot.Melee)
            {
                var variant = _attackCounter++ % P.AttackVariants;
                LastMeleeContactDelay = _attackContacts[variant] * weapon.Definition.Cooldown;
                LastMeleeSweep = _attackSweeps[variant];
                SetInt(P.AttackIndexHash, variant);
                SetFloat(P.AttackSpeedHash, 1f / Mathf.Max(weapon.Definition.Cooldown, 0.01f));
                SetTrigger(P.AttackHash);
            }
            else
            {
                SetTrigger(P.ShootHash);
            }
        }

        private void OnReloadChanged(WeaponRuntime weapon)
        {
            if (weapon.IsReloading)
                SetFloat(P.ReloadSpeedHash, 1f / Mathf.Max(weapon.Definition.ReloadTime, 0.01f));
        }

        private void OnHealthChanged(int current, int max)
        {
            var damaged = current < _lastHealth;
            _lastHealth = current;
            if (!damaged) return;

            if (current <= 0)
            {
                // The level finishes now and late ticks stop: a flash would stay on the corpse.
                _flash?.Stop();
            }
            else if (_combatVisual != null)
            {
                _flash?.Trigger(_combatVisual.PlayerHitFlashColor, _combatVisual.HitFlashTime);
            }

            if (current > 0)
            {
                SetTrigger(P.HitHash);
                return;
            }

            SetBool(P.DeadHash, true);
            if (_upperBodyLayer >= 0)
                _animator.SetLayerWeight(_upperBodyLayer, 0f);
        }

        private void OnInteracted(InteractionResult result, DoorData door)
        {
            if (result == InteractionResult.WeaponTaken || result == InteractionResult.DoorOpened ||
                result == InteractionResult.DoorClosed || result == InteractionResult.DoorUnlocked)
                SetTrigger(P.UseHash);
        }

        /// <summary>Once per view: <c>animator.parameters</c> allocates.</summary>
        private void CacheParameters()
        {
            _parameters.Clear();
            Array.Clear(_attackContacts, 0, _attackContacts.Length);
            Array.Clear(_attackSweeps, 0, _attackSweeps.Length);
            _upperBodyLayer = -1;
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;

            foreach (var parameter in _animator.parameters)
            {
                _parameters.Add(parameter.nameHash);
                var contact = Array.IndexOf(P.AttackContactHashes, parameter.nameHash);
                if (contact >= 0 && contact < _attackContacts.Length)
                    _attackContacts[contact] = Mathf.Clamp01(parameter.defaultFloat);
                var sweep = Array.IndexOf(P.AttackSweepHashes, parameter.nameHash);
                if (sweep >= 0 && sweep < _attackSweeps.Length)
                    _attackSweeps[sweep] = Mathf.Sign(parameter.defaultFloat);
            }
            _upperBodyLayer = _animator.GetLayerIndex(P.UpperBodyLayer);
        }

        private void SetFloat(int hash, float value)
        {
            if (_parameters.Contains(hash)) _animator.SetFloat(hash, value);
        }

        private void SetInt(int hash, int value)
        {
            if (_parameters.Contains(hash)) _animator.SetInteger(hash, value);
        }

        private void SetBool(int hash, bool value)
        {
            if (_parameters.Contains(hash)) _animator.SetBool(hash, value);
        }

        private void SetTrigger(int hash)
        {
            if (_parameters.Contains(hash)) _animator.SetTrigger(hash);
        }
    }
}
