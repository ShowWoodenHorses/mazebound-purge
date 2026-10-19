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
        public int RevivesUsed;

        /// <summary>Cards taken (stable ids, repeated when taken twice).</summary>
        public List<string> Cards = new List<string>();

        /// <summary>Boosters bought for this run (already applied).</summary>
        public List<string> Boosters = new List<string>();

        /// <summary>The two weapon slots; an empty id = the basic pistol.</summary>
        public List<RunWeapon> Weapons = new List<RunWeapon>();

        public string ThrowableId;
        public int ThrowableCount;

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
            RevivesUsed = 0;
            Cards ??= new List<string>();
            Boosters ??= new List<string>();
            Weapons ??= new List<RunWeapon>();
            Cards.Clear();
            Boosters.Clear();
            Weapons.Clear();
            ThrowableId = null;
            ThrowableCount = 0;
        }

        public void Normalize()
        {
            Cards ??= new List<string>();
            Boosters ??= new List<string>();
            Weapons ??= new List<RunWeapon>();
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
            if (RevivesUsed < 0) RevivesUsed = 0;
            if (ThrowableCount < 0 || string.IsNullOrEmpty(ThrowableId)) ThrowableCount = 0;
            foreach (var weapon in Weapons)
                if (weapon.Ammo < 0) weapon.Ammo = 0;
        }
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
