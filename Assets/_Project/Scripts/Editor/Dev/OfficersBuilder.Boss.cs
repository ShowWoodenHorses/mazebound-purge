using System;
using System.Collections.Generic;
using Purge.Core.Content;
using Purge.Core.Drops;
using Purge.Core.Effects;
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
            // The Mutant's blow on the ground (stage 8c): both fists brought down (Bare Fists set).
            ("Boss_Slam", "Humanoid@SmashingFists"),
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

        /// <summary>
        /// The Mutant's own controller (stage 8c): the bosses' states, a cast clip per ability of
        /// <see cref="MutantAbilities"/> — the phase's roar, the summons' roar (both), the roar before the charge, the
        /// fists on the ground, the boulder heaved with both claws (no throw without a weapon in the packs — polish).
        /// </summary>
        public const string MutantControllerPath = "Assets/_Project/Art/Monster/Mutant.controller";
        private const string MutantArtPath = "Assets/_Project/Art/Monster/Mutant";
        private const string BoulderModelPath = "Assets/_Project/Import/PolygonNature/Prefabs/Rocks/SM_Rock_Boulder_01.prefab";

        /// <summary>The pack's plain palette (its prefab's moss shader is not converted to Purge/Lit); before and after Collect.</summary>
        private static readonly string[] BoulderMaterialPaths =
        {
            "Assets/_Project/Art/ThirdParty/PolygonNature/Materials/PolygonNature_01.mat",
            "Assets/_Project/Import/PolygonNature/Materials/PolygonNature_01.mat",
        };

        private static readonly ClipSet MutantClips = new ClipSet
        {
            Idle = BossClips.Idle, Walk = BossClips.Walk, Run = BossClips.Run, Stun = BossClips.Stun, Roar = BossClips.Roar,
            Attacks = BossClips.Attacks, Hits = BossClips.Hits, Deaths = BossClips.Deaths, Rise = BossClips.Rise,
            Casts = new[] { "Brute_Rage", "Brute_Roar", "Brute_Roar", "Brute_Charge", "Boss_Slam", "Brute_Push" },
        };

        /// <summary>Cast clips timed by the swing of the hands (the rest — roars — play through their warning).</summary>
        private static readonly string[] SwingCasts = { "Boss_Slam", "Brute_Push" };

        public static bool IsMutant(string prefabName) => prefabName == "Mutant_Forest";

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

        /// <summary>
        /// A boss: its controller (the Mutant — its own, the rest — <see cref="BossControllerPath"/>), the head height of
        /// its body, measured run and walk, a cast timing per ability — a roar plays through the warning of its ability at
        /// about its own pace, a blow lets go when the right hand moves fastest.
        /// </summary>
        private static MonsterViewDefinition SetupBoss(GameObject prefab, MonsterDefinition definition, AnimatorController controller,
            ClipSet clips, List<string> report)
        {
            var viewPath = $"{ViewsPath}/{prefab.name}.asset";
            var view = AssetDatabase.LoadAssetAtPath<MonsterViewDefinition>(viewPath);
            if (view == null)
            {
                view = ScriptableObject.CreateInstance<MonsterViewDefinition>();
                AssetDatabase.CreateAsset(view, viewPath);
                SeedLook(view, prefab.name);
            }

            if (IsMutant(prefab.name)) SetupMutantLook(view);
            var path = AssetDatabase.GetAssetPath(prefab);
            var head = IsBigBoss(prefab.name) ? BigBossHeadHeight : BossHeadHeight;
            var scale = SetupPrefab(path, controller, view.Equipment, head);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            var runSpeed = MeasureGroundSpeed(prefab, LoadClip(clips.Run), scale);
            var walkSpeed = MeasureGroundSpeed(prefab, LoadClip(clips.Walk), scale);
            var abilities = definition.Abilities;
            var timings = new List<CastClipTiming>();
            for (var c = 0; c < clips.Casts.Length; c++)
            {
                var clip = LoadClip(clips.Casts[c]);
                float moment;
                if (Array.IndexOf(SwingCasts, clips.Casts[c]) >= 0) moment = MeasureSwing(prefab, clip).Moment;
                else
                {
                    var warning = c < abilities.Count && abilities[c] != null ? abilities[c].Warning : PhaseWarning;
                    moment = Mathf.Clamp(warning / Mathf.Max(0.05f, clip.length), 0.05f, 1f);
                }

                timings.Add(new CastClipTiming(moment, clip.length));
            }

            var first = timings[0];
            timings.RemoveAt(0);
            view.Configure(definition.Id, prefab, runSpeed, walkSpeed, first.Moment, first.Length, timings.ToArray());
            EditorUtility.SetDirty(view);
            report.Add($"  {prefab.name} ({definition.Id}): boss, scale {scale:0.00}, run {runSpeed:0.00} c/s, walk {walkSpeed:0.00} c/s, " +
                       $"casts {clips.Casts.Length}, death {LoadClip("Boss_Death").length:0.0} s.");
            return view;
        }

        /// <summary>The Mutant's boulder (kept when set: edited in the asset).</summary>
        private static void SetupMutantLook(MonsterViewDefinition view)
        {
            if (view.ProjectileModel != null) return;
            var boulder = EnsureBoulder();
            if (boulder == null) return;
            view.ConfigureLook(view.Equipment, view.SplashColor, boulder, view.ProjectileColor, 1f);
            EditorUtility.SetDirty(view);
        }

        /// <summary>Diameter of the Mutant's boulder, m (its landing circle is r 1.5).</summary>
        private const float BoulderSize = 1.1f;

        /// <summary>
        /// The boulder the Mutant throws: a rock of the Nature pack scaled down to <see cref="BoulderSize"/>, centred on
        /// the root (the projectile view draws the first mesh with its offset from the root). Made once.
        /// </summary>
        private static GameObject EnsureBoulder()
        {
            var path = $"{MutantArtPath}/Mutant_Boulder.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(BoulderModelPath);
            var filter = source != null ? source.GetComponentInChildren<MeshFilter>() : null;
            var sourceRenderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
            if (filter == null || filter.sharedMesh == null || sourceRenderer == null)
            {
                Debug.LogWarning($"[Purge] Build Monsters: '{BoulderModelPath}' is missing — the Mutant throws a blob.");
                return null;
            }

            EnsureFolder(MutantArtPath);
            var mesh = filter.sharedMesh;
            var bounds = mesh.bounds;
            var scale = BoulderSize / Mathf.Max(0.01f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)));
            var root = new GameObject("Mutant_Boulder");
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = -bounds.center * scale;
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            Material palette = null;
            foreach (var materialPath in BoulderMaterialPaths)
                palette ??= AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            model.AddComponent<MeshRenderer>().sharedMaterial = palette != null ? palette : sourceRenderer.sharedMaterial;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
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

            if (phase != null) phase.Warning = PhaseWarning;
            if (IsMutant(prefabName)) definition.SetAbilities(MutantAbilities(phase));
            else if (phase != null) definition.SetAbilities(phase);
            else definition.SetAbilities();

            EditorUtility.SetDirty(definition);
        }

        /// <summary>
        /// The Mutant's abilities (stage 8c) into a definition made before them (stage 8a: only the phase) — its phase and
        /// other numbers are kept; added once (a charge in the list = done).
        /// </summary>
        private static void EnsureMutantAbilities(MonsterDefinition definition)
        {
            PhaseAbility phase = null;
            foreach (var ability in definition.Abilities)
            {
                if (ability is ChargeAbility) return;
                if (ability is PhaseAbility existing && phase == null) phase = existing;
            }

            phase ??= new PhaseAbility
            {
                HealthBelow = 0.5f, DamageFactor = 1.5f, Rage = true, Warning = PhaseWarning, CastSound = "rage_roar",
            };
            definition.SetAbilities(MutantAbilities(phase));
            EditorUtility.SetDirty(definition);
        }

        /// <summary>
        /// Draft abilities of the Mutant (card 8c; order = priority and the cast clip of <see cref="MutantClips"/>):
        /// [phase — rage from 50 %, summon 6 forest zombies every 15 s (≤ 12 alive; in the rage 12 a cast, ≤ 24 — decision
        /// of 2026-10-26), charge (line 0.8 s), blow on the ground (a ring closing in, r 3 under the player, 1 s), boulder
        /// along an arc (circle r 1.5, every 5 s)].
        /// </summary>
        private static AbilityModule[] MutantAbilities(PhaseAbility phase)
        {
            var zombie = AssetDatabase.LoadAssetAtPath<MonsterDefinition>("Assets/_Project/Data/Monsters/Forest/Zombie_Forest.asset");
            var summon = new SummonAbility
            {
                Cooldown = 15f, FirstDelay = 4f, NeedsSight = false, MaxRange = 30f, Warning = 1.2f, Recovery = 0.4f,
                Monster = zombie, Count = 6, Radius = 4.5f, Limit = 12, UntilPhase = 0, CastSound = "summon",
            };
            var ragingSummon = new SummonAbility
            {
                Cooldown = 15f, FirstDelay = 0f, NeedsSight = false, MaxRange = 30f, Warning = 1.2f, Recovery = 0.4f,
                Monster = zombie, Count = 12, Radius = 5f, Limit = 24, FromPhase = 1, CastSound = "summon",
            };
            var charge = new ChargeAbility
            {
                Cooldown = 8f, FirstDelay = 2f, MinRange = 4f, MaxRange = 11f, NeedsSight = true, Warning = 0.8f, Recovery = 0.6f,
                Distance = 10f, Speed = 11f, Damage = 40f, Knockback = 3f, CrowdPush = 8f, CastSound = "boss_roar",
            };
            var slam = new AreaStrikeAbility
            {
                Cooldown = 7f, FirstDelay = 3f, MinRange = 0f, MaxRange = 9f, NeedsSight = true, Warning = 1f, Recovery = 0.6f,
                Radius = 3f, Damage = 30f, Closing = true, ReleaseSound = "boss_smash",
            };
            var slamLook = PackEffect("PolygonArsenal/FX_Ground_Slam");
            if (slamLook != null)
            {
                var dust = new LookEffect { Look = slamLook };
                dust.SetRadius(3f);
                slam.SetImpact(dust);
            }

            var boulder = new ProjectileAbility
            {
                Cooldown = 5f, FirstDelay = 1.5f, MinRange = 4f, MaxRange = 14f, NeedsSight = true, Warning = 0.9f, Recovery = 0.4f,
                Form = ProjectileForm.Arc, Speed = 10f, Damage = 25f, Range = 14f, ArcRadius = 1.5f, ArcHeight = 0.4f,
                ImpactSound = "boss_smash",
            };
            var boulderLook = PackEffect("PolygonArsenal/FX_Boulder_Hit");
            if (boulderLook != null)
            {
                var debris = new LookEffect { Look = boulderLook };
                debris.SetRadius(1.5f);
                boulder.SetImpact(debris);
            }

            return new AbilityModule[] { phase, summon, ragingSummon, charge, slam, boulder };
        }

        /// <summary>A built pack effect of Art/Effects (Build Pack Effects), or null when it is not built yet.</summary>
        private static UnityEngine.AddressableAssets.AssetReferenceGameObject PackEffect(string name)
        {
            var guid = AssetDatabase.AssetPathToGUID($"Assets/_Project/Art/Effects/{name}.prefab");
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[Purge] Build Monsters: effect '{name}' is not built (Build Pack Effects first).");
                return null;
            }

            return new UnityEngine.AddressableAssets.AssetReferenceGameObject(guid);
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
