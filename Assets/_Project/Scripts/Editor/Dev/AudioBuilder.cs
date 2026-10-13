using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Maze.Core.Audio;
using Maze.Core.Definitions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Audio: import settings of the sounds (<c>Sounds/</c>) and music (<c>Music/</c>), and the
    /// <see cref="AudioCatalog"/> filled from file names (a cue gets every file matching its pattern, in name order).
    /// Weapon sound groups come from the folders under Sounds/Weapon (<see cref="FillWeaponSounds"/>).
    /// Rerun after adding or renaming sound files. Tuning in the catalog (volumes, ranges, pitch…) is kept; only the
    /// clips are replaced. The catalog and the music are made Addressable (group Maze Shared).
    /// </summary>
    internal static class AudioBuilder
    {
        private const string SoundsFolder = "Assets/_Project/Sounds";
        private const string MusicFolder = "Assets/_Project/Music";
        private const string CatalogFolder = "Assets/_Project/Data/Audio";
        private const string CatalogPath = CatalogFolder + "/AudioCatalog.asset";

        /// <summary>Folders right under it are weapon sound groups (see <see cref="FillWeaponSounds"/>).</summary>
        internal const string WeaponGroupsFolder = SoundsFolder + "/Weapon";
        private static readonly Regex AttackFile = new Regex(@"^attack\d*$", RegexOptions.IgnoreCase);
        private static readonly Regex ReloadFile = new Regex(@"^reload\d*$", RegexOptions.IgnoreCase);

        /// <summary>Clips up to this long are decompressed on load (cheap to play often); longer ones stay compressed.</summary>
        private const float DecompressMaxLength = 3f;

        /// <summary>(folder under Sounds, file name pattern, cue).</summary>
        private static readonly (string Folder, string Pattern, Func<AudioCatalog, SoundCue> Cue)[] Cues =
        {
            ("Player", @"^Wood Run \d+$", c => c.Footstep),
            ("Player", @"^Wood Walk \d+$", c => c.FootstepWalk),
            ("Player", @"^hit\d*$", c => c.PlayerHurt),
            ("Player", @"^death$", c => c.PlayerDeath),
            ("Player", @"^win$", c => c.ExitReached),

            ("Weapon", @"^melee-attack\d*$", c => c.MeleeSwing),
            ("Weapon", @"^rifle_attack\d*$", c => c.Shot),
            ("Weapon", @"^reload$", c => c.ReloadMagazine),
            ("Weapon", @"^rifle_one_reload$", c => c.ReloadSingle),
            ("Weapon", @"^bullet\d*$", c => c.BulletImpact),

            ("Other", @"^switch_weapon$", c => c.SwitchWeapon),
            ("Other", @"^key_pick_up$", c => c.PickupKey),
            ("Other", @"^medicine_pick_up$", c => c.PickupMedkit),
            ("Other", @"^weapon_pick_up$", c => c.PickupWeapon),
            ("Other", @"^map$", c => c.PickupMapFragment),
            ("Other", @"^Door Open \d+$", c => c.DoorOpen),
            ("Other", @"^Door Close \d+$", c => c.DoorClose),
            ("Other", @"^door_unlock$", c => c.DoorUnlock),
            ("Other", @"^door_lock$", c => c.DoorLocked),
            ("Other", @"^ice_hit\d*$", c => c.IceHitCue),
            ("Other", @"^ice_break\d*$", c => c.IceBreakCue),
            ("Other", @"^detect$", c => c.ChaseStinger),
            ("Other", @"^light_loop$", c => c.TorchLoop),

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
            ("UI", @"^window_exit_level$", c => c.ConfirmExit),
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
            ("walker", 1f),
            ("listener", 1.12f),
            ("hunter", 0.88f),
        };

        [MenuItem("Maze/Dev/Build Audio")]
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
            if (empty.Count > 0) Debug.LogWarning("[Maze] Build Audio: no files for " + string.Join(", ", empty));
            if (unused.Count > 0) Debug.LogWarning("[Maze] Build Audio: files not used by any sound: " + string.Join(", ", unused));
            if (problem != null) Debug.LogWarning("[Maze] " + problem);
            Debug.Log($"[Maze] Audio built: {CatalogPath}, {sounds.Count} sound file(s), {music.Count} music file(s), " +
                      $"{reimported} reimported.");
        }

        /// <summary>
        /// Sound groups of weapons: every folder right under <see cref="WeaponGroupsFolder"/> is a group (its name),
        /// files <c>attack*</c> its shot or swing, <c>reload*</c> its reload. An existing group keeps its tuning; a new
        /// one starts from the common sounds of the weapons using it. Groups whose folder is gone are removed.
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
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            var groups = new List<WeaponSound>();
            foreach (var folder in folders)
            {
                var name = folder.Key;
                var users = definitions.Where(d => string.Equals(d.SoundGroup, name, StringComparison.OrdinalIgnoreCase)).ToList();
                var sound = catalog.FindWeaponSound(name);
                if (sound == null)
                {
                    var melee = users.Count > 0 && users.All(d => d.Slot == WeaponSlot.Melee);
                    var automatic = users.Count > 0 && users.All(d => d.FireMode == FireMode.Automatic);
                    sound = new WeaponSound(name, (melee ? catalog.MeleeSwing : catalog.Shot).CopyTuning(),
                        (automatic ? catalog.ReloadMagazine : catalog.ReloadSingle).CopyTuning());
                }

                var attack = folder.Where(p => AttackFile.IsMatch(Path.GetFileNameWithoutExtension(p))).ToList();
                var reload = folder.Where(p => ReloadFile.IsMatch(Path.GetFileNameWithoutExtension(p))).ToList();
                sound.Attack.Clips = attack.Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
                sound.Reload.Clips = reload.Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
                used.UnionWith(attack);
                used.UnionWith(reload);
                groups.Add(sound);

                if (users.Count == 0)
                    Debug.LogWarning($"[Maze] Build Audio: no weapon uses the sound group '{name}' (set it in the Sound field " +
                                     "of a weapon definition, or name the folder by a weapon id).");
            }

            foreach (var definition in definitions)
                if (!string.IsNullOrEmpty(definition.Sound) && groups.All(g => !string.Equals(g.Group, definition.Sound, StringComparison.OrdinalIgnoreCase)))
                    Debug.LogWarning($"[Maze] Build Audio: weapon '{definition.name}' uses the sound group '{definition.Sound}', " +
                                     $"but there is no folder {WeaponGroupsFolder}/{definition.Sound}: it plays the common sounds.");

            catalog.MutableWeaponSounds.Clear();
            catalog.MutableWeaponSounds.AddRange(groups);
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
            // The WebGL override refuses "preserve sample rate" (SetOverrideSampleSettings returns false, nothing set).
            web.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            web.sampleRateOverride = 44100;
            return Apply(importer, !isInterface, false, settings, web);
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
                Debug.LogWarning($"[Maze] Build Audio: the WebGL override of '{importer.assetPath}' was refused.");
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
