using System;
using System.Collections.Generic;
using Maze.Gameplay.Combat;

namespace Maze.Application.Save
{
    /// <summary>
    /// Everything that persists between sessions (ТЗ §85). Serialized with <c>JsonUtility</c>, hence public fields.
    /// The current unfinished run is never stored.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>
        /// 2: aim settings in <see cref="ControlsSettings"/>. 3: Attack, Melee and Ranged are one cluster on screen
        /// (placed as <see cref="TouchElement.Attack"/>); their old separate places are dropped. 4: Use got a new
        /// built-in place left of the cluster; a place saved near the old Attack button would overlap it — dropped.
        /// </summary>
        public const int CurrentVersion = 4;

        public int Version = CurrentVersion;

        /// <summary>Levels opened by completing the previous one. The first catalog level is always open.</summary>
        public List<string> UnlockedLevels = new List<string>();

        /// <summary>Best stars per completed level.</summary>
        public List<LevelStarsRecord> LevelStars = new List<LevelStarsRecord>();

        /// <summary>Zombies killed in completed runs.</summary>
        public int TotalKills;

        public SettingsData Settings = new SettingsData();

        /// <summary>Map layers bought with a rewarded ad, per level (they stay for replays of that level).</summary>
        public List<MapUnlockRecord> MapUnlocks = new List<MapUnlockRecord>();

        /// <summary>Replaces lists and objects missing in old or hand-edited JSON.</summary>
        public void Normalize()
        {
            UnlockedLevels ??= new List<string>();
            LevelStars ??= new List<LevelStarsRecord>();
            MapUnlocks ??= new List<MapUnlockRecord>();
            MapUnlocks.RemoveAll(record => record == null || string.IsNullOrEmpty(record.LevelId));
            Settings ??= new SettingsData();
            Settings.Language ??= string.Empty;
            // Saves made before the controls settings existed have zeros there (a valid size is never zero).
            if (!(Settings.Controls.Size > 0f)) Settings.Controls = ControlsSettings.Default;
            if (Version < 2)
            {
                var defaults = ControlsSettings.Default;
                Settings.Controls.AimMode = defaults.AimMode;
                Settings.Controls.AimAssist = defaults.AimAssist;
            }
            if (Version < 3)
            {
                Settings.Controls.Layout.Attack = TouchPlacement.Default;
                Settings.Controls.Layout.Melee = TouchPlacement.Default;
                Settings.Controls.Layout.Ranged = TouchPlacement.Default;
            }
            if (Version < 4) Settings.Controls.Layout.Use = TouchPlacement.Default;
            Version = CurrentVersion;
            UnlockedLevels.RemoveAll(string.IsNullOrEmpty);
            LevelStars.RemoveAll(record => record == null || string.IsNullOrEmpty(record.LevelId));
            if (TotalKills < 0) TotalKills = 0;
            foreach (var record in LevelStars)
                if (!(record.BestTime > 0f)) record.BestTime = 0f; // Negative or NaN from a hand-edited save.
        }

        /// <summary>
        /// Adds the progress of another save (the same player on another device, before signing in): opened levels,
        /// best stars and times, layers bought on the map; kills — the larger count. Settings stay these.
        /// Both saves must be normalized.
        /// </summary>
        public void MergeFrom(SaveData other)
        {
            if (other == null) return;
            foreach (var level in other.UnlockedLevels)
                if (!UnlockedLevels.Contains(level))
                    UnlockedLevels.Add(level);

            foreach (var theirs in other.LevelStars)
            {
                var mine = LevelStars.Find(record => record.LevelId == theirs.LevelId);
                if (mine == null)
                {
                    LevelStars.Add(new LevelStarsRecord { LevelId = theirs.LevelId, Stars = theirs.Stars, BestTime = theirs.BestTime });
                    continue;
                }

                if (theirs.Stars > mine.Stars) mine.Stars = theirs.Stars;
                if (theirs.BestTime > 0f && (mine.BestTime <= 0f || theirs.BestTime < mine.BestTime))
                    mine.BestTime = theirs.BestTime;
            }

            foreach (var theirs in other.MapUnlocks)
            {
                var mine = MapUnlocks.Find(record => record.LevelId == theirs.LevelId);
                if (mine == null) MapUnlocks.Add(new MapUnlockRecord { LevelId = theirs.LevelId, Layers = theirs.Layers });
                else mine.Layers |= theirs.Layers;
            }

            if (other.TotalKills > TotalKills) TotalKills = other.TotalKills;
        }
    }

    [Serializable]
    public sealed class LevelStarsRecord
    {
        public string LevelId;
        public int Stars;

        /// <summary>Best time of a completed run, seconds; 0 = none (records made before the timer).</summary>
        public float BestTime;
    }

    /// <summary>Map layers that need a rewarded ad (the player and map pieces are always free).</summary>
    [Flags]
    public enum MapUnlock
    {
        None = 0,
        Zombies = 1,
        Weapons = 2,
        Medkits = 4,
        Keys = 8,
    }

    [Serializable]
    public sealed class MapUnlockRecord
    {
        public string LevelId;
        public MapUnlock Layers;
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

        /// <summary>Map screen: hide icons of map fragments not collected yet. Negated like <see cref="MapHidePlayer"/>.</summary>
        public bool MapHideFragments;

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
