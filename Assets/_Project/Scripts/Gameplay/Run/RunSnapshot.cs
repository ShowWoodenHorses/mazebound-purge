using System.Collections.Generic;

namespace Purge.Gameplay.Run
{
    /// <summary>
    /// The run's state at a checkpoint (stage 6e; ТЗ «Продолжение забега»): taken when a wave starts (then
    /// <see cref="WaveInProgress"/>, the state the wave began with) and when it is cleared. A continued run starts from it in
    /// the pause before <see cref="NextWave"/>: what was won inside an unfinished wave is not in it. The save's form is
    /// <c>RunSave</c> (Application).
    /// </summary>
    public sealed class RunSnapshot
    {
        /// <summary>Seed of the run: its waves are generated from it.</summary>
        public int Seed;

        /// <summary>The wave last started or cleared (0-based); -1 = none yet.</summary>
        public int Wave = -1;

        /// <summary>Taken at the start of <see cref="Wave"/>: a continued run plays it again.</summary>
        public bool WaveInProgress;

        public int Money;
        public long Score;
        public int Kills;
        public int WavesCleared;
        public int Experience;
        public int Level = 1;
        public int Health;
        public bool AdReviveUsed;
        public bool SkillReviveUsed;

        /// <summary>Seconds played.</summary>
        public float Time;

        /// <summary>Cards taken: a card's id once per level.</summary>
        public List<string> Cards = new List<string>();

        /// <summary>Content id per weapon slot; null or empty = the basic pistol.</summary>
        public string[] Weapons = new string[2];

        /// <summary>Rounds in each slot's magazine.</summary>
        public int[] Ammo = new int[2];

        public int ActiveSlot;
        public string ThrowableId;
        public int ThrowableCount;

        /// <summary>Barrels blown up (level object ids, stage 7в): a continued run has them gone.</summary>
        public List<string> BrokenBarrels = new List<string>();

        /// <summary>Chests opened (a challenge only when its champions were beaten): once per run.</summary>
        public List<string> OpenedChests = new List<string>();

        /// <summary>The chest key has dropped: a continued run gives it to the player while the locked chest is shut.</summary>
        public bool ChestKeyDropped;

        /// <summary>Vehicles blown up or breaking down (level object ids, stage 9в): a continued run has them gone.</summary>
        public List<string> DestroyedVehicles = new List<string>();

        /// <summary>HP of the damaged vehicles still standing (id → HP); a continued run has them at their places.</summary>
        public Dictionary<string, float> VehicleHealth = new Dictionary<string, float>();

        /// <summary>Explored map cells (stage 7г): <see cref="Visibility.MapExploration.Export"/>; null = none.</summary>
        public string Explored;

        /// <summary>The wave a continued run starts with (after its pause).</summary>
        public int NextWave => WaveInProgress ? System.Math.Max(0, Wave) : Wave + 1;
    }
}
