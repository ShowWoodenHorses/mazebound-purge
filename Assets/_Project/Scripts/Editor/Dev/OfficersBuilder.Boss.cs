using System;
using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Drops;
using Purge.Core.Horde;
using Purge.Core.Monsters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using static Purge.Editor.Dev.CharacterAnimationUtility;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// The bosses of Build Monsters (stage 8a; Art/Monster/&lt;Biome&gt;/Boss): one controller (<see cref="BossControllerPath"/>)
    /// on the Creature Animset — the Brute's idle, walk, run and hits, their own heavy claw attacks, the roar it climbs out
    /// with (the rise state, through its emerge time), the roar of a phase (the cast), a long death; the stun is the
    /// officers'. Size by the body (<see cref="MonsterStats.Radius"/>): 3×3 bodies a head taller than 2×2. Draft numbers of
    /// the card of stage 8: HP, speed, body, the phase; abilities — stages 8b–8d. The Gargoyle and the Elemental wait for
    /// their stages (13, 14).
    /// </summary>
    internal static partial class OfficersBuilder
    {
        public const string BossControllerPath = "Assets/_Project/Art/Monster/Boss.controller";
        private const string BossDropPath = "Assets/_Project/Data/Drops/BossDrop.asset";
        private const string BossPrefix = "Boss_";

        /// <summary>Head height of a 2×2 boss / a 3×3 one, m (the Brute — 1.6).</summary>
        public const float BossHeadHeight = 2.2f, BigBossHeadHeight = 2.7f;

        /// <summary>"Boss_*" clip → the Ultimate Animation Collection (Creature Animset) file it is copied from.</summary>
        private static readonly (string Name, string Source)[] BossClipTable =
        {
            ("Boss_Attack_1", "Humanoid@AttackRightClaws2Creature"),
            ("Boss_Attack_2", "Humanoid@AttackLeftClaws2Creature"),
            ("Boss_Attack_3", "Humanoid@AttackRightClaws3Creature"),
            ("Boss_Entrance", "Humanoid@Roar4Creature"),
            ("Boss_Death", "Humanoid@Death"),
        };

        /// <summary>The bosses: abilities [phase] for now — the phase's roar is the cast.</summary>
        private static readonly ClipSet BossClips = new ClipSet
        {
            Idle = "Brute_Idle", Walk = "Brute_Walk", Run = "Brute_Run", Stun = Stun, Roar = "Brute_Roar",
            Attacks = new[] { "Boss_Attack_1", "Boss_Attack_2", "Boss_Attack_3" },
            Hits = new[] { "Brute_Hit_Front", "Brute_Hit_Left", "Brute_Hit_Right" },
            // One fall for every side.
            Deaths = new[] { "Boss_Death", "Boss_Death", "Boss_Death", "Boss_Death" },
            Casts = new[] { "Brute_Rage" },
            Rise = "Boss_Entrance",
        };

        /// <summary>The bosses this stage builds: the Mutant and the four intermediate ones (stage 8).</summary>
        public static bool IsBuiltBoss(string prefabName) =>
            prefabName == "Mutant_Forest" || prefabName == "ZombieBoss_Common" || prefabName == "Golem_Common" ||
            prefabName == "Medusa_Common" || prefabName == "Demon_Common";

        private static bool IsBigBoss(string prefabName) => prefabName == "Mutant_Forest" || prefabName == "Golem_Common";

        /// <summary>Copies the missing "Boss_*" clips and imports them (in place; the death falls away); returns how many were copied.</summary>
        private static int PrepareBossClips(Dictionary<string, string> uac)
        {
            var copied = 0;
            foreach (var (name, source) in BossClipTable)
            {
                var target = $"{AnimationsPath}/{name}.fbx";
                if (AssetDatabase.LoadMainAssetAtPath(target) != null) continue;
                if (!uac.TryGetValue(source, out var path))
                    throw new InvalidOperationException($"Clip '{name}' is missing and '{source}' is not in Import: put the " +
                                                        "Ultimate Animation Collection pack into Import/Animations.");
                if (!AssetDatabase.CopyAsset(path, target))
                    throw new InvalidOperationException($"Could not copy '{path}' to '{target}'.");
                copied++;
            }

            ImportClips(BossPrefix, _ => false, null, name => name != "Boss_Death");
            return copied;
        }

        /// <summary>A boss: its controller, the head height of its body, measured run and walk, the phase's roar timing.</summary>
        private static MonsterViewDefinition SetupBoss(GameObject prefab, MonsterDefinition definition, AnimatorController controller,
            List<string> report)
        {
            var viewPath = $"{ViewsPath}/{prefab.name}.asset";
            var view = AssetDatabase.LoadAssetAtPath<MonsterViewDefinition>(viewPath);
            if (view == null)
            {
                view = ScriptableObject.CreateInstance<MonsterViewDefinition>();
                AssetDatabase.CreateAsset(view, viewPath);
                SeedLook(view, prefab.name);
            }

            var path = AssetDatabase.GetAssetPath(prefab);
            var head = IsBigBoss(prefab.name) ? BigBossHeadHeight : BossHeadHeight;
            var scale = SetupPrefab(path, controller, view.Equipment, head);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            var runSpeed = MeasureGroundSpeed(prefab, LoadClip(BossClips.Run), scale);
            var walkSpeed = MeasureGroundSpeed(prefab, LoadClip(BossClips.Walk), scale);
            var roar = LoadClip(BossClips.Casts[0]);
            // The phase's roar plays through its warning at about its own pace.
            var roarMoment = Mathf.Clamp(PhaseWarning / Mathf.Max(0.05f, roar.length), 0.05f, 1f);
            view.Configure(definition.Id, prefab, runSpeed, walkSpeed, roarMoment, roar.length);
            EditorUtility.SetDirty(view);
            report.Add($"  {prefab.name} ({definition.Id}): boss, scale {scale:0.00}, run {runSpeed:0.00} c/s, walk {walkSpeed:0.00} c/s, " +
                       $"death {LoadClip("Boss_Death").length:0.0} s.");
            return view;
        }

        /// <summary>Draft roar of a phase, s (its warning).</summary>
        private const float PhaseWarning = 1.2f;

        /// <summary>Draft numbers of a new boss (card of stage 8; balance — stage 10).</summary>
        private static void SeedBoss(MonsterDefinition definition, string prefabName)
        {
            var stats = definition.Stats;
            definition.SetDrop(EnsureBossDrop());
            definition.SetPerception(MonsterPerception.Hunter);
            stats.EmergeTime = 2.5f;
            stats.CorpseTime = 8f;
            stats.SightRange = 14f;
            stats.TurnSpeed = 200f;
            stats.AttackInterval = 1.8f;
            stats.DoorDamage = 60f;
            stats.Radius = IsBigBoss(prefabName) ? 1.5f : 1f;
            stats.AttackReach = stats.Radius + 1f;
            PhaseAbility phase = null;
            switch (prefabName)
            {
                case "Mutant_Forest":
                    // The Forest's final boss: from 50 % — rage (× 1.5 damage; the summons ×2 — stage 8c).
                    stats.MaxHp = 6000f;
                    stats.Speed = 1.6f;
                    stats.Damage = 35;
                    phase = new PhaseAbility { HealthBelow = 0.5f, DamageFactor = 1.5f, Rage = true, CastSound = "rage_roar" };
                    break;
                case "ZombieBoss_Common":
                    // The Butcher: rage from 30 %.
                    stats.MaxHp = 3000f;
                    stats.Speed = 1.8f;
                    stats.Damage = 28;
                    phase = new PhaseAbility { HealthBelow = 0.3f, DamageFactor = 1.5f, Rage = true, CastSound = "rage_roar" };
                    break;
                case "Golem_Common":
                    stats.MaxHp = 4000f;
                    stats.Speed = 1.4f;
                    stats.Damage = 32;
                    stats.AttackInterval = 2.2f;
                    break;
                case "Medusa_Common":
                    stats.MaxHp = 3000f;
                    stats.Speed = 1.8f;
                    stats.Damage = 22;
                    break;
                default: // Demon_Common
                    stats.MaxHp = 3500f;
                    stats.Speed = 2f;
                    stats.Damage = 25;
                    break;
            }

            if (phase != null)
            {
                phase.Warning = PhaseWarning;
                definition.SetAbilities(phase);
            }
            else definition.SetAbilities();

            EditorUtility.SetDirty(definition);
        }

        /// <summary>
        /// The bosses' drop (ТЗ: гарантированное оружие на ступень выше дропа элиты): a weapon always, the elite's rarity
        /// odds one step up — no common one.
        /// </summary>
        private static DropTable EnsureBossDrop()
        {
            var drop = AssetDatabase.LoadAssetAtPath<DropTable>(BossDropPath);
            if (drop != null) return drop;
            if (!AssetDatabase.CopyAsset(EliteDropPath, BossDropPath))
                throw new InvalidOperationException($"Could not copy '{EliteDropPath}' to '{BossDropPath}'.");
            drop = AssetDatabase.LoadAssetAtPath<DropTable>(BossDropPath);
            drop.Configure(ContentIds.FromName("BossDrop"), nothing: 0f, ammo: 0f, medkit: 0f, throwable: 0f, weapon: 100f);
            drop.SetRarityWeights(0f, 100f, 45f, 18f, 10f);
            EditorUtility.SetDirty(drop);
            return drop;
        }
    }
}
