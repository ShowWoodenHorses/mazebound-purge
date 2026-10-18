using System;
using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Drops;
using Purge.Core.Effects;
using Purge.Core.Horde;
using Purge.Core.Monsters;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Champions of Build Monsters (stage 5e2; ТЗ: рядовые и офицеры с аффиксом — быстрый, огненный, бронированный,
    /// взрывной, вампир): the five draft affixes in Data/Monsters/Champions (made once; numbers and modules are kept on
    /// rebuilds — a new affix is a new asset in the folder), and the sandbox's champion rules get every affix of the
    /// folder plus the rank-up drops (the officers' table for the rank-and-file, the elite's for officers).
    /// </summary>
    internal static partial class OfficersBuilder
    {
        public const string ChampionsPath = "Assets/_Project/Data/Monsters/Champions";

        /// <summary>The draft affixes (made before any definition: creating an asset re-imports).</summary>
        private static void EnsureChampionAffixes()
        {
            EnsureFolder(ChampionsPath);
            EnsureAffix("Affix_Fast", new Color(1f, 0.9f, 0.2f, 0.7f), new SpeedAffix { SpeedFactor = 1.5f, AttackRateFactor = 1.3f });

            var burning = AssetDatabase.LoadAssetAtPath<StatusDefinition>(EffectsPath + "/Status_Burning.asset");
            var fire = AssetDatabase.LoadAssetAtPath<ZoneDefinition>(EffectsPath + "/Zone_Fire.asset");
            var resist = new ResistAffix();
            if (burning != null) resist.SetResistances(new StatusResistance(burning, 0f));
            EnsureAffix("Affix_Fiery", new Color(1f, 0.45f, 0.1f, 0.8f),
                new HitStatusAffix { Status = burning, Duration = 3f },
                new TrailAffix { Zone = fire, Duration = 3f, Strength = 1f },
                resist);

            EnsureAffix("Affix_Armored", new Color(0.45f, 0.65f, 1f, 0.7f), new ArmorAffix { DamageTaken = 0.5f });

            var blast = new DeathBlastAffix { Delay = 0.6f };
            var explosion = new ExplosionEffect { Damage = 30f, EdgeDamage = 0.35f, SoundRadius = 12f };
            explosion.SetRadius(2f);
            blast.SetEffects(explosion);
            EnsureAffix("Affix_Explosive", new Color(1f, 0.15f, 0.1f, 0.85f), blast);

            EnsureAffix("Affix_Vampire", new Color(0.75f, 0f, 0.3f, 0.8f), new LifestealAffix { Share = 1f });
        }

        private static void EnsureAffix(string name, Color glow, params AffixModule[] modules)
        {
            var path = $"{ChampionsPath}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<ChampionAffixDefinition>(path) != null) return;
            var affix = ScriptableObject.CreateInstance<ChampionAffixDefinition>();
            affix.Configure(ContentIds.FromName(name), glow, modules);
            AssetDatabase.CreateAsset(affix, path);
        }

        /// <summary>Every affix of the folder (sorted by path), checked; problems go to <paramref name="problems"/>.</summary>
        public static ChampionAffixDefinition[] FindChampionAffixes(List<string> problems)
        {
            var affixes = new List<ChampionAffixDefinition>();
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(ChampionAffixDefinition), new[] { ChampionsPath }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                var affix = AssetDatabase.LoadAssetAtPath<ChampionAffixDefinition>(path);
                if (affix == null) continue;
                affix.Validate(problems);
                affixes.Add(affix);
            }

            return affixes.ToArray();
        }

        /// <summary>The sandbox's champions: the folder's affixes, the rank-up drops when not set by hand.</summary>
        public static void SetupChampions(StressTestDefinition stress, ChampionAffixDefinition[] affixes)
        {
            stress.Champions.SetAffixes(affixes, AssetDatabase.LoadAssetAtPath<DropTable>(OfficerDropPath),
                AssetDatabase.LoadAssetAtPath<DropTable>(EliteDropPath));
        }
    }
}
