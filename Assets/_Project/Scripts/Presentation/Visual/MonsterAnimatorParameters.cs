namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Parameters of the officers' (elite, bosses') Animator controllers that Build Monsters makes and
    /// <c>MonsterViewPresenter</c> sets: the controller only shows the gameplay's state.
    /// </summary>
    public static class MonsterAnimatorParameters
    {
        /// <summary>Int: 0 stands, 1 walks (wanders), 2 runs.</summary>
        public const string Pace = "Pace";

        /// <summary>Float: playback of the walk / run clip (the monster's speed over the clip's ground speed).</summary>
        public const string Playback = "Playback";

        public const string Attack = "Attack";
        public const string AttackIndex = "AttackIndex";

        /// <summary>Float: the attack state lasts 1 s at 1; the presenter sets 1 / attack interval.</summary>
        public const string AttackSpeed = "AttackSpeed";

        public const string Hit = "Hit";
        public const string HitIndex = "HitIndex";
        public const string Stunned = "Stunned";

        /// <summary>Bool: has just noticed the player — roars (the caller's scream).</summary>
        public const string Alert = "Alert";

        /// <summary>Bool: casts an ability (warning and recovery).</summary>
        public const string Cast = "Cast";

        /// <summary>Float: playback of the cast clip (fitted so it lets go at the end of the warning).</summary>
        public const string CastSpeed = "CastSpeed";

        /// <summary>Int: which ability is cast (its index in the monster's list) — a cast clip per ability (stage 5d).</summary>
        public const string CastIndex = "CastIndex";

        /// <summary>Bool: gets up from the ground (raised by a Shaman, stage 5e).</summary>
        public const string Emerging = "Emerging";

        /// <summary>Float 0..1: how far it has got up (drives the rise clip's time).</summary>
        public const string EmergeProgress = "EmergeProgress";

        public const string Dead = "Dead";

        /// <summary>Int: 0 front (falls back), 1 back, 2 left, 3 right.</summary>
        public const string DeathIndex = "DeathIndex";

        public const int AttackClips = 3;
        public const int HitClips = 3;
    }
}
