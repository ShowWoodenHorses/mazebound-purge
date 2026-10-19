using System;
using System.Collections.Generic;
using Purge.Gameplay.Combat;

namespace Purge.Application.Save
{
    /// <summary>
    /// Everything that persists between sessions (ТЗ: прогрессия, экономика, продолжение забега). Serialized with
    /// <c>JsonUtility</c>, hence public fields. Content is referred to by stable ids (<c>ContentDefinition.Id</c>); an id
    /// the game no longer has is kept and ignored. One document: the local copy, the cloud and the VK slots carry it whole.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>1: the first Purge format. 0 (field missing): a Maze-format save of an early dev build.</summary>
        public const int CurrentVersion = 1;

        public int SaveVersion = CurrentVersion;

        /// <summary>The one currency, between runs.</summary>
        public int Money;

        /// <summary>Opened levels and completed difficulties.</summary>
        public List<LevelRecord> Levels = new List<LevelRecord>();

        /// <summary>Best survival results per location.</summary>
        public List<SurvivalRecord> Survival = new List<SurvivalRecord>();

        /// <summary>Bought weapons and throwables: upgrade level, chosen forks, drop pool mark.</summary>
        public List<OwnedItem> Arsenal = new List<OwnedItem>();

        /// <summary>Bought nodes of the character tree (stable ids).</summary>
        public List<string> CharacterNodes = new List<string>();

        public SaveStats Stats = new SaveStats();

        /// <summary>The unfinished run (Continue); <see cref="RunSave.Active"/> false = none.</summary>
        public RunSave Run = new RunSave();

        public SettingsData Settings = new SettingsData();

        public bool HasRun => Run != null && Run.Active;

        /// <summary>
        /// Replaces lists and objects missing in old or hand-edited JSON, drops broken and repeated entries, clamps
        /// numbers. A Maze-format save (no <see cref="SaveVersion"/>) keeps only its settings: its progress fields are gone.
        /// </summary>
        public void Normalize()
        {
            Levels ??= new List<LevelRecord>();
            Survival ??= new List<SurvivalRecord>();
            Arsenal ??= new List<OwnedItem>();
            CharacterNodes ??= new List<string>();
            Stats ??= new SaveStats();
            Run ??= new RunSave();
            Settings ??= new SettingsData();
            Settings.Language ??= string.Empty;
            // Saves made before the controls settings existed have zeros there (a valid size is never zero).
            if (!(Settings.Controls.Size > 0f)) Settings.Controls = ControlsSettings.Default;
            SaveVersion = CurrentVersion;

            if (Money < 0) Money = 0;
            Levels.RemoveAll(record => record == null || string.IsNullOrEmpty(record.LevelId));
            Distinct(Levels, record => record.LevelId);
            foreach (var record in Levels)
            {
                if (record.CompletedMask < 0) record.CompletedMask = 0;
                if (record.ClosedArenaMask < 0) record.ClosedArenaMask = 0;
            }

            Survival.RemoveAll(record => record == null || string.IsNullOrEmpty(record.LocationId));
            Distinct(Survival, record => record.LocationId);
            foreach (var record in Survival)
            {
                if (record.BestScore < 0) record.BestScore = 0;
                if (record.BestWave < 0) record.BestWave = 0;
            }

            Arsenal.RemoveAll(item => item == null || string.IsNullOrEmpty(item.Id));
            Distinct(Arsenal, item => item.Id);
            foreach (var item in Arsenal)
            {
                if (item.Level < 1) item.Level = 1;
                if (!Enum.IsDefined(typeof(PoolMark), item.Mark)) item.Mark = PoolMark.Normal;
            }

            CharacterNodes.RemoveAll(string.IsNullOrEmpty);
            Distinct(CharacterNodes, node => node);
            Stats.Normalize();
            Run.Normalize();
        }

        public LevelRecord FindLevel(string levelId) => Levels.Find(record => record.LevelId == levelId);

        public OwnedItem FindItem(string id) => Arsenal.Find(item => item.Id == id);

        /// <summary>
        /// Adds the progress of another save (the same player on another device, before signing in): opened levels and
        /// completed difficulties, records, purchases (the higher upgrade with its forks), tree nodes, statistics and money
        /// — the larger value (money is not summed: the same coins would count twice). Settings and the unfinished run
        /// stay these (the other device's run is taken only when this one has none). Both saves must be normalized.
        /// </summary>
        public void MergeFrom(SaveData other)
        {
            if (other == null) return;
            Money = Math.Max(Money, other.Money);

            foreach (var theirs in other.Levels)
            {
                var mine = FindLevel(theirs.LevelId);
                if (mine == null)
                {
                    Levels.Add(theirs.Clone());
                    continue;
                }

                mine.CompletedMask |= theirs.CompletedMask;
                mine.ClosedArenaMask |= theirs.ClosedArenaMask;
            }

            foreach (var theirs in other.Survival)
            {
                var mine = Survival.Find(record => record.LocationId == theirs.LocationId);
                if (mine == null)
                {
                    Survival.Add(new SurvivalRecord { LocationId = theirs.LocationId, BestScore = theirs.BestScore, BestWave = theirs.BestWave });
                    continue;
                }

                mine.BestScore = Math.Max(mine.BestScore, theirs.BestScore);
                mine.BestWave = Math.Max(mine.BestWave, theirs.BestWave);
            }

            foreach (var theirs in other.Arsenal)
            {
                var mine = FindItem(theirs.Id);
                if (mine == null)
                    Arsenal.Add(new OwnedItem { Id = theirs.Id, Level = theirs.Level, Forks = theirs.Forks, Mark = theirs.Mark });
                else if (theirs.Level > mine.Level)
                {
                    mine.Level = theirs.Level;
                    mine.Forks = theirs.Forks;
                }
            }

            foreach (var node in other.CharacterNodes)
                if (!CharacterNodes.Contains(node))
                    CharacterNodes.Add(node);

            Stats.MergeFrom(other.Stats);
            if (!HasRun && other.HasRun) Run = other.Run;
        }

