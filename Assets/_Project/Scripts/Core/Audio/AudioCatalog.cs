using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Maze.Core.Audio
{
    /// <summary>A music track: an Addressable clip (loaded only while it plays) and how it loops.</summary>
    [Serializable]
    public sealed class MusicTrack
    {
        [SerializeField] private AssetReferenceT<AudioClip> _clip = new AssetReferenceT<AudioClip>(string.Empty);

        [SerializeField, Range(0f, 1f)] private float _volume = 0.6f;

        [Tooltip("Seconds before the end at which the next loop starts over the fading tail. 0 = the clip loops " +
                 "seamlessly by itself.")]
        [SerializeField, Min(0f)] private float _loopOverlap;

        public AssetReferenceT<AudioClip> Clip { get => _clip; internal set => _clip = value; }
        public float Volume { get => _volume; internal set => _volume = value; }
        public float LoopOverlap { get => _loopOverlap; internal set => _loopOverlap = value; }
        public bool IsSet => _clip != null && _clip.RuntimeKeyIsValid();
    }

    /// <summary>Voice of a zombie type: one set of zombie sounds, told apart by pitch.</summary>
    [Serializable]
    public sealed class ZombieVoice
    {
        [Tooltip("ZombieDefinition id (walker, listener, hunter).")]
        public string ZombieId;

        [Range(0.5f, 1.5f)] public float Pitch = 1f;
    }

    /// <summary>
    /// Own sounds of a group of weapons (<c>WeaponDefinition.SoundGroup</c>): files of <c>Sounds/Weapon/&lt;group&gt;/</c>.
    /// An empty cue falls back to the common one (melee swing / shot, magazine / single reload).
    /// </summary>
    [Serializable]
    public sealed class WeaponSound
    {
        [Tooltip("Folder name under Sounds/Weapon; weapons pick it by their Sound field (or id).")]
        [SerializeField] private string _group;
        [Tooltip("Files attack*: the shot or swing.")]
        [SerializeField] private SoundCue _attack = new SoundCue(0.5f, 0f, 4, new Vector2(0.95f, 1.05f));
        [Tooltip("Files reload*.")]
        [SerializeField] private SoundCue _reload = new SoundCue(0.7f, 0f, 1, Vector2.one);

        public WeaponSound(string group, SoundCue attack, SoundCue reload)
        {
            _group = group;
            _attack = attack ?? new SoundCue();
            _reload = reload ?? new SoundCue();
        }

        public string Group => _group;
        public SoundCue Attack => _attack;
        public SoundCue Reload => _reload;
    }

    /// <summary>
    /// Every sound and music track of the game (ТЗ §71), Addressable at <see cref="Address"/> and resident for the
    /// application lifetime (music clips are loaded only while they play). Display only: gameplay never reads it.
    /// Filled by Maze → Dev → Build Audio, which keeps the tuning (volumes, ranges…) on rebuild.
    /// </summary>
    [CreateAssetMenu(fileName = "AudioCatalog", menuName = "Maze/Audio Catalog")]
    public sealed class AudioCatalog : ScriptableObject
    {
        public const string Address = "Audio/Catalog";

        [Header("Music")]
        [SerializeField] private MusicTrack _menuMusic = new MusicTrack();
        [SerializeField] private MusicTrack _levelMusic = new MusicTrack();
        [Tooltip("Seconds of a cross-fade between tracks.")]
        [SerializeField, Min(0f)] private float _musicFade = 1.5f;
        [Tooltip("Music volume while the game is paused or the map is open (share of the normal volume).")]
        [SerializeField, Range(0f, 1f)] private float _pausedMusicVolume = 0.5f;
        [Tooltip("Music volume under the result jingle (share of the normal volume).")]
        [SerializeField, Range(0f, 1f)] private float _resultMusicVolume = 0.25f;

        [Header("Player")]
        [SerializeField] private SoundCue _footstep = new SoundCue(0.6f, 0f, 2, new Vector2(0.92f, 1.08f));
        [Tooltip("Not used yet: for a walk animation, if one is added.")]
        [SerializeField] private SoundCue _footstepWalk = new SoundCue(0.6f, 0f, 2, new Vector2(0.92f, 1.08f));
        [SerializeField] private SoundCue _playerHurt = new SoundCue(0.8f, 0f, 1, new Vector2(0.95f, 1.05f), 0.15f);
        [SerializeField] private SoundCue _playerDeath = new SoundCue(1f, 0f, 1, Vector2.one);

        [Header("Weapons")]
        [Tooltip("Common swing of melee weapons without their own sound group.")]
        [SerializeField] private SoundCue _meleeSwing = new SoundCue(0.9f, 0f, 2, new Vector2(0.9f, 1.1f));
        [Tooltip("Common shot of guns without their own sound group.")]
        [SerializeField] private SoundCue _shot = new SoundCue(0.5f, 0f, 4, new Vector2(0.95f, 1.05f));
        [Tooltip("Sound groups (Sounds/Weapon/<group>/): weapons whose Sound (or id) names one use its sounds.")]
        [SerializeField] private List<WeaponSound> _weaponSounds = new List<WeaponSound>();
        [Tooltip("Automatic weapons: a magazine change.")]
        [SerializeField] private SoundCue _reloadMagazine = new SoundCue(0.7f, 0f, 1, Vector2.one);
        [Tooltip("Single-shot weapons.")]
        [SerializeField] private SoundCue _reloadSingle = new SoundCue(0.6f, 0f, 1, Vector2.one);
        [SerializeField] private SoundCue _switchWeapon = new SoundCue(0.6f, 0f, 1, new Vector2(0.97f, 1.03f));
        [Tooltip("A bullet hits a wall or a closed door.")]
        [SerializeField] private SoundCue _bulletImpact = new SoundCue(0.5f, 10f, 3, new Vector2(0.9f, 1.1f));

        [Header("Pickups")]
        [SerializeField] private SoundCue _pickupKey = new SoundCue(0.45f, 0f, 1, Vector2.one);
        [SerializeField] private SoundCue _pickupMedkit = new SoundCue(0.9f, 0f, 1, Vector2.one);
        [SerializeField] private SoundCue _pickupWeapon = new SoundCue(0.5f, 0f, 1, Vector2.one);
        [SerializeField] private SoundCue _pickupMapFragment = new SoundCue(0.6f, 0f, 1, Vector2.one);

        [Header("Doors")]
        [SerializeField] private SoundCue _doorOpen = new SoundCue(0.9f, 10f, 2, new Vector2(0.95f, 1.05f));
        [SerializeField] private SoundCue _doorClose = new SoundCue(0.8f, 10f, 2, new Vector2(0.95f, 1.05f));
        [SerializeField] private SoundCue _doorUnlock = new SoundCue(0.8f, 10f, 1, Vector2.one);
        [Tooltip("Locked and no key; also a door that cannot close (someone in the doorway).")]
        [SerializeField] private SoundCue _doorLocked = new SoundCue(0.7f, 10f, 1, new Vector2(0.97f, 1.03f), 0.3f);
        [Tooltip("A melee hit on a frozen door's ice (empty: the bullet impact).")]
        [SerializeField] private SoundCue _iceHit = new SoundCue(0.7f, 10f, 2, new Vector2(0.92f, 1.08f));
        [Tooltip("The ice of a frozen door breaks (empty: the door unlock).")]
        [SerializeField] private SoundCue _iceBreak = new SoundCue(0.85f, 12f, 1, new Vector2(0.95f, 1.05f));

        [Header("Zombies")]
        [Tooltip("Groans now and then while idle, patrolling or returning.")]
        [SerializeField] private SoundCue _zombieGroan = new SoundCue(0.4f, 7f, 2, new Vector2(0.95f, 1.05f));
        [Tooltip("Roar when a zombie notices the player (Alert, before the chase).")]
        [SerializeField] private SoundCue _zombieRoar = new SoundCue(0.55f, 12f, 2, new Vector2(0.95f, 1.05f));
        [SerializeField] private SoundCue _zombieAttack = new SoundCue(0.5f, 8f, 2, new Vector2(0.95f, 1.05f));
        [SerializeField] private SoundCue _zombieHurt = new SoundCue(0.5f, 10f, 3, new Vector2(0.95f, 1.05f));
        [SerializeField] private SoundCue _zombieDeath = new SoundCue(0.55f, 10f, 2, new Vector2(0.95f, 1.05f));
        [SerializeField] private SoundCue _zombieStepWalk = new SoundCue(0.9f, 6f, 3, new Vector2(0.9f, 1.05f));
        [SerializeField] private SoundCue _zombieStepRun = new SoundCue(0.8f, 8f, 3, new Vector2(0.9f, 1.05f));
        [Tooltip("Pitch per zombie type (one set of voices for all types).")]
        [SerializeField] private List<ZombieVoice> _zombieVoices = new List<ZombieVoice>();
        [Tooltip("Seconds between groans of one zombie: random in [min, max].")]
        [SerializeField] private Vector2 _groanInterval = new Vector2(6f, 14f);
        [Tooltip("Cells a zombie walks per step sound (patrol / return).")]
        [SerializeField, Min(0.1f)] private float _zombieWalkStep = 0.7f;
        [Tooltip("Cells a zombie runs per step sound (chase).")]
        [SerializeField, Min(0.1f)] private float _zombieRunStep = 1.1f;
        [Tooltip("Stinger when zombies notice the player: once per chase, not per zombie.")]
        [SerializeField] private SoundCue _chaseStinger = new SoundCue(0.6f, 0f, 1, Vector2.one);
        [Tooltip("The stinger plays again only after this many seconds without any zombie noticing the player.")]
        [SerializeField, Min(0f)] private float _stingerRearm = 6f;

        [Header("World")]
        [Tooltip("Loop of a burning torch at every light source; only the nearest ones play.")]
        [SerializeField] private SoundCue _torchLoop = new SoundCue(0.7f, 5f, 3, Vector2.one);
        [SerializeField, Range(0, 8)] private int _maxTorchLoops = 3;

        [Header("Interface")]
        [SerializeField] private SoundCue _click = new SoundCue(0.5f, 0f, 2, new Vector2(0.97f, 1.03f));
        [SerializeField] private SoundCue _back = new SoundCue(0.3f, 0f, 2, Vector2.one);
        [Tooltip("A locked level or another unavailable button.")]
        [SerializeField] private SoundCue _denied = new SoundCue(0.5f, 0f, 1, Vector2.one, 0.2f);
        [SerializeField] private SoundCue _pauseOpen = new SoundCue(0.8f, 0f, 1, Vector2.one);
        [SerializeField] private SoundCue _pauseResume = new SoundCue(0.6f, 0f, 1, Vector2.one);
        [SerializeField] private SoundCue _mapOpen = new SoundCue(0.7f, 0f, 1, Vector2.one);
        [SerializeField] private SoundCue _mapClose = new SoundCue(0.7f, 0f, 1, Vector2.one);
        [Tooltip("The player reached an exit.")]
        [SerializeField] private SoundCue _exitReached = new SoundCue(0.8f, 0f, 1, Vector2.one);
        [Tooltip("The \"Finish level?\" window.")]
        [SerializeField] private SoundCue _confirmExit = new SoundCue(1f, 0f, 1, Vector2.one);
        [SerializeField] private SoundCue _levelComplete = new SoundCue(1f, 0f, 1, Vector2.one);
        [SerializeField] private SoundCue _levelFailed = new SoundCue(0.8f, 0f, 1, Vector2.one);
        [Tooltip("Each star of the result, one after another, pitch rising.")]
        [SerializeField] private SoundCue _star = new SoundCue(0.8f, 0f, 3, Vector2.one);
        [Tooltip("Seconds after the level ends before its jingle (lets the death or exit sound be heard).")]
        [SerializeField, Min(0f)] private float _resultDelay = 0.5f;
        [Tooltip("Seconds between star sounds.")]
        [SerializeField, Min(0.05f)] private float _starInterval = 0.4f;
        [Tooltip("Pitch added per star (the 2nd star is higher than the 1st…).")]
        [SerializeField, Range(0f, 0.5f)] private float _starPitchStep = 0.12f;

        public MusicTrack MenuMusic => _menuMusic;
        public MusicTrack LevelMusic => _levelMusic;
        public float MusicFade => _musicFade;
        public float PausedMusicVolume => _pausedMusicVolume;
        public float ResultMusicVolume => _resultMusicVolume;

        public SoundCue Footstep => _footstep;
        public SoundCue FootstepWalk => _footstepWalk;
        public SoundCue PlayerHurt => _playerHurt;
        public SoundCue PlayerDeath => _playerDeath;

        public SoundCue MeleeSwing => _meleeSwing;
        public SoundCue Shot => _shot;
        public IReadOnlyList<WeaponSound> WeaponSounds => _weaponSounds;
        public SoundCue ReloadMagazine => _reloadMagazine;
        public SoundCue ReloadSingle => _reloadSingle;
        public SoundCue SwitchWeapon => _switchWeapon;
        public SoundCue BulletImpact => _bulletImpact;

        public SoundCue PickupKey => _pickupKey;
        public SoundCue PickupMedkit => _pickupMedkit;
        public SoundCue PickupWeapon => _pickupWeapon;
        public SoundCue PickupMapFragment => _pickupMapFragment;

        public SoundCue DoorOpen => _doorOpen;
        public SoundCue DoorClose => _doorClose;
        public SoundCue DoorUnlock => _doorUnlock;
        public SoundCue DoorLocked => _doorLocked;
        public SoundCue IceHit => NonEmpty(_iceHit) ?? _bulletImpact;
        public SoundCue IceBreak => NonEmpty(_iceBreak) ?? _doorUnlock;

        public SoundCue ZombieGroan => _zombieGroan;
        public SoundCue ZombieRoar => _zombieRoar;
        public SoundCue ZombieAttack => _zombieAttack;
        public SoundCue ZombieHurt => _zombieHurt;
        public SoundCue ZombieDeath => _zombieDeath;
        public SoundCue ZombieStepWalk => _zombieStepWalk;
        public SoundCue ZombieStepRun => _zombieStepRun;
        public float GroanIntervalMin => Mathf.Max(0.5f, Mathf.Min(_groanInterval.x, _groanInterval.y));
        public float GroanIntervalMax => Mathf.Max(GroanIntervalMin, Mathf.Max(_groanInterval.x, _groanInterval.y));
        public float ZombieWalkStep => _zombieWalkStep;
        public float ZombieRunStep => _zombieRunStep;
        public SoundCue ChaseStinger => _chaseStinger;
        public float StingerRearm => _stingerRearm;

        public SoundCue TorchLoop => _torchLoop;
        public int MaxTorchLoops => _maxTorchLoops;

        public SoundCue Click => _click;
        public SoundCue Back => _back;
        public SoundCue Denied => _denied;
        public SoundCue PauseOpen => _pauseOpen;
        public SoundCue PauseResume => _pauseResume;
        public SoundCue MapOpen => _mapOpen;
        public SoundCue MapClose => _mapClose;
        public SoundCue ExitReached => _exitReached;
        public SoundCue ConfirmExit => _confirmExit;
        public SoundCue LevelComplete => _levelComplete;
        public SoundCue LevelFailed => _levelFailed;
        public SoundCue Star => _star;
        public float ResultDelay => _resultDelay;
        public float StarInterval => _starInterval;
        public float StarPitchStep => _starPitchStep;

        /// <summary>The cues themselves (the builder fills them); <see cref="IceHit"/> / <see cref="IceBreak"/> fall back.</summary>
        internal SoundCue IceHitCue => _iceHit;
        internal SoundCue IceBreakCue => _iceBreak;

        internal List<WeaponSound> MutableWeaponSounds => _weaponSounds;
        internal List<ZombieVoice> MutableZombieVoices => _zombieVoices;

        /// <summary>The group's attack sound, or null when it has none (play the common one).</summary>
        public SoundCue WeaponAttack(string group) => NonEmpty(FindWeaponSound(group)?.Attack);

        /// <summary>The group's reload sound, or null when it has none (play the common one).</summary>
        public SoundCue WeaponReload(string group) => NonEmpty(FindWeaponSound(group)?.Reload);

        /// <summary>By name, not by definition (copies of one definition may live in several bundles).</summary>
        public WeaponSound FindWeaponSound(string group)
        {
            if (string.IsNullOrEmpty(group)) return null;
            foreach (var sound in _weaponSounds)
                if (sound != null && string.Equals(sound.Group, group, StringComparison.OrdinalIgnoreCase))
                    return sound;
            return null;
        }

        private static SoundCue NonEmpty(SoundCue cue) => cue == null || cue.IsEmpty ? null : cue;

        /// <summary>Voice pitch of a zombie type; 1 when it is not listed.</summary>
        public float ZombiePitch(string zombieId)
        {
            foreach (var voice in _zombieVoices)
                if (voice != null && string.Equals(voice.ZombieId, zombieId, StringComparison.Ordinal))
                    return voice.Pitch;
            return 1f;
        }

        /// <summary>Every sound cue (for checks and tools).</summary>
        public IEnumerable<SoundCue> AllCues()
        {
            yield return _footstep; yield return _footstepWalk; yield return _playerHurt; yield return _playerDeath;
            yield return _meleeSwing; yield return _shot; yield return _reloadMagazine;
            yield return _reloadSingle; yield return _switchWeapon; yield return _bulletImpact;
            foreach (var sound in _weaponSounds)
            {
                if (sound == null) continue;
                yield return sound.Attack;
                yield return sound.Reload;
            }
            yield return _pickupKey; yield return _pickupMedkit; yield return _pickupWeapon; yield return _pickupMapFragment;
            yield return _doorOpen; yield return _doorClose; yield return _doorUnlock; yield return _doorLocked;
            yield return _zombieGroan; yield return _zombieRoar; yield return _zombieAttack; yield return _zombieHurt;
            yield return _zombieDeath; yield return _zombieStepWalk; yield return _zombieStepRun; yield return _chaseStinger;
            yield return _torchLoop;
            yield return _click; yield return _back; yield return _denied; yield return _pauseOpen; yield return _pauseResume;
            yield return _mapOpen; yield return _mapClose; yield return _exitReached; yield return _confirmExit;
            yield return _levelComplete; yield return _levelFailed; yield return _star;
        }
    }
}
