using System.Collections.Generic;
using Purge.Core.Balance;
using Purge.Core.Monsters;

namespace Purge.Gameplay.Balance
{
    /// <summary>
    /// One run of the balance bot (stage 10a): handed to the level in <see cref="Level.LevelLaunchOptions.Bot"/>;
    /// the level fills <see cref="Report"/>, the batch runner reads it after the run.
    /// </summary>
    public sealed class BotRun
    {
        public BotRun(BotPlan plan)
        {
            Plan = plan;
        }

        public BotPlan Plan { get; }

        public BotReport Report { get; } = new BotReport();
    }

    /// <summary>What happened in a bot's run; filled while it goes, so a stopped run keeps what it had.</summary>
    public sealed class BotReport
    {
        public const int Ranks = (int)MonsterRank.Boss + 1;

        /// <summary>Seconds of play (pauses — cards — not counted).</summary>
        public float Duration { get; internal set; }

        public bool Died { get; internal set; }
        public int WaveCount { get; internal set; }
        public int WavesCleared { get; internal set; }

        /// <summary>Kills by <see cref="MonsterRank"/>.</summary>
        public int[] Kills { get; } = new int[Ranks];

        /// <summary>The run's money (kills, waves).</summary>
        public int Money { get; internal set; }

        /// <summary>Money of kills alone (before rounding).</summary>
        public float KillMoney { get; internal set; }

        public int XpLevel { get; internal set; } = 1;

        /// <summary>Experience gained over the run.</summary>
        public float Experience { get; internal set; }

        public int DamageTaken { get; internal set; }
        public int Healed { get; internal set; }
        public int Throws { get; internal set; }
        public int ChestsOpened { get; internal set; }
        public int WeaponsTaken { get; internal set; }

        /// <summary>Ids of the weapons in the slots at the end ("" — the base pistol).</summary>
        public string[] Weapons { get; } = { "", "" };

        /// <summary>Cards in the order taken, "id@second".</summary>
        public List<string> Cards { get; } = new List<string>();

        public List<BotWave> Waves { get; } = new List<BotWave>();

        public BotWave CurrentWave => Waves.Count > 0 ? Waves[Waves.Count - 1] : null;
    }

    /// <summary>One wave of a bot's run: from its start to the start of the next one (its pause included).</summary>
    public sealed class BotWave
    {
        public BotWave(int index, float start)
        {
            Index = index;
            Start = start;
        }

        public int Index { get; }

        /// <summary>Second of the run the wave started at.</summary>
        public float Start { get; }

        /// <summary>Seconds from the start to clearing it; -1 — not cleared.</summary>
        public float Duration { get; internal set; } = -1f;

        public int[] Kills { get; } = new int[BotReport.Ranks];
        public int Money { get; internal set; }
        public float Experience { get; internal set; }
        public int DamageTaken { get; internal set; }

        /// <summary>The player's experience level and HP when it was cleared (or the run ended).</summary>
        public int XpLevel { get; internal set; }

        public int Health { get; internal set; }
    }
}
