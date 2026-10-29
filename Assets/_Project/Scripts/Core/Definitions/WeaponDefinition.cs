using System.Collections.Generic;
using Purge.Core.Audio;
using Purge.Core.Content;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Definitions
{
    /// <summary>One of the player's two weapon slots (ТЗ: firearm or melee in any combination).</summary>
    public enum WeaponSlot
    {
        First = 0,
        Second = 1,
    }

    public enum FireMode
    {
        Single = 0,
        Automatic = 1,
    }

    /// <summary>
    /// How the character holds the weapon: picks the animation set (stance, locomotion, attack, reload) and the hand
    /// pose. Display only.
    /// </summary>
    public enum WeaponHold
    {
        /// <summary>Pistols, revolvers: two hands, short.</summary>
        Pistol = 0,
        /// <summary>Rifles, submachine guns, sniper rifles.</summary>
        Rifle = 1,
        Shotgun = 2,
        /// <summary>Heavy weapons at the hip: minigun, flamethrower, tesla.</summary>
        Heavy = 3,
        /// <summary>On the shoulder: grenade launcher, RPG.</summary>
        Shoulder = 4,
        /// <summary>One-handed melee: knife, machete, sword.</summary>
        Melee = 5,
        /// <summary>Two-handed heavy melee: axe, sledgehammer.</summary>
        MeleeHeavy = 6,
    }

    /// <summary>
    /// A weapon kind (ТЗ «Оружие и снаряжение»): rarity, upgradability, how it is held and what it does — a list of
    /// <see cref="WeaponModule"/>s (exactly one <see cref="WeaponAttack"/>; ranged attacks need
    /// <see cref="AmmoModule"/>). Visual representation is defined separately (WeaponVisualDefinition), never here.
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponDefinition", menuName = "Purge/Definitions/Weapon")]
    public sealed class WeaponDefinition : ContentDefinition
    {
        private const float DefaultSoundRadius = 4f;

        [SerializeField] private Rarity _rarity;

        [Tooltip("Cannot be upgraded in the arsenal: strong at once (about an upgraded weapon of the same rarity at " +
                 "level 6–7).")]
        [SerializeField] private bool _noUpgrades;

        [Tooltip("Open from the start without buying (stage 11б, the model's start_pool): in the drop and, within the " +
                 "rarity allowance, for the run's start.")]
        [SerializeField] private bool _startPool;

        [Tooltip("How the character holds it: the animation set and hand pose.")]
        [SerializeField] private WeaponHold _hold = WeaponHold.Rifle;

        [Tooltip("Sound group the player hears (display only): a folder Sounds/Weapon/<group>/ (attack*, reload*). " +
                 "Default = the folder named by the weapon's id, if any. No sound in the group = the common melee / " +
                 "shot / reload sounds.")]
        [SerializeField, WeaponSoundGroup] private string _sound;

        [Tooltip("Pitch of the weapon's sounds: one group sounds heavier on a weapon a bit lower (0.85–0.9).")]
        [SerializeField, Range(0.5f, 1.5f)] private float _soundPitch = 1f;

        [Tooltip("Volume of the weapon's attack (× the group's).")]
        [SerializeField, Range(0f, 1.5f)] private float _soundVolume = 1f;

        [SerializeReference, ModuleList] private List<WeaponModule> _modules = new List<WeaponModule>();

        // Module lookups, cached: definitions are read every tick.
        [System.NonSerialized] private bool _cached;
        [System.NonSerialized] private WeaponAttack _attack;
        [System.NonSerialized] private AmmoModule _ammo;
        [System.NonSerialized] private SilencerModule _silencer;
        [System.NonSerialized] private MobilityModule _mobility;
        [System.NonSerialized] private FeelModule _feel;
        [System.NonSerialized] private WeaponEffectsModule _effects;
        [System.NonSerialized] private IgniteCellsModule _ignite;

        public Rarity Rarity => _rarity;
        public bool Upgradable => !_noUpgrades;

        /// <summary>Open from the start without buying (stage 11б).</summary>
        public bool StartPool { get => _startPool; internal set => _startPool = value; }

        public WeaponHold Hold => _hold;
        public IReadOnlyList<WeaponModule> Modules => _modules;

        /// <summary>Sound group of the weapon (AudioCatalog): the set one, otherwise the id.</summary>
        public string SoundGroup => string.IsNullOrEmpty(_sound) ? Id : _sound;

        /// <summary>The group set by hand (empty = none).</summary>
        internal string Sound => _sound;

        /// <summary>Pitch of the weapon's sounds (× the cue's random pitch).</summary>
        public float SoundPitch => _soundPitch;

        /// <summary>Volume of the weapon's attack (× the cue's).</summary>
        public float SoundVolume => _soundVolume;

        /// <summary>The attack; null only in a broken asset (<see cref="Validate"/>).</summary>
        public WeaponAttack Attack => Cache()._attack;

        /// <summary>Magazine and ammo type; null for melee.</summary>
        public AmmoModule Ammo => Cache()._ammo;

        public bool IsMelee => Attack == null || Attack.IsMelee;
        public bool UsesAmmo => !IsMelee && Ammo != null;
        public int MagazineSize => UsesAmmo ? Ammo.Magazine : 0;
        public float ReloadTime => UsesAmmo ? Ammo.ReloadTime : 0f;

        /// <summary>Seconds between attacks.</summary>
        public float Cooldown => Attack != null ? Attack.Interval : 1f;

        public FireMode FireMode => Attack != null && Attack.Automatic ? FireMode.Automatic : FireMode.Single;

        /// <summary>
        /// How far an attack is heard by monsters, cells: melee — the swing's; a gun — the weapon's own override or its
        /// ammo type's, × silencer.
        /// </summary>
        public float SoundRadius
        {
            get
            {
                Cache();
                if (_attack is MeleeAttack melee) return melee.SoundRadius;
                var radius = _ammo == null ? DefaultSoundRadius
                    : _ammo.SoundRadius > 0f ? _ammo.SoundRadius
                    : _ammo.Ammo != null ? _ammo.Ammo.SoundRadius : DefaultSoundRadius;
                return _silencer != null ? radius * _silencer.Noise : radius;
            }
        }

        /// <summary>Speed multiplier while in hands (<see cref="MobilityModule"/>; 1 without it).</summary>
        public float SpeedInHands => Cache()._mobility != null ? _mobility.InHands : 1f;

        /// <summary>Extra speed multiplier while firing.</summary>
        public float SpeedWhileFiring => Cache()._mobility != null ? _mobility.WhileFiring : 1f;

        /// <summary>How the weapon feels (<see cref="FeelModule"/>; <see cref="FeelModule.Default"/> without it).</summary>
        public FeelModule Feel => Cache()._feel ?? FeelModule.Default;

        /// <summary>Looks of the attack (<see cref="WeaponEffectsModule"/>), or null: defaults only.</summary>
        public WeaponEffectsModule Effects => Cache()._effects;

        /// <summary>Flammable cells the stream sets alight (<see cref="IgniteCellsModule"/>), or null.</summary>
        public IgniteCellsModule Ignite => Cache()._ignite;

        public bool TryGet<T>(out T module) where T : WeaponModule => Purge.Core.Modules.Modules.TryGet(_modules, out module);

        /// <summary>Problems of the weapon's setup, in English (editor checks).</summary>
        public void Validate(List<string> problems)
        {
            var owner = $"Weapon '{name}'";
            Purge.Core.Modules.Modules.Validate(_modules, owner, problems);
            var attacks = 0;
            foreach (var module in _modules)
                if (module is WeaponAttack) attacks++;
            if (attacks != 1) problems.Add($"{owner}: needs exactly one attack module, has {attacks}.");
            if (attacks == 1 && !IsMelee && Ammo == null) problems.Add($"{owner}: a ranged attack needs 'Ammo and magazine'.");
        }

        private void OnValidate() => _cached = false;

        private WeaponDefinition Cache()
        {
            if (_cached) return this;
            _attack = null;
            _ammo = null;
            _silencer = null;
            _mobility = null;
            _feel = null;
            _effects = null;
            _ignite = null;
            foreach (var module in _modules)
                switch (module)
                {
                    case FeelModule feel when _feel == null: _feel = feel; break;
                    case WeaponEffectsModule effects when _effects == null: _effects = effects; break;
                    case IgniteCellsModule ignite when _ignite == null: _ignite = ignite; break;
                    case WeaponAttack attack when _attack == null: _attack = attack; break;
                    case AmmoModule ammo when _ammo == null: _ammo = ammo; break;
                    case SilencerModule silencer when _silencer == null: _silencer = silencer; break;
                    case MobilityModule mobility when _mobility == null: _mobility = mobility; break;
                }

            _cached = true;
            return this;
        }

        /// <summary>For tests and tools: replaces the modules.</summary>
        internal void Configure(string id, params WeaponModule[] modules)
        {
            SetId(id);
            _modules = new List<WeaponModule>(modules);
            _cached = false;
        }

        internal void ConfigureLook(Rarity rarity, WeaponHold hold, bool noUpgrades = false)
        {
            _rarity = rarity;
            _hold = hold;
            _noUpgrades = noUpgrades;
        }

        /// <summary>Tools: the modules after editing them in place.</summary>
        internal List<WeaponModule> MutableModules
        {
            get
            {
                _cached = false;
                return _modules;
            }
        }

        internal void SetSound(string group) => _sound = group;

        internal void SetSoundTone(float pitch, float volume)
        {
            _soundPitch = pitch;
            _soundVolume = volume;
        }
    }
}
