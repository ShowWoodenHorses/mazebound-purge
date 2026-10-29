using System;
using System.Collections.Generic;

namespace Purge.Application.Save
{
    /// <summary>A level the player has opened.</summary>
    [Serializable]
    public sealed class LevelRecord
    {
        public string LevelId;

        /// <summary>Completed difficulties: bit d = difficulty d (0 = easy … 3 = unreal). Non-zero = the level is passed.</summary>
        public int CompletedMask;

        /// <summary>Difficulties completed with the arena closed (the extra reward is paid once).</summary>
        public int ClosedArenaMask;

        public bool IsCompleted => CompletedMask != 0;

        public bool IsCompletedOn(int difficulty) =>
            difficulty >= 0 && difficulty < 31 && (CompletedMask & (1 << difficulty)) != 0;

        /// <summary>The easy difficulty is always open, each next one — once the previous is completed (ТЗ А.2).</summary>
        public bool IsDifficultyOpen(int difficulty) => difficulty == 0 || IsCompletedOn(difficulty - 1);

        public LevelRecord Clone() => new LevelRecord
        {
            LevelId = LevelId, CompletedMask = CompletedMask, ClosedArenaMask = ClosedArenaMask,
        };
    }

    [Serializable]
    public sealed class SurvivalRecord
    {
        public string LocationId;
        public long BestScore;
        public int BestWave;
    }

    /// <summary>How often a bought weapon falls in drops (ТЗ: favourite ×3, normal ×1, excluded never).</summary>
    public enum PoolMark
    {
        Normal = 0,
        Favorite = 1,
        Excluded = 2,
    }

    /// <summary>A bought weapon or throwable.</summary>
    [Serializable]
    public sealed class OwnedItem
    {
        /// <summary>Stable content id.</summary>
        public string Id;

        /// <summary>Upgrade level, 1 = as bought.</summary>
        public int Level = 1;

        /// <summary>Chosen modifiers of the upgrade forks: bit f = the second option of fork f.</summary>
        public int Forks;

        public PoolMark Mark;
    }

    /// <summary>What the player took into the last run (stage 11): weapon, throwable, chosen boosters; empty = none.</summary>
    [Serializable]
    public sealed class LoadoutSave
    {
        /// <summary>Weapon of slot 1; empty = the base pistol.</summary>
        public string WeaponId = string.Empty;

        public string ThrowableId = string.Empty;

        public List<string> Boosters = new List<string>();

        public void Normalize()
        {
            WeaponId ??= string.Empty;
            ThrowableId ??= string.Empty;
            Boosters ??= new List<string>();
            Boosters.RemoveAll(string.IsNullOrEmpty);
        }
    }

    [Serializable]
    public sealed class SaveStats
    {
        /// <summary>Monsters killed in all runs.</summary>
        public int TotalKills;

        /// <summary>Runs started (interstitials start after the third).</summary>
        public int RunsStarted;

        /// <summary>Runs won (all waves of a level passed).</summary>
        public int RunsWon;

        public void Normalize()
        {
            if (TotalKills < 0) TotalKills = 0;
            if (RunsStarted < 0) RunsStarted = 0;
            if (RunsWon < 0) RunsWon = 0;
        }

        public void MergeFrom(SaveStats other)
        {
            if (other == null) return;
            TotalKills = Math.Max(TotalKills, other.TotalKills);
            RunsStarted = Math.Max(RunsStarted, other.RunsStarted);
            RunsWon = Math.Max(RunsWon, other.RunsWon);
        }
    }

    public enum RunMode
    {
        Level = 0,
        Survival = 1,
    }

    /// <summary>
    /// State of the unfinished run, written as soon as it changes (ТЗ: каждое новое состояние забега сразу попадает в
    /// сейв). Leaving during a wave (<see cref="WaveInProgress"/>) restarts that wave on continue; death clears it.
    /// </summary>
    [Serializable]
    public sealed class RunSave
    {
        public bool Active;
        public RunMode Mode;

        /// <summary>Level (levels mode) or location (survival).</summary>
        public string LevelId;

        public int Difficulty;
        public bool ArenaClosed;

        /// <summary>Seed of the run (waves, drops).</summary>
        public int Seed;

        /// <summary>Wave reached (1-based); 0 = before the first.</summary>
        public int Wave;

        public bool WaveInProgress;

        /// <summary>Earned in this run (added to <see cref="SaveData.Money"/> at the end).</summary>
        public int Money;

        public long Score;
        public int Kills;
        public int Experience;
        public int PlayerLevel = 1;
        public int Health;
        public bool AdReviveUsed;
        public bool SkillReviveUsed;

        /// <summary>Seconds played (the result's time).</summary>
        public float Time;

