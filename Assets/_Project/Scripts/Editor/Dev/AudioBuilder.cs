using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Purge.Core.Audio;
using Purge.Core.Definitions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Audio: import settings of the sounds (<c>Sounds/</c>) and music (<c>Music/</c>), and the
    /// <see cref="AudioCatalog"/> filled from file names (a cue gets every file matching its pattern, in name order).
    /// Weapon sound groups come from the folders under Sounds/Weapon (<see cref="FillWeaponSounds"/>).
    /// Rerun after adding or renaming sound files. Tuning in the catalog (volumes, ranges, pitch…) is kept; only the
    /// clips are replaced. The catalog and the music are made Addressable (group Purge Shared).
    /// </summary>
    internal static class AudioBuilder
    {
        private const string SoundsFolder = "Assets/_Project/Sounds";
        private const string MusicFolder = "Assets/_Project/Music";
        private const string CatalogFolder = "Assets/_Project/Data/Audio";
        private const string CatalogPath = CatalogFolder + "/AudioCatalog.asset";

        /// <summary>Folders right under it are weapon sound groups (see <see cref="FillWeaponSounds"/>).</summary>
        internal const string WeaponGroupsFolder = SoundsFolder + "/Weapon";

        /// <summary>Folders right under it are shared weapon sounds (<see cref="SoundPart"/>), any file names.</summary>
        internal const string PartsFolderName = "_Parts";
        internal const string WeaponPartsFolder = WeaponGroupsFolder + "/" + PartsFolderName;

        private static readonly Regex AttackFile = new Regex(@"^attack\d*$", RegexOptions.IgnoreCase);
        private static readonly Regex StartFile = new Regex(@"^start\d*$", RegexOptions.IgnoreCase);
        private static readonly Regex LoopFile = new Regex(@"^loop\d*$", RegexOptions.IgnoreCase);
        private static readonly Regex EndFile = new Regex(@"^end\d*$", RegexOptions.IgnoreCase);
        private static readonly Regex ReloadFile = new Regex(@"^reload\d*$", RegexOptions.IgnoreCase);

        /// <summary>Clips up to this long are decompressed on load (cheap to play often); longer ones stay compressed.</summary>
        private const float DecompressMaxLength = 3f;

        /// <summary>(folder under Sounds, file name pattern, cue).</summary>
        private static readonly (string Folder, string Pattern, Func<AudioCatalog, SoundCue> Cue)[] Cues =
        {
            ("Player", @"^Wood Run \d+$", c => c.Footstep),
            ("Player", @"^Wood Walk \d+$", c => c.FootstepWalk),
            ("Player", @"^hit\d*$", c => c.PlayerHurt),
            ("Player", @"^death\d*$", c => c.PlayerDeath),
            ("Player", @"^bodyfall\d*$", c => c.BodyfallCue),
            ("Player", @"^heartbeat_loop\d*$", c => c.HeartbeatCue),

            ("Weapon", @"^melee-attack\d*$", c => c.MeleeSwing),
            ("Weapon", @"^rifle_attack\d*$", c => c.Shot),
            ("Weapon", @"^reload$", c => c.ReloadMagazine),
            ("Weapon", @"^rifle_one_reload$", c => c.ReloadSingle),
            ("Weapon", @"^bullet\d*$", c => c.BulletImpact),

            ("Other", @"^switch_weapon$", c => c.SwitchWeapon),
            ("Other", @"^key_pick_up\d*$", c => c.PickupKey),
            ("Other", @"^medicine_pick_up\d*$", c => c.PickupMedkit),
            ("Other", @"^weapon_pick_up\d*$", c => c.PickupWeapon),
            ("Other", @"^Door Open \d+$", c => c.DoorOpen),
            ("Other", @"^Door Close \d+$", c => c.DoorClose),
            ("Other", @"^door_unlock$", c => c.DoorUnlock),
            ("Other", @"^door_lock$", c => c.DoorLocked),
            ("Other", @"^ice_hit\d*$", c => c.IceHitCue),
            ("Weapon", @"^throw\d*$", c => c.ThrowCue),
            ("Weapon", @"^pin\d*$", c => c.PinCue),
            ("Weapon", @"^land\d*$", c => c.LandCue),
            ("Weapon", @"^explosion_small\d*$", c => c.ExplosionSmallCue),
            ("Weapon", @"^explosion_medium\d*$", c => c.ExplosionCue),
            ("Weapon", @"^explosion_large\d*$", c => c.ExplosionLargeCue),
            ("Weapon", @"^ignite\d*$", c => c.IgniteCue),
            ("Weapon", @"^empty_click\d*$", c => c.EmptyClickCue),
            ("Weapon", @"^low_ammo\d*$", c => c.LowAmmoCue),
            ("Combat", @"^monster_hit\d*$", c => c.MonsterHitCue),
            ("Combat", @"^kill_pop\d*$", c => c.KillPopCue),
            ("Combat", @"^kill_splat\d*$", c => c.KillSplatCue),
            ("Other", @"^ammo_pick_up\d*$", c => c.PickupAmmoCue),
            ("Other", @"^throwable_pick_up\d*$", c => c.PickupThrowableCue),
            ("Other", @"^weapon_drop\d*$", c => c.WeaponDropCue),
            ("Other", @"^rare_drop\d*$", c => c.RareDropCue),
            ("Other", @"^fire_loop\d*$", c => c.FireLoopCue),
            ("Other", @"^ice_break\d*$", c => c.IceBreakCue),
            ("Other", @"^detect$", c => c.ChaseStinger),
            ("Other", @"^light_loop$", c => c.TorchLoop),
            ("Player", @"^win$", c => c.WaveClearedCue),

            ("Zombie", @"^zombie-idle\d*$", c => c.ZombieGroan),
            ("Zombie", @"^zombie-scream\d*$", c => c.ZombieRoar),
            ("Zombie", @"^zombie-attack\d*$", c => c.ZombieAttack),
            ("Zombie", @"^zombie-hit\d*$", c => c.ZombieHurt),
            ("Zombie", @"^zombie-death\d*$", c => c.ZombieDeath),
            ("Zombie", @"^Dirt Walk \d+$", c => c.ZombieStepWalk),
            ("Zombie", @"^Dirt Run \d+$", c => c.ZombieStepRun),

            ("UI", @"^click\d*$", c => c.Click),
            ("UI", @"^cancel$", c => c.Back),
            ("UI", @"^level_closed$", c => c.Denied),
            ("UI", @"^pause_start$", c => c.PauseOpen),
            ("UI", @"^pause_continue$", c => c.PauseResume),
            ("UI", @"^map_open$", c => c.MapOpen),
            ("UI", @"^map_close$", c => c.MapClose),
            ("UI", @"^game_win$", c => c.LevelComplete),
            ("UI", @"^game_over$", c => c.LevelFailed),
            ("UI", @"^star\d*$", c => c.Star),
        };

        /// <summary>(file name, track, volume and loop overlap of a newly set track). The level track fades out at its end.</summary>
        private static readonly (string Name, Func<AudioCatalog, MusicTrack> Track, float Volume, float Overlap)[] Music =
        {
            ("menu", c => c.MenuMusic, 0.6f, 0f),
            ("game", c => c.LevelMusic, 0.35f, 2.5f),
        };

        /// <summary>Default voice pitch per zombie type (set when the list is empty).</summary>
        private static readonly (string Id, float Pitch)[] ZombieVoices =
        {
            ("zombie_walker", 1f),
            ("zombie_listener", 1.12f),
            ("zombie_hunter", 0.88f),
            ("forest_zombie_walker", 1f),
            ("forest_zombie_listener", 1.12f),
            ("forest_zombie_hunter", 0.88f),
            ("snow_zombie_walker", 1f),
            ("snow_zombie_listener", 1.12f),
            ("snow_zombie_hunter", 0.88f),
        };

        [MenuItem("Purge/Dev/Build Audio")]
        public static void Build()
        {
            var sounds = Clips(SoundsFolder);
            var music = Clips(MusicFolder);
            var reimported = 0;
            foreach (var path in sounds) reimported += ConfigureSound(path) ? 1 : 0;
            foreach (var path in music) reimported += ConfigureMusic(path) ? 1 : 0;

            var catalog = LoadOrCreateCatalog();
            var used = new HashSet<string>();
            var empty = new List<string>();
            foreach (var (folder, pattern, cue) in Cues)
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                var matches = sounds
                    .Where(p => string.Equals(Path.GetFileName(Path.GetDirectoryName(p)), folder, StringComparison.OrdinalIgnoreCase))
                    .Where(p => regex.IsMatch(Path.GetFileNameWithoutExtension(p)))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                used.UnionWith(matches);
                cue(catalog).Clips = matches.Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
                if (matches.Count == 0) empty.Add($"{folder}/{pattern}");
            }

            FillParts(catalog, sounds, used);
            FillWeaponSounds(catalog, sounds, used);

            foreach (var (name, track, volume, overlap) in Music)
            {
                var path = music.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase));
                var target = track(catalog);
                if (path == null)
                {
                    empty.Add($"Music/{name}");
                    continue;
                }

                var guid = AssetDatabase.AssetPathToGUID(path);
                if (!target.IsSet)
                {
                    target.Volume = volume;
                    target.LoopOverlap = overlap;
                }

                if (target.Clip.AssetGUID != guid)
                    target.Clip = new AssetReferenceT<AudioClip>(guid);
            }

            if (catalog.MutableZombieVoices.Count == 0)
                foreach (var (id, pitch) in ZombieVoices)
                    catalog.MutableZombieVoices.Add(new ZombieVoice { ZombieId = id, Pitch = pitch });

            EditorUtility.SetDirty(catalog);
            var problem = LevelDesigner.LevelSync.SyncShared(AddressableAssetSettingsDefaultObject.GetSettings(true));
            AssetDatabase.SaveAssets();

            var unused = sounds.Where(p => !used.Contains(p)).Select(p => p.Substring(SoundsFolder.Length + 1)).ToList();
            if (empty.Count > 0) Debug.LogWarning("[Purge] Build Audio: no files for " + string.Join(", ", empty));
            if (unused.Count > 0) Debug.LogWarning("[Purge] Build Audio: files not used by any sound: " + string.Join(", ", unused));
            if (problem != null) Debug.LogWarning("[Purge] " + problem);
            Debug.Log($"[Purge] Audio built: {CatalogPath}, {sounds.Count} sound file(s), {music.Count} music file(s), " +
                      $"{reimported} reimported.");
        }

        /// <summary>Names of the weapon sound group folders (right under Sounds/Weapon, without the parts), sorted.</summary>
        internal static List<string> WeaponGroups() =>
            SubFolders(WeaponGroupsFolder).Where(name => !string.Equals(name, PartsFolderName, StringComparison.OrdinalIgnoreCase)).ToList();

        /// <summary>Names of the shared weapon sound folders (Sounds/Weapon/_Parts/&lt;part&gt;), sorted.</summary>
        internal static List<string> WeaponParts() => SubFolders(WeaponPartsFolder);

        private static List<string> SubFolders(string folder) =>
            AssetDatabase.IsValidFolder(folder)
                ? AssetDatabase.GetSubFolders(folder)
                    .Select(path => path.Substring(path.LastIndexOf('/') + 1))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

        /// <summary>
        /// Shared weapon sounds: every folder under <see cref="WeaponPartsFolder"/> is a part (its name), all its files are
        /// variants. A part keeps its tuning; parts whose folder is gone are removed.
        /// </summary>
        private static void FillParts(AudioCatalog catalog, List<string> sounds, HashSet<string> used)
        {
            var folders = sounds
                .Where(p => string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(p))?.Replace('\\', '/'),
                    WeaponPartsFolder, StringComparison.OrdinalIgnoreCase))
                .GroupBy(p => Path.GetFileName(Path.GetDirectoryName(p)), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            var parts = new List<SoundPart>();
            foreach (var folder in folders)
            {
                var part = catalog.MutableParts.FirstOrDefault(p => p != null &&
                               string.Equals(p.Name, folder.Key, StringComparison.OrdinalIgnoreCase))
                           ?? new SoundPart(folder.Key, DefaultPartCue(folder.Key));
                var files = folder.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                part.Cue.Clips = files.Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
                used.UnionWith(files);
                parts.Add(part);
            }

            catalog.MutableParts.Clear();
            catalog.MutableParts.AddRange(parts);
        }

        /// <summary>Tuning of a new part by its kind (name prefix): tails and shells are quiet and limited.</summary>
        private static SoundCue DefaultPartCue(string name)
        {
            if (name.StartsWith("tail", StringComparison.OrdinalIgnoreCase))
                return new SoundCue(0.35f, 0f, 2, new Vector2(0.95f, 1.05f), 0.25f);
            if (name.StartsWith("shells", StringComparison.OrdinalIgnoreCase))
                return new SoundCue(0.25f, 0f, 2, new Vector2(0.9f, 1.1f), 0.15f);
            if (name.StartsWith("hit", StringComparison.OrdinalIgnoreCase))
                return new SoundCue(0.6f, 12f, 3, new Vector2(0.92f, 1.08f), 0.04f);
            if (name.EndsWith("flight", StringComparison.OrdinalIgnoreCase))
                return new SoundCue(0.5f, 14f, 2, Vector2.one);
            return new SoundCue(0.7f, 0f, 2, new Vector2(0.97f, 1.03f));
        }

        /// <summary>
        /// Sound groups of weapons: every folder right under <see cref="WeaponGroupsFolder"/> (but the parts) is a group
        /// (its name), files <c>attack*</c> its shot or swing, <c>start*/loop*/end*</c> continuous fire, <c>reload*</c>
        /// a whole reload. An existing group keeps its tuning and parts; a new one starts from the common sounds and
        /// the parts its weapons' kind suggests (<see cref="DefaultParts"/>). Groups whose folder is gone are removed.
        /// </summary>
        private static void FillWeaponSounds(AudioCatalog catalog, List<string> sounds, HashSet<string> used)
        {
            var definitions = AssetDatabase.FindAssets("t:WeaponDefinition")
                .Select(guid => AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(definition => definition != null)
                .ToList();

            var folders = sounds
                .Where(p => string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(p))?.Replace('\\', '/'),
                    WeaponGroupsFolder, StringComparison.OrdinalIgnoreCase))
                .GroupBy(p => Path.GetFileName(Path.GetDirectoryName(p)), StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.Equals(g.Key, PartsFolderName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            var groups = new List<WeaponSound>();
            foreach (var folder in folders)
            {
                var name = folder.Key;
                var users = definitions.Where(d => string.Equals(d.SoundGroup, name, StringComparison.OrdinalIgnoreCase)).ToList();
                var sound = catalog.FindWeaponSound(name);
                if (sound == null)
                {
                    var melee = users.Count > 0 && users.All(d => d.IsMelee);
                    var automatic = users.Count > 0 && users.All(d => d.FireMode == FireMode.Automatic);
                    var attackTuning = (melee ? catalog.MeleeSwing : catalog.Shot).CopyTuning();
                    if (!melee && !automatic) attackTuning.Volume = 0.8f; // Hand-operated guns: one shot sounds big.
                    if (users.Count > 0 && users.All(d => d.Attack is StreamAttack))
                        attackTuning.MinInterval = 0.3f; // A stream attacks 10 times a second.
                    sound = new WeaponSound(name, attackTuning,
                        (automatic ? catalog.ReloadMagazine : catalog.ReloadSingle).CopyTuning());
                    if (users.Count > 0) DefaultParts(sound, users[0]);
                }

                sound.Attack.Clips = Files(folder, AttackFile, used);
                sound.Start.Clips = Files(folder, StartFile, used);
                sound.Loop.Clips = Files(folder, LoopFile, used);
                sound.End.Clips = Files(folder, EndFile, used);
                sound.Reload.Clips = Files(folder, ReloadFile, used);
                groups.Add(sound);

                if (users.Count == 0)
                    Debug.LogWarning($"[Purge] Build Audio: no weapon uses the sound group '{name}' (set it in the Sound field " +
                                     "of a weapon definition, or name the folder by a weapon id).");
                foreach (var step in sound.ReloadSteps)
                    WarnMissingPart(catalog, name, step.Part);
                WarnMissingPart(catalog, name, sound.Draw);
                WarnMissingPart(catalog, name, sound.Tail);
                WarnMissingPart(catalog, name, sound.Cycle);
                WarnMissingPart(catalog, name, sound.Shells);
                WarnMissingPart(catalog, name, sound.Hit);
                WarnMissingPart(catalog, name, sound.Flight);
            }

            foreach (var definition in definitions)
                if (!string.IsNullOrEmpty(definition.Sound) && groups.All(g => !string.Equals(g.Group, definition.Sound, StringComparison.OrdinalIgnoreCase)))
                    Debug.LogWarning($"[Purge] Build Audio: weapon '{definition.name}' uses the sound group '{definition.Sound}', " +
                                     $"but there is no folder {WeaponGroupsFolder}/{definition.Sound}: it plays the common sounds.");

            catalog.MutableWeaponSounds.Clear();
            catalog.MutableWeaponSounds.AddRange(groups);
        }

        private static AudioClip[] Files(IEnumerable<string> folder, Regex pattern, HashSet<string> used)
        {
            var files = folder.Where(p => pattern.IsMatch(Path.GetFileNameWithoutExtension(p)))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            used.UnionWith(files);
            return files.Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
        }

        private static void WarnMissingPart(AudioCatalog catalog, string group, string part)
        {
            if (!string.IsNullOrEmpty(part) && catalog.Part(part) == null)
                Debug.LogWarning($"[Purge] Build Audio: the sound group '{group}' uses the part '{part}', but there is no " +
                                 $"folder {WeaponPartsFolder}/{part} with sounds.");
        }

        /// <summary>
        /// Parts of a new group by the kind of its first weapon (how it is held, its attack and ammo type): reload steps,
        /// draw, tail, shells, hit. A first guess, kept and tuned in the catalog afterwards; parts missing in
        /// Sounds/Weapon/_Parts are simply silent.
        /// </summary>
        private static void DefaultParts(WeaponSound sound, WeaponDefinition weapon)
        {
            var steps = sound.MutableReloadSteps;
            steps.Clear();
            if (weapon.IsMelee)
            {
                sound.SetParts("draw_blade", null, null, null,
                    weapon.Hold == WeaponHold.MeleeHeavy ? "hit_heavy" : "hit_blade", null);
                return;
            }

            var ammo = weapon.Ammo?.Ammo != null ? weapon.Ammo.Ammo.Id : string.Empty;
            var bullets = weapon.Attack as BulletAttack;
            var silenced = weapon.TryGet<SilencerModule>(out _);
            var draw = weapon.Hold == WeaponHold.Pistol ? "draw_pistol" : ammo == "ammo_battery" ? "draw_energy" : "draw_gun";
            var tail = bullets != null && !silenced ? "tail" : null;
            var shells = bullets == null ? null : bullets.Pellets > 1 ? "shells_shotgun" : "shells_brass";
            var hit = weapon.Attack is ChainAttack ? "hit_energy" : null;
            sound.SetParts(draw, tail, null, shells, hit, null);

            switch (ammo)
            {
                case "ammo_battery": steps.Add(new ReloadStep("battery", 0.3f)); return;
                case "ammo_fuel": steps.Add(new ReloadStep("canister", 0.25f)); return;
                case "ammo_charge":
                    steps.Add(new ReloadStep("gl_open", 0.05f));
                    steps.Add(new ReloadStep("gl_insert", 0.45f));
                    steps.Add(new ReloadStep("gl_close", 1f));
                    return;
                case "ammo_shells":
                    steps.Add(new ReloadStep("shotgun_mag_out", 0.1f));
                    steps.Add(new ReloadStep("shotgun_mag_in", 0.7f));
                    return;
            }

            var prefix = weapon.Hold == WeaponHold.Pistol ? "pistol" : "rifle";
            steps.Add(new ReloadStep(prefix + "_mag_out", 0.05f));
            steps.Add(new ReloadStep(prefix + "_mag_draw", 0.3f));
            steps.Add(new ReloadStep(prefix + "_mag_in", 0.6f));
            steps.Add(new ReloadStep(prefix == "pistol" ? "pistol_slide" : "rifle_charge", 1f));
        }

        private static AudioCatalog LoadOrCreateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog != null) return catalog;

            if (!AssetDatabase.IsValidFolder(CatalogFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Audio");
            catalog = ScriptableObject.CreateInstance<AudioCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            return catalog;
        }

        private static List<string> Clips(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return new List<string>();
            return AssetDatabase.FindAssets("t:AudioClip", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Sounds: Vorbis, mono except the interface (the game pans them itself), short ones decompressed on load so
        /// playing them costs nothing, long ones (ambience loops) compressed in memory.
        /// WebGL: MP3. Unity decodes every clip under 128 KB with WebAudio <c>decodeAudioData</c>, and a mobile browser
        /// (Android Chrome, checked 2026-10-11) fails on its AAC ("EncodingError: Unable to decode audio data"): only
        /// music and the long loops (played by an audio element) were heard. MP3 decodes everywhere.
        /// </summary>
        private static bool ConfigureSound(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) return false;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var isInterface = path.Replace('\\', '/').StartsWith(SoundsFolder + "/UI/", StringComparison.OrdinalIgnoreCase);
            var settings = new AudioImporterSampleSettings
            {
                loadType = clip != null && clip.length > DecompressMaxLength
                    ? AudioClipLoadType.CompressedInMemory
                    : AudioClipLoadType.DecompressOnLoad,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = 0.6f,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
                preloadAudioData = true,
            };
            var web = settings;
            web.compressionFormat = AudioCompressionFormat.MP3;
            // A short loop (continuous fire, fire, a rocket's flight; stage Э3): MP3 is not gapless — the encoder's
            // ~40 ms of silence at the start of the clip was heard on every repeat of the flamethrower. Uncompressed
            // (a WAV for decodeAudioData, decodes everywhere); only a few such clips, so the download barely grows.
            if (IsLoop(path) && clip != null && clip.length <= PcmLoopMaxLength)
            {
                web.compressionFormat = AudioCompressionFormat.PCM;
                web.loadType = AudioClipLoadType.DecompressOnLoad;
            }
            // The WebGL override refuses "preserve sample rate" (SetOverrideSampleSettings returns false, nothing set).
            web.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            web.sampleRateOverride = 44100;
            return Apply(importer, !isInterface, false, settings, web);
        }

        /// <summary>Longest loop kept uncompressed on WebGL, seconds (longer ambience stays MP3).</summary>
        private const float PcmLoopMaxLength = 6f;

        /// <summary>A clip the game plays looping: "loop", "*_loop" or a part "*_flight".</summary>
        private static bool IsLoop(string path)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            return name.Equals("loop", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("_loop", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("_flight", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Music: stereo Vorbis, streamed (never fully in memory where the platform can stream), loaded in the background.</summary>
        private static bool ConfigureMusic(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) return false;
            var settings = new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.Streaming,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = 0.5f,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
                preloadAudioData = false,
            };
            return Apply(importer, false, true, settings);
        }

        private const string WebGL = "WebGL";

        /// <param name="web">WebGL override; null — none (the defaults).</param>
        private static bool Apply(AudioImporter importer, bool mono, bool loadInBackground, AudioImporterSampleSettings settings,
            AudioImporterSampleSettings? web = null)
        {
            var webSame = web == null
                ? !importer.ContainsSampleSettingsOverride(WebGL)
                : importer.ContainsSampleSettingsOverride(WebGL) && Same(importer.GetOverrideSampleSettings(WebGL), web.Value);
            if (importer.forceToMono == mono && importer.loadInBackground == loadInBackground &&
                Same(importer.defaultSampleSettings, settings) && webSame)
                return false;

            importer.forceToMono = mono;
            importer.loadInBackground = loadInBackground;
            importer.defaultSampleSettings = settings;
            if (web == null) importer.ClearSampleSettingOverride(WebGL);
            else if (!importer.SetOverrideSampleSettings(WebGL, web.Value))
                Debug.LogWarning($"[Purge] Build Audio: the WebGL override of '{importer.assetPath}' was refused.");
            importer.SaveAndReimport();
            return true;
        }

        private static bool Same(AudioImporterSampleSettings a, AudioImporterSampleSettings b) =>
            a.loadType == b.loadType && a.compressionFormat == b.compressionFormat &&
            Mathf.Approximately(a.quality, b.quality) && a.sampleRateSetting == b.sampleRateSetting &&
            (a.sampleRateSetting != AudioSampleRateSetting.OverrideSampleRate || a.sampleRateOverride == b.sampleRateOverride) &&
            a.preloadAudioData == b.preloadAudioData;
    }
}
