using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Core.Audio
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
        [Tooltip("The zombie type (its stable id).")]
        [Content.ContentRef(typeof(Definitions.ZombieDefinition))]
        public string ZombieId;

        [Range(0.5f, 1.5f)] public float Pitch = 1f;
    }

    /// <summary>
    /// A shared sound of weapons (<c>Sounds/Weapon/_Parts/&lt;part&gt;/</c>): a reload step, a draw, a shot tail, shells,
    /// a hit. Groups refer to parts by name (<see cref="WeaponSoundPartAttribute"/>), so one recording serves many guns.
    /// </summary>
    [Serializable]
    public sealed class SoundPart
    {
        [SerializeField] private string _name;
        [SerializeField] private SoundCue _cue = new SoundCue(0.7f, 0f, 2, new Vector2(0.96f, 1.04f));

        public SoundPart(string name, SoundCue cue)
        {
            _name = name;
            _cue = cue ?? new SoundCue();
        }

        public string Name => _name;
        public SoundCue Cue => _cue;
    }

    /// <summary>One step of a reload heard by parts: a shared part at a share of the reload time.</summary>
    [Serializable]
    public struct ReloadStep
    {
        [WeaponSoundPart] public string Part;

        [Tooltip("When it plays: share of the reload time (1 = the step ends with the reload).")]
        [Range(0f, 1f)] public float At;

        public ReloadStep(string part, float at)
        {
            Part = part;
            At = at;
        }
    }

    /// <summary>
    /// Own sounds of a group of weapons (<c>WeaponDefinition.SoundGroup</c>): files of <c>Sounds/Weapon/&lt;group&gt;/</c>
    /// (attack*, start*, loop*, end*, reload*) and shared parts by name (reload steps, draw, tail, shells, hit…).
    /// An empty cue falls back to the common one (melee swing / shot, magazine / single reload).
    /// </summary>
    [Serializable]
    public sealed class WeaponSound
    {
        [Tooltip("Folder name under Sounds/Weapon; weapons pick it by their Sound field (or id).")]
        [SerializeField] private string _group;
        [Tooltip("Files attack*: the shot or swing.")]
        [SerializeField] private SoundCue _attack = new SoundCue(0.5f, 0f, 4, new Vector2(0.95f, 1.05f));
        [Tooltip("Files start*: continuous fire begins (minigun spin-up, flamethrower burst). With a loop only.")]
        [SerializeField] private SoundCue _start = new SoundCue(0.6f, 0f, 1, Vector2.one);
        [Tooltip("Files loop*: continuous fire (minigun, flamethrower, beam) — plays while the attack goes on, instead " +
                 "of the attack sound per shot.")]
        [SerializeField] private SoundCue _loop = new SoundCue(0.5f, 0f, 1, Vector2.one);
        [Tooltip("Files end*: continuous fire stops (cool-down).")]
        [SerializeField] private SoundCue _end = new SoundCue(0.6f, 0f, 1, Vector2.one);
        [Tooltip("Files reload*: the whole reload in one recording (used when there are no reload steps).")]
        [SerializeField] private SoundCue _reload = new SoundCue(0.7f, 0f, 1, Vector2.one);

        [Tooltip("Reload by parts (magazine out, in, bolt…): each step plays at its share of the reload time.")]
        [SerializeField] private List<ReloadStep> _reloadSteps = new List<ReloadStep>();
        [Tooltip("Taking the weapon in hands.")]
        [SerializeField, WeaponSoundPart] private string _draw;
        [Tooltip("Echo after a single shot or at the end of a burst (once, not per shot).")]
        [SerializeField, WeaponSoundPart] private string _tail;
        [Tooltip("After every shot: pump or bolt action.")]
        [SerializeField, WeaponSoundPart] private string _cycle;
        [Tooltip("Seconds after the shot before the cycle sound.")]
        [SerializeField, Min(0f)] private float _cycleDelay = 0.35f;
        [Tooltip("Spent shells falling (a moment after the shot).")]
        [SerializeField, WeaponSoundPart] private string _shells;
        [Tooltip("The weapon's hit on a monster (melee impact, energy, lightning); none = the common monster hit.")]
        [SerializeField, WeaponSoundPart] private string _hit;
        [Tooltip("Loop that follows the weapon's projectile while it flies (rocket).")]
        [SerializeField, WeaponSoundPart] private string _flight;

        public WeaponSound(string group, SoundCue attack, SoundCue reload)
        {
            _group = group;
            _attack = attack ?? new SoundCue();
            _reload = reload ?? new SoundCue();
        }

        public string Group => _group;
        public SoundCue Attack => _attack;
        public SoundCue Start => _start;
        public SoundCue Loop => _loop;
        public SoundCue End => _end;
        public SoundCue Reload => _reload;
        public IReadOnlyList<ReloadStep> ReloadSteps => _reloadSteps;
        public string Draw => _draw;
        public string Tail => _tail;
        public string Cycle => _cycle;
        public float CycleDelay => _cycleDelay;
        public string Shells => _shells;
        public string Hit => _hit;
        public string Flight => _flight;

        /// <summary>Continuous fire: a loop plays instead of a sound per attack.</summary>
        public bool IsContinuous => _loop != null && !_loop.IsEmpty;

        internal List<ReloadStep> MutableReloadSteps => _reloadSteps;

        /// <summary>Tools: the shared parts of the group.</summary>
        internal void SetParts(string draw, string tail, string cycle, string shells, string hit, string flight)
        {
            _draw = draw;
            _tail = tail;
            _cycle = cycle;
            _shells = shells;
            _hit = hit;
            _flight = flight;
        }

        internal void SetCycleDelay(float delay) => _cycleDelay = delay;
    }

    /// <summary>
    /// Every sound and music track of the game (ТЗ §71), Addressable at <see cref="Address"/> and resident for the
    /// application lifetime (music clips are loaded only while they play). Display only: gameplay never reads it.
    /// Filled by Purge → Dev → Build Audio, which keeps the tuning (volumes, ranges…) on rebuild.
    /// </summary>
    [CreateAssetMenu(fileName = "AudioCatalog", menuName = "Purge/Audio Catalog")]
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
        [Tooltip("The body falls after the death cry.")]
        [SerializeField] private SoundCue _bodyfall = new SoundCue(0.8f, 0f, 1, new Vector2(0.95f, 1.05f));
        [Tooltip("Seconds after the death before the body falls.")]
        [SerializeField, Min(0f)] private float _bodyfallDelay = 0.6f;
        [Tooltip("Loop while the player's health is low.")]
        [SerializeField] private SoundCue _heartbeat = new SoundCue(0.6f, 0f, 1, Vector2.one);
        [Tooltip("Share of the health at or below which the heartbeat plays.")]
        [SerializeField, Range(0f, 1f)] private float _heartbeatShare = 0.25f;
        [Tooltip("The run's level-up (stage 6e).")]
        [SerializeField] private SoundCue _levelUp = new SoundCue(0.7f, 0f, 1, Vector2.one, 0.3f);
        [Tooltip("The player revives (stage 6e).")]
        [SerializeField] private SoundCue _revive = new SoundCue(0.9f, 0f, 1, Vector2.one);
        [Tooltip("A kill's coin reaches the HUD's money (stage 6e). Very often: short, soft, spaced out.")]
        [SerializeField] private SoundCue _coin = new SoundCue(0.25f, 0f, 2, new Vector2(0.95f, 1.1f), 0.07f);

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
        [Tooltip("Attack with an empty magazine and no reserve.")]
        [SerializeField] private SoundCue _emptyClick = new SoundCue(0.7f, 0f, 1, new Vector2(0.97f, 1.03f), 0.12f);
        [Tooltip("Each shot while the magazine is low (Low Ammo Share).")]
        [SerializeField] private SoundCue _lowAmmo = new SoundCue(0.35f, 0f, 1, Vector2.one, 0.2f);
        [Tooltip("Share of the magazine left at which the low-ammo tick starts (magazines of 4 and more).")]
        [SerializeField, Range(0f, 0.5f)] private float _lowAmmoShare = 0.25f;
        [Tooltip("Shared weapon sounds (Sounds/Weapon/_Parts/<part>/): reload steps, draws, tails, shells, hits.")]
        [SerializeField] private List<SoundPart> _parts = new List<SoundPart>();

        [Header("Hits and kills")]
        [Tooltip("The player's weapon hits a monster (a weapon with its own Hit part plays that instead).")]
        [SerializeField] private SoundCue _monsterHit = new SoundCue(0.45f, 12f, 4, new Vector2(0.9f, 1.1f), 0.04f);
        [Tooltip("A monster dies: a cartoon pop.")]
        [SerializeField] private SoundCue _killPop = new SoundCue(0.55f, 14f, 4, new Vector2(0.9f, 1.15f), 0.03f);
        [Tooltip("A monster dies: the splash of its colour, with the pop.")]
        [SerializeField] private SoundCue _killSplat = new SoundCue(0.4f, 12f, 3, new Vector2(0.9f, 1.1f), 0.05f);

        [Header("Throwables (empty: draft stand-ins until stage 4в)")]
        [Tooltip("A throw leaving the hand (empty: the melee swing).")]
        [SerializeField] private SoundCue _throw = new SoundCue(0.6f, 0f, 2, new Vector2(0.95f, 1.1f));
        [Tooltip("A blast of a medium radius (empty: the shot).")]
        [UnityEngine.Serialization.FormerlySerializedAs("_explosion")]
        [SerializeField] private SoundCue _explosionMedium = new SoundCue(1f, 25f, 4, new Vector2(0.9f, 1.05f));
        [Tooltip("A blast up to Small Radius (empty: the medium one).")]
        [SerializeField] private SoundCue _explosionSmall = new SoundCue(0.9f, 20f, 4, new Vector2(0.92f, 1.06f));
        [Tooltip("A blast from Large Radius on (empty: the medium one).")]
        [SerializeField] private SoundCue _explosionLarge = new SoundCue(1f, 30f, 3, new Vector2(0.92f, 1.04f));
        [Tooltip("Cells: a blast of at most this radius is small.")]
        [SerializeField, Min(0f)] private float _smallRadius = 1.5f;
        [Tooltip("Cells: a blast of at least this radius is large.")]
        [SerializeField, Min(0f)] private float _largeRadius = 2.8f;
        [Tooltip("The pin pulled as the throw leaves the hand.")]
        [SerializeField] private SoundCue _pin = new SoundCue(0.5f, 0f, 1, new Vector2(0.95f, 1.05f));
        [Tooltip("A throwable lands (before its effect).")]
        [SerializeField] private SoundCue _land = new SoundCue(0.6f, 12f, 3, new Vector2(0.92f, 1.08f));
        [Tooltip("Loop of a burning zone: the nearest burning cell is heard.")]
        [SerializeField] private SoundCue _fireLoop = new SoundCue(0.6f, 9f, 1, Vector2.one);
        [Tooltip("A zone catching on the ground: a molotov breaking, fire (empty: the bullet impact).")]
        [SerializeField] private SoundCue _ignite = new SoundCue(0.8f, 15f, 3, new Vector2(0.9f, 1.1f));

        [Header("Pickups")]
        [SerializeField] private SoundCue _pickupKey = new SoundCue(0.45f, 0f, 1, Vector2.one);
        [SerializeField] private SoundCue _pickupMedkit = new SoundCue(0.9f, 0f, 1, Vector2.one);
        [SerializeField] private SoundCue _pickupWeapon = new SoundCue(0.5f, 0f, 1, Vector2.one);
        [Tooltip("Ammo picked up (empty: the weapon pickup). Often: keep it short and soft.")]
        [SerializeField] private SoundCue _pickupAmmo = new SoundCue(0.4f, 0f, 2, new Vector2(0.95f, 1.08f), 0.05f);
        [Tooltip("A throwable picked up (empty: the weapon pickup).")]
        [SerializeField] private SoundCue _pickupThrowable = new SoundCue(0.5f, 0f, 1, Vector2.one);
        [Tooltip("The player drops a weapon.")]
        [SerializeField] private SoundCue _weaponDrop = new SoundCue(0.6f, 0f, 1, new Vector2(0.95f, 1.05f));
        [Tooltip("A rare (or better) item drops from a monster.")]
        [SerializeField] private SoundCue _rareDrop = new SoundCue(0.6f, 0f, 1, Vector2.one, 0.5f);
        [SerializeField] private Content.Rarity _rareDropFrom = Content.Rarity.Rare;

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

        [Header("Monsters (stage 6e2)")]
        [Tooltip("Sounds of abilities by name: a folder Sounds/Monster/<name> each (abilities name them in Cast/Release Sound).")]
        [SerializeField] private List<SoundPart> _monsterSounds = new List<SoundPart>();
        [Tooltip("A caller monster calls the horde (Monster/call*).")]
        [SerializeField] private SoundCue _monsterCall = new SoundCue(0.7f, 16f, 2, new Vector2(0.95f, 1.05f), 0.3f);
        [Tooltip("A hit rings off a monster's armored front (Monster/armor_hit*).")]
        [SerializeField] private SoundCue _armorHit = new SoundCue(0.45f, 12f, 3, new Vector2(0.9f, 1.1f), 0.06f);
        [Tooltip("A champion appears (Monster/champion*).")]
        [SerializeField] private SoundCue _championAppear = new SoundCue(0.7f, 16f, 2, new Vector2(0.95f, 1.05f), 0.2f);
        [Tooltip("A monster strikes a closed door (Other/door_hit*).")]
        [SerializeField] private SoundCue _doorHit = new SoundCue(0.7f, 12f, 2, new Vector2(0.9f, 1.1f), 0.1f);
        [Tooltip("A monster breaks a door (Other/door_broken*).")]
        [SerializeField] private SoundCue _doorBroken = new SoundCue(0.9f, 14f, 1, new Vector2(0.95f, 1.05f));
        [Tooltip("A chest opens (Other/chest_open*, stage 7в).")]
        [SerializeField] private SoundCue _chestOpen = new SoundCue(0.8f, 12f, 1, new Vector2(0.95f, 1.05f));
        [Tooltip("A locked chest without its key (Other/chest_locked*).")]
        [SerializeField] private SoundCue _chestLocked = new SoundCue(0.7f, 8f, 1, new Vector2(0.95f, 1.05f), 0.3f);
        [Tooltip("Loop of the safe dome while it stands (Other/dome_loop*), heard near it.")]
        [SerializeField] private SoundCue _domeLoop = new SoundCue(0.35f, 8f, 1, Vector2.one);

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
        [SerializeField] private SoundCue _levelComplete = new SoundCue(1f, 0f, 1, Vector2.one);
        [Tooltip("A wave is cleared, the pause between waves starts (stage 6b).")]
        [SerializeField] private SoundCue _waveCleared = new SoundCue(0.8f, 0f, 1, Vector2.one);
        [SerializeField] private SoundCue _levelFailed = new SoundCue(0.8f, 0f, 1, Vector2.one);
        [Tooltip("Each star of the result, one after another, pitch rising.")]
        [SerializeField] private SoundCue _star = new SoundCue(0.8f, 0f, 3, Vector2.one);
        [Tooltip("Cards of a level-up appear (UI/cards_show*).")]
        [SerializeField] private SoundCue _cardsShown = new SoundCue(0.7f, 0f, 1, Vector2.one);
        [Tooltip("A card is chosen (UI/card_pick*).")]
        [SerializeField] private SoundCue _cardChosen = new SoundCue(0.7f, 0f, 1, Vector2.one);
        [Tooltip("The cards are rerolled (UI/card_reroll*).")]
        [SerializeField] private SoundCue _cardReroll = new SoundCue(0.6f, 0f, 1, new Vector2(1.05f, 1.1f));
        [Tooltip("A confirmation window opens (UI/window_exit_level).")]
        [SerializeField] private SoundCue _confirmOpen = new SoundCue(0.6f, 0f, 1, Vector2.one);
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
        public SoundCue Bodyfall => _bodyfall;
        public float BodyfallDelay => _bodyfallDelay;
        public SoundCue Heartbeat => _heartbeat;
        public float HeartbeatShare => _heartbeatShare;
        public SoundCue LevelUp => _levelUp;
        public SoundCue Revive => _revive;
        public SoundCue Coin => _coin;

        public SoundCue MeleeSwing => _meleeSwing;
        public SoundCue Shot => _shot;
        public IReadOnlyList<WeaponSound> WeaponSounds => _weaponSounds;
        public SoundCue ReloadMagazine => _reloadMagazine;
        public SoundCue ReloadSingle => _reloadSingle;
        public SoundCue SwitchWeapon => _switchWeapon;
        public SoundCue BulletImpact => _bulletImpact;
        public SoundCue EmptyClick => _emptyClick;
        public SoundCue LowAmmo => _lowAmmo;
        public float LowAmmoShare => _lowAmmoShare;
        public IReadOnlyList<SoundPart> Parts => _parts;
        public SoundCue MonsterHit => _monsterHit;
        public SoundCue KillPop => _killPop;
        public SoundCue KillSplat => _killSplat;
        public SoundCue WeaponDrop => _weaponDrop;
        public SoundCue RareDrop => _rareDrop;
        public Content.Rarity RareDropFrom => _rareDropFrom;
        public SoundCue Pin => _pin;
        public SoundCue Land => _land;
        public SoundCue FireLoop => _fireLoop;

        public SoundCue PickupKey => _pickupKey;
        public SoundCue PickupMedkit => _pickupMedkit;
        public SoundCue PickupWeapon => _pickupWeapon;

        public SoundCue DoorOpen => _doorOpen;
        public SoundCue DoorClose => _doorClose;
        public SoundCue DoorUnlock => _doorUnlock;
        public SoundCue DoorLocked => _doorLocked;
        public SoundCue Throw => NonEmpty(_throw) ?? _meleeSwing;
        public SoundCue Explosion => NonEmpty(_explosionMedium) ?? _shot;

        /// <summary>The blast sound for a radius: small, medium or large (missing ones fall back to the medium).</summary>
        public SoundCue ExplosionOf(float radius) =>
            radius <= _smallRadius ? NonEmpty(_explosionSmall) ?? Explosion
            : radius >= _largeRadius ? NonEmpty(_explosionLarge) ?? Explosion
            : Explosion;
        public SoundCue Ignite => NonEmpty(_ignite) ?? _bulletImpact;
        public SoundCue PickupAmmo => NonEmpty(_pickupAmmo) ?? _pickupWeapon;
        public SoundCue PickupThrowable => NonEmpty(_pickupThrowable) ?? _pickupWeapon;
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
        public SoundCue LevelComplete => _levelComplete;
        public SoundCue WaveCleared => _waveCleared;
        public SoundCue LevelFailed => _levelFailed;
        public SoundCue Star => _star;
        public float ResultDelay => _resultDelay;
        public float StarInterval => _starInterval;
        public float StarPitchStep => _starPitchStep;

        /// <summary>The cues themselves (the builder fills them); <see cref="IceHit"/> / <see cref="IceBreak"/> fall back.</summary>
        internal SoundCue IceHitCue => _iceHit;
        internal SoundCue ThrowCue => _throw;
        internal SoundCue ExplosionCue => _explosionMedium;
        internal SoundCue ExplosionSmallCue => _explosionSmall;
        internal SoundCue ExplosionLargeCue => _explosionLarge;
        internal SoundCue PinCue => _pin;
        internal SoundCue LandCue => _land;
        internal SoundCue FireLoopCue => _fireLoop;
        internal SoundCue EmptyClickCue => _emptyClick;
        internal SoundCue LowAmmoCue => _lowAmmo;
        internal SoundCue MonsterHitCue => _monsterHit;
        internal SoundCue KillPopCue => _killPop;
        internal SoundCue KillSplatCue => _killSplat;
        internal SoundCue WeaponDropCue => _weaponDrop;
        internal SoundCue RareDropCue => _rareDrop;
        internal SoundCue BodyfallCue => _bodyfall;
        internal SoundCue HeartbeatCue => _heartbeat;
        internal SoundCue IgniteCue => _ignite;
        internal SoundCue PickupAmmoCue => _pickupAmmo;
        internal SoundCue PickupThrowableCue => _pickupThrowable;
        internal SoundCue IceBreakCue => _iceBreak;
        internal SoundCue WaveClearedCue => _waveCleared;
        internal SoundCue LevelUpCue => _levelUp;
        internal SoundCue ReviveCue => _revive;
        internal SoundCue CoinCue => _coin;

        internal List<WeaponSound> MutableWeaponSounds => _weaponSounds;
        internal List<ZombieVoice> MutableZombieVoices => _zombieVoices;
        internal List<SoundPart> MutableParts => _parts;
        internal List<SoundPart> MutableMonsterSounds => _monsterSounds;
        internal SoundCue MonsterCallCue => _monsterCall;
        internal SoundCue ArmorHitCue => _armorHit;
        internal SoundCue ChampionAppearCue => _championAppear;
        internal SoundCue DoorHitCue => _doorHit;
        internal SoundCue DoorBrokenCue => _doorBroken;
        internal SoundCue ChestOpenCue => _chestOpen;
        internal SoundCue ChestLockedCue => _chestLocked;
        internal SoundCue DomeLoopCue => _domeLoop;
        internal SoundCue CardsShownCue => _cardsShown;
        internal SoundCue CardChosenCue => _cardChosen;
        internal SoundCue CardRerollCue => _cardReroll;
        internal SoundCue ConfirmOpenCue => _confirmOpen;

        public IReadOnlyList<SoundPart> MonsterSounds => _monsterSounds;
        public SoundCue MonsterCall => _monsterCall;
        public SoundCue ArmorHit => _armorHit;
        public SoundCue ChampionAppear => _championAppear;
        public SoundCue DoorHit => _doorHit;
        public SoundCue DoorBroken => _doorBroken;
        public SoundCue ChestOpen => _chestOpen;
        public SoundCue ChestLocked => _chestLocked;
        public SoundCue DomeLoop => _domeLoop;
        public SoundCue CardsShown => _cardsShown;
        public SoundCue CardChosen => _cardChosen;
        public SoundCue CardReroll => _cardReroll;
        public SoundCue ConfirmOpen => _confirmOpen;

        /// <summary>A monster sound by name (<see cref="MonsterSoundAttribute"/>), or null when there is none.</summary>
        public SoundCue MonsterSound(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var sound in _monsterSounds)
                if (sound != null && string.Equals(sound.Name, name, StringComparison.OrdinalIgnoreCase))
                    return NonEmpty(sound.Cue);
            return null;
        }

        /// <summary>A shared part's sound, or null when there is none (or it has no clips).</summary>
        public SoundCue Part(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var part in _parts)
                if (part != null && string.Equals(part.Name, name, StringComparison.OrdinalIgnoreCase))
                    return NonEmpty(part.Cue);
            return null;
        }

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
                yield return sound.Start;
                yield return sound.Loop;
                yield return sound.End;
                yield return sound.Reload;
            }
            foreach (var part in _parts)
                if (part != null)
                    yield return part.Cue;
            yield return _emptyClick; yield return _lowAmmo; yield return _monsterHit; yield return _killPop;
            yield return _killSplat; yield return _weaponDrop; yield return _rareDrop; yield return _pin; yield return _land;
            yield return _fireLoop; yield return _bodyfall; yield return _heartbeat;
            yield return _levelUp; yield return _revive; yield return _coin;
            yield return _throw; yield return _explosionSmall; yield return _explosionMedium; yield return _explosionLarge;
            yield return _ignite; yield return _pickupAmmo; yield return _pickupThrowable; yield return _iceHit;
            yield return _iceBreak;
            yield return _pickupKey; yield return _pickupMedkit; yield return _pickupWeapon;
            yield return _doorOpen; yield return _doorClose; yield return _doorUnlock; yield return _doorLocked;
            yield return _zombieGroan; yield return _zombieRoar; yield return _zombieAttack; yield return _zombieHurt;
            yield return _zombieDeath; yield return _zombieStepWalk; yield return _zombieStepRun; yield return _chaseStinger;
            yield return _torchLoop;
            yield return _click; yield return _back; yield return _denied; yield return _pauseOpen; yield return _pauseResume;
            yield return _mapOpen; yield return _mapClose;
            yield return _levelComplete; yield return _levelFailed; yield return _star; yield return _waveCleared;
            foreach (var sound in _monsterSounds)
                if (sound != null)
                    yield return sound.Cue;
            yield return _monsterCall; yield return _armorHit; yield return _championAppear;
            yield return _doorHit; yield return _doorBroken;
            yield return _chestOpen; yield return _chestLocked; yield return _domeLoop;
            yield return _cardsShown; yield return _cardChosen; yield return _cardReroll; yield return _confirmOpen;
        }
    }
}