        private static void Distinct<T>(List<T> list, Func<T, string> key)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            list.RemoveAll(item => !seen.Add(key(item)));
        }
    }

    [Serializable]
    public sealed class SettingsData
    {
        public float MusicVolume = 1f;
        public float SfxVolume = 1f;
        public ControlsSettings Controls = ControlsSettings.Default;

        /// <summary>Frames-per-second counter on screen (off by default; old saves get false).</summary>
        public bool ShowFps;

        /// <summary>Map screen: hide the player icon. Negated so that old saves (false) show it.</summary>
        public bool MapHidePlayer;

        /// <summary>Map screen: hide zombies / weapons / medkits / keys. Negated like <see cref="MapHidePlayer"/>.</summary>
        public bool MapHideZombies;
        public bool MapHideWeapons;
        public bool MapHideMedkits;
        public bool MapHideKeys;

        /// <summary>Language code ("en", "ru", …); empty until the first start chooses one from the system language.</summary>
        public string Language = string.Empty;
    }

    /// <summary>
    /// Controls: on-screen stick response, size and layout (<see cref="TouchLayout"/>), aiming (all input devices). Applied at once, without restarting
    /// a level.
    /// </summary>
    [Serializable]
    public struct ControlsSettings : IEquatable<ControlsSettings>
    {
        public const float MinDeadZone = 0f;
        public const float MaxDeadZone = 0.4f;
        public const float MinSize = 0.7f;
        public const float MaxSize = 1.4f;
        public const float MinOpacity = 0.2f;
        public const float MaxOpacity = 1f;

        /// <summary>Part of the stick travel (0..1) that does nothing.</summary>
        public float StickDeadZone;

        /// <summary>0 = soft start (fine control near the centre), 1 = full speed quickly.</summary>
        public float StickSensitivity;

        /// <summary>The stick appears where the thumb touches (its side of the screen) rather than staying in place.</summary>
        public bool FloatingStick;

        /// <summary>Scale of the stick and buttons.</summary>
        public float Size;

        public float Opacity;

        /// <summary>Mirrored layout: stick on the right, buttons on the left.</summary>
        public bool LeftHanded;

        /// <summary>Directions attacks and the standing facing snap to.</summary>
        public AimMode AimMode;

        /// <summary>Attacks turn to the nearest visible zombie near the wanted direction.</summary>
        public bool AimAssist;

        /// <summary>Positions and sizes of the stick and buttons set by the player (old saves: built-in places).</summary>
        public TouchLayout Layout;

        /// <summary>Aim help is on by default on mobile (touch), off elsewhere.</summary>
        public static ControlsSettings Default => new ControlsSettings
        {
            AimMode = UnityEngine.Application.isMobilePlatform ? AimMode.Eight : AimMode.Free,
            AimAssist = UnityEngine.Application.isMobilePlatform,
            StickDeadZone = 0.2f,
            StickSensitivity = 0.5f,
            FloatingStick = true,
            Size = 1f,
            Opacity = 0.6f,
            LeftHanded = false,
            Layout = TouchLayout.Default,
        };

        public ControlsSettings Clamped()
        {
            var result = this;
            result.StickDeadZone = Clamp(StickDeadZone, MinDeadZone, MaxDeadZone, Default.StickDeadZone);
            result.StickSensitivity = Clamp(StickSensitivity, 0f, 1f, Default.StickSensitivity);
            result.Size = Clamp(Size, MinSize, MaxSize, Default.Size);
            result.Opacity = Clamp(Opacity, MinOpacity, MaxOpacity, Default.Opacity);
            if (!Enum.IsDefined(typeof(AimMode), AimMode)) result.AimMode = Default.AimMode;
            result.Layout = Layout.Clamped();
            return result;
        }

        public bool Equals(ControlsSettings other) =>
            UnityEngine.Mathf.Approximately(StickDeadZone, other.StickDeadZone) &&
            UnityEngine.Mathf.Approximately(StickSensitivity, other.StickSensitivity) &&
            FloatingStick == other.FloatingStick &&
            UnityEngine.Mathf.Approximately(Size, other.Size) &&
            UnityEngine.Mathf.Approximately(Opacity, other.Opacity) &&
            LeftHanded == other.LeftHanded &&
            AimMode == other.AimMode &&
            AimAssist == other.AimAssist &&
            Layout.Equals(other.Layout);

        public override bool Equals(object obj) => obj is ControlsSettings other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(HashCode.Combine(StickDeadZone, StickSensitivity, FloatingStick, Size, Opacity, LeftHanded, AimMode, AimAssist), Layout);

        private static float Clamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) ? fallback : UnityEngine.Mathf.Clamp(value, min, max);
    }
}