        /// <summary>Cards taken (stable ids, repeated when taken twice).</summary>
        public List<string> Cards = new List<string>();

        /// <summary>Boosters bought for this run (already applied).</summary>
        public List<string> Boosters = new List<string>();

        /// <summary>The two weapon slots; an empty id = the basic pistol.</summary>
        public List<RunWeapon> Weapons = new List<RunWeapon>();

        /// <summary>0 / 1: the slot in hands.</summary>
        public int ActiveSlot;

        public string ThrowableId;
        public int ThrowableCount;

        /// <summary>Level objects (stage 7в): barrels blown up, chests opened (ids of the level), the chest key dropped.</summary>
        public List<string> BrokenBarrels = new List<string>();

        public List<string> OpenedChests = new List<string>();
        public bool ChestKeyDropped;

        /// <summary>Vehicles (stage 9в): blown up (level ids), HP of the damaged ones still standing.</summary>
        public List<string> DestroyedVehicles = new List<string>();

        public List<RunVehicle> Vehicles = new List<RunVehicle>();

        /// <summary>Explored map cells (stage 7г): a bit per cell, Base64; empty = none.</summary>
        public string Explored;

        /// <summary>No run any more (death, finish, abandon).</summary>
        public void Clear()
        {
            Active = false;
            Mode = RunMode.Level;
            LevelId = null;
            Difficulty = 0;
            ArenaClosed = false;
            Seed = 0;
            Wave = 0;
            WaveInProgress = false;
            Money = 0;
            Score = 0;
            Kills = 0;
            Experience = 0;
            PlayerLevel = 1;
            Health = 0;
            AdReviveUsed = false;
            SkillReviveUsed = false;
            Time = 0f;
            ActiveSlot = 0;
            Cards ??= new List<string>();
            Boosters ??= new List<string>();
            Weapons ??= new List<RunWeapon>();
            Cards.Clear();
            Boosters.Clear();
            Weapons.Clear();
            ThrowableId = null;
            ThrowableCount = 0;
            BrokenBarrels ??= new List<string>();
            OpenedChests ??= new List<string>();
            BrokenBarrels.Clear();
            OpenedChests.Clear();
            ChestKeyDropped = false;
            DestroyedVehicles ??= new List<string>();
            Vehicles ??= new List<RunVehicle>();
            DestroyedVehicles.Clear();
            Vehicles.Clear();
            Explored = null;
        }

        public void Normalize()
        {
            Cards ??= new List<string>();
            Boosters ??= new List<string>();
            Weapons ??= new List<RunWeapon>();
            BrokenBarrels ??= new List<string>();
            OpenedChests ??= new List<string>();
            BrokenBarrels.RemoveAll(string.IsNullOrEmpty);
            OpenedChests.RemoveAll(string.IsNullOrEmpty);
            DestroyedVehicles ??= new List<string>();
            Vehicles ??= new List<RunVehicle>();
            DestroyedVehicles.RemoveAll(string.IsNullOrEmpty);
            Vehicles.RemoveAll(vehicle => vehicle == null || string.IsNullOrEmpty(vehicle.Id) || !(vehicle.Health > 0f) ||
                                          float.IsInfinity(vehicle.Health));
            Cards.RemoveAll(string.IsNullOrEmpty);
            Boosters.RemoveAll(string.IsNullOrEmpty);
            Weapons.RemoveAll(weapon => weapon == null);
            if (Active && string.IsNullOrEmpty(LevelId)) Clear(); // a run of nothing cannot be continued
            if (!Enum.IsDefined(typeof(RunMode), Mode)) Mode = RunMode.Level;
            if (Wave < 0) Wave = 0;
            if (Money < 0) Money = 0;
            if (Score < 0) Score = 0;
            if (Kills < 0) Kills = 0;
            if (Experience < 0) Experience = 0;
            if (PlayerLevel < 1) PlayerLevel = 1;
            if (!(Time >= 0f) || float.IsInfinity(Time)) Time = 0f;
            if (ActiveSlot != 1) ActiveSlot = 0;
            if (ThrowableCount < 0 || string.IsNullOrEmpty(ThrowableId)) ThrowableCount = 0;
            foreach (var weapon in Weapons)
                if (weapon.Ammo < 0) weapon.Ammo = 0;
        }
    }

    [Serializable]
    public sealed class RunVehicle
    {
        /// <summary>The vehicle's id in the level.</summary>
        public string Id;

        public float Health;
    }

    [Serializable]
    public sealed class RunWeapon
    {
        /// <summary>Stable content id; empty = the basic pistol.</summary>
        public string Id;

        /// <summary>Rounds in the magazine.</summary>
        public int Ammo;
    }
}
