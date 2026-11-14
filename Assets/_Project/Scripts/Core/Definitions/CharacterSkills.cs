using System;

namespace Purge.Core.Definitions
{
    [Flags]
    public enum CharacterSkills
    {
        None = 0,
        QuickHands = 1 << 0,
        SharpEye = 1 << 1,
        WideChoice = 1 << 2,
        CardControl = 1 << 3,
        Tactician = 1 << 4,
        Sapper = 1 << 5,
        Preparation = 1 << 6,
        Luck = 1 << 7,
        SecondWind = 1 << 8,
        LastStand = 1 << 9,
        Barrier = 1 << 10,
        Instinct = 1 << 11,
        QuietSteps = 1 << 12,
        Buyer = 1 << 13,
        ChestHunter = 1 << 14,
    }
}
