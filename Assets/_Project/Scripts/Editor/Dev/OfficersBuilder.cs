using System;
using System.Collections.Generic;
using System.IO;
using Purge.Core.Content;
using Purge.Core.Drops;
using Purge.Core.Effects;
using Purge.Core.Horde;
using Purge.Core.Monsters;
using Purge.Core.Visual;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using static Purge.Editor.Dev.CharacterAnimationUtility;
using P = Purge.Presentation.Visual.MonsterAnimatorParameters;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// The officers' part of Purge → Dev → Build Monsters (stage 5c; ТЗ: офицеры — Animator, экипировка в сокетах):
    /// <list type="number">
    /// <item>Clips "Officer_*" copied once from the Ultimate Animation Collection (Bow &amp; Arrow) into Art/Animations.</item>
    /// <item>One controller for every officer (<see cref="ControllerPath"/>, parameters —
    /// <see cref="Purge.Presentation.Visual.MonsterAnimatorParameters"/>): idle / walk / run by Pace, attacks, hit
    /// reactions, stun, the roar (Alert — the Mixamo scream), the cast and four deaths; the rank-and-file "Monster_*"
    /// clips, the cast — the throw. An officer that looks different (the archer) gets an override controller in
    /// <see cref="OverridesPath"/>.</item>
    /// <item>The prefab in Art/Monster/&lt;Biome&gt;/Officer is set up in place: the controller, no root motion, a scale
    /// that makes it <see cref="HeadHeight"/> m tall at the head (a little taller than the rank-and-file), the equipment of
    /// its look on the bones (children "Equipment_*", at the model's <see cref="EquipmentModel"/> offset), opaque
    /// Standard materials → Purge/Lit.</item>
    /// <item>Look <c>Data/Monsters/Views/&lt;prefab&gt;.asset</c> (<see cref="MonsterViewDefinition"/>): prefab, measured
    /// ground speeds of walk and run, the moment the cast clip lets go (the right hand moves fastest); equipment, splash and
    /// projectile look are kept once set.</item>
    /// <item>A new definition gets the draft numbers, abilities and the officers' drop (<see cref="Seed"/>); afterwards
    /// they are edited in the asset and kept.</item>
    /// </list>
    /// The Brutes (Ram_*, stage 5d) get their own controller (<see cref="BruteControllerPath"/>) on the Creature Animset
    /// clips "Brute_*" (two casts: the shove and the roar before the charge), a head taller, a weapon in the right hand and
    /// the draft numbers: shove, charge, they break doors.
    /// </summary>
    internal static partial class OfficersBuilder
    {
        public const string ControllerPath = "Assets/_Project/Art/Monster/Officer.controller";
        public const string OverridesPath = "Assets/_Project/Art/Monster/Controllers";
        private const string ViewsPath = "Assets/_Project/Data/Monsters/Views";
        private const string OfficerDropPath = "Assets/_Project/Data/Drops/OfficerDrop.asset";
        private const string MassDropPath = "Assets/_Project/Data/Drops/HordeDrop.asset";
        private const string EffectsPath = "Assets/_Project/Data/Effects";
        private const string ClipPrefix = "Officer_";
        private const string Throw = "Officer_Throw";
        private const string Scream = "Officer_Scream";
        private const string EquipmentPrefix = "Equipment_";

        /// <summary>Head height of an officer, m (the rank-and-file: ~1.18).</summary>
        public const float HeadHeight = 1.3f;

        /// <summary>"Officer_*" clip → the Ultimate Animation Collection file it is copied from.</summary>
        private static readonly (string Name, string Source)[] ClipTable =
        {
            ("Officer_Bow_Idle", "Humanoid@IdleBreatheBowLoaded"),
            ("Officer_Bow_Walk", "Humanoid@WalkForwardBowLoaded"),
            ("Officer_Bow_Run", "Humanoid@RunForwardBowLoaded"),
            ("Officer_Bow_Shoot", "Humanoid@ShootArrowAllInOne"),
            ("Officer_Bow_Death_Front", "Humanoid@DeathFrontBow"),
            ("Officer_Bow_Death_Back", "Humanoid@DeathBackBow"),
            ("Officer_Bow_Death_Left", "Humanoid@DeathLeftBow"),
            ("Officer_Bow_Death_Right", "Humanoid@DeathRightBow"),
            // Getting up from the ground (raised by a Shaman, stage 5e).
            (Rise, "Humanoid@GetBackUpFrontCombat"),
        };

        private const string Rise = "Officer_Rise";

        private static readonly string[] Looping = { "Officer_Bow_Idle", "Officer_Bow_Walk", "Officer_Bow_Run", "Officer_Stunned" };

        /// <summary>
        /// Officers' own copies of shared clips (Art/Animations → Art/Animations): the originals bake their ground drift into
        /// the pose — attacks lunge ~1.5 m, hit reactions step back ~1 m, the roar and the throw shift too — which the
        /// baked rank-and-file subtract while baking, but an Animator would play as is (the officer slides through the
        /// player and snaps back). The copies are imported with the drift taken out (root motion, never applied).
        /// </summary>
        private static readonly (string Name, string Source)[] InPlaceCopies =
        {
            ("Officer_Attack_1", "Monster_Attack_1"), ("Officer_Attack_2", "Monster_Attack_2"), ("Officer_Attack_3", "Monster_Attack_3"),
            ("Officer_Hit_Head", "Monster_Hit_Head"), ("Officer_Hit_LeftArm", "Monster_Hit_LeftArm"),
            ("Officer_Hit_RightArm", "Monster_Hit_RightArm"), ("Officer_Stunned", "Monster_Stunned"),
            ("Officer_Scream", "Zombie_scream"), ("Officer_Throw", "Player_Throw"),
        };

        // The roar part of the scream (as Build Zombie Animations trims it): seconds of the source take.
        private const float ScreamStart = 0.45f;
        private const float ScreamEnd = 1.3f;

        // Base clips of the controller (the rank-and-file's).
        private const string Idle = "Monster_Idle";
        private const string Walk = "Monster_Walk_1";
        private const string Run = "Monster_Run_1";
        private static readonly string[] Attacks = { "Officer_Attack_1", "Officer_Attack_2", "Officer_Attack_3" };
        private static readonly string[] Hits = { "Officer_Hit_Head", "Officer_Hit_LeftArm", "Officer_Hit_RightArm" };
        private const string Stun = "Officer_Stunned";
        private static readonly string[] Deaths = { "Monster_Death_Front", "Monster_Death_Back", "Monster_Death_Left", "Monster_Death_Right" };

        /// <summary>The archer: the bow's idle, walk, run, shot and deaths instead of the base ones.</summary>
        private static readonly (string Base, string Override)[] ArcherClips =
        {
            (Idle, "Officer_Bow_Idle"), (Walk, "Officer_Bow_Walk"), (Run, "Officer_Bow_Run"), (Throw, "Officer_Bow_Shoot"),
            (Deaths[0], "Officer_Bow_Death_Front"), (Deaths[1], "Officer_Bow_Death_Back"),
            (Deaths[2], "Officer_Bow_Death_Left"), (Deaths[3], "Officer_Bow_Death_Right"),
        };

        /// <summary>The Brutes' controller (stage 5d): the same states on the Creature clips, two casts.</summary>
        public const string BruteControllerPath = "Assets/_Project/Art/Monster/Brute.controller";
        private const string BrutePrefix = "Brute_";

        /// <summary>Head height of a Brute, m: a head taller than the other officers.</summary>
        public const float BruteHeadHeight = 1.6f;

        /// <summary>"Brute_*" clip → the Ultimate Animation Collection (Creature Animset) file it is copied from.</summary>
        private static readonly (string Name, string Source)[] BruteClipTable =
        {
            ("Brute_Idle", "Humanoid@IdleBreatheCreature"),
            ("Brute_Walk", "Humanoid@WalkForward1Creature"),
            ("Brute_Run", "Humanoid@RunForward1Creature"),
            ("Brute_Attack_1", "Humanoid@AttackRightClaws1Creature"),
            ("Brute_Attack_2", "Humanoid@AttackLeftClaws1Creature"),
            ("Brute_Attack_3", "Humanoid@Attack2HitComboCreature"),
            ("Brute_Hit_Front", "Humanoid@GetHitFront1"),
            ("Brute_Hit_Left", "Humanoid@GetHitLeft"),
            ("Brute_Hit_Right", "Humanoid@GetHitRight"),
            ("Brute_Roar", "Humanoid@Roar1Creature"),
            ("Brute_Push", "Humanoid@AttackDoubleClawsCreature"),
            ("Brute_Charge", "Humanoid@Roar2Creature"),
            ("Brute_Rage", "Humanoid@Roar3Creature"),
        };

        private static readonly string[] BruteLooping = { "Brute_Idle", "Brute_Walk", "Brute_Run" };

        /// <summary>The clips a controller of this builder is made of.</summary>
        private sealed class ClipSet
        {
            public string Idle, Walk, Run, Stun, Roar;
            public string[] Attacks, Hits, Deaths;

            /// <summary>A cast clip per ability index (CastIndex); one = every ability.</summary>
            public string[] Casts;

            /// <summary>Getting up when raised (stage 5e), played by the share risen (EmergeProgress).</summary>
            public string Rise;
        }

        private static readonly ClipSet OfficerClips = new ClipSet
        {
            Idle = Idle, Walk = Walk, Run = Run, Stun = Stun, Roar = Scream, Attacks = Attacks, Hits = Hits, Deaths = Deaths,
            Casts = new[] { Throw }, Rise = Rise,
        };

        /// <summary>
        /// The Brute: abilities [rage, push, charge] — the roar of rage (stage 5e: first, nothing may delay it), the shove
        /// of both claws, the roar before the run-up.
        /// </summary>
        private static readonly ClipSet BruteClips = new ClipSet
        {
            Idle = "Brute_Idle", Walk = "Brute_Walk", Run = "Brute_Run", Stun = Stun, Roar = "Brute_Roar",
            Attacks = new[] { "Brute_Attack_1", "Brute_Attack_2", "Brute_Attack_3" },
            Hits = new[] { "Brute_Hit_Front", "Brute_Hit_Left", "Brute_Hit_Right" },
            Deaths = Deaths,
            Casts = new[] { "Brute_Rage", "Brute_Push", "Brute_Charge" },
            Rise = Rise,
        };

        public static bool IsBrute(string prefabName) => prefabName.StartsWith("Ram_", StringComparison.Ordinal);

        /// <summary>Copies the missing "Officer_*" and "Brute_*" clips and imports them; returns how many were copied.</summary>
        public static int PrepareClips(Dictionary<string, string> uac)
        {
            var copied = 0;
            foreach (var (name, source) in BruteClipTable)
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

            // The Creature's attacks and hits lunge and step: in place, as the officers' copies.
            ImportClips(BrutePrefix, name => Array.IndexOf(BruteLooping, name) >= 0, null,
                name => Array.IndexOf(BruteLooping, name) < 0);

            foreach (var (name, source) in ClipTable)
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

            foreach (var (name, source) in InPlaceCopies)
            {
                var target = $"{AnimationsPath}/{name}.fbx";
                if (AssetDatabase.LoadMainAssetAtPath(target) != null) continue;
                if (!AssetDatabase.CopyAsset($"{AnimationsPath}/{source}.fbx", target))
                    throw new InvalidOperationException($"Could not copy '{source}' to '{target}' (run Build Zombie / Player Animations first).");
                copied++;
            }

            ImportClips(ClipPrefix, name => Array.IndexOf(Looping, name) >= 0,
                name => name == Scream ? (ScreamStart, ScreamEnd) : ((float, float)?)null,
                name => name == Rise || Array.Exists(InPlaceCopies, copy => copy.Name == name));
            copied += PrepareEliteClips(uac);
            copied += PrepareBossClips(uac);
            return copied;
        }

        /// <summary>
        /// The shared assets the drafts refer to (the officers' drop, acid, slow), made before any definition: creating an
        /// asset re-imports, and a definition made just before would lose its unsaved numbers.
        /// </summary>
        public static void PrepareAssets()
        {
            EnsureOfficerDrop();
            EnsureAcidZone();
            EnsureStatus("Status_Slow", new Color(0.55f, 0.85f, 1f, 1f), new SlowStatus { SpeedFactor = 0.5f });
            // The elite's (stage 5e).
            EnsureEliteDrop();
            EnsureFreeze();
            EnsurePoisonZone();
            // The champions' affixes (stage 5e2).
            EnsureChampionAffixes();
            // The bosses' (stage 8a).
            EnsureBossDrop();
            // The gaze's stone (stage 8b).
            EnsureStone();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Sets every officer up; returns their looks.</summary>
        public static List<MonsterViewDefinition> Build(List<(GameObject Prefab, MonsterDefinition Definition)> officers,
            List<string> report)
        {
            var views = new List<MonsterViewDefinition>();
            if (officers.Count == 0) return views;
            var controller = BuildController(ControllerPath, OfficerClips);
            var brutes = BuildController(BruteControllerPath, BruteClips);
            var armadillos = BuildController(ArmadilloControllerPath, ArmadilloClips);
            var shamans = BuildController(ShamanControllerPath, ShamanClips);
            var bosses = BuildController(BossControllerPath, BossClips);
            var mutants = BuildController(MutantControllerPath, MutantClips);
            var hooks = BuildController(HookControllerPath, HookClips);
            EnsureFolder(OverridesPath);
            EnsureFolder(ViewsPath);
            foreach (var (prefab, definition) in officers)
            {
                if (IsBrute(prefab.name))
                {
                    EnsureRage(definition);
                    views.Add(SetupBrute(prefab, definition, brutes, report));
                }
                else if (IsArmadillo(prefab.name)) views.Add(SetupElite(prefab, definition, armadillos, ArmadilloClips, report));
                else if (IsShaman(prefab.name)) views.Add(SetupElite(prefab, definition, shamans, ShamanClips, report));
                else if (IsHook(prefab.name)) views.Add(SetupElite(prefab, definition, hooks, HookClips, report));
                else if (IsMutant(prefab.name))
                {
                    EnsureMutantAbilities(definition);
                    views.Add(SetupBoss(prefab, definition, mutants, MutantClips, report));
                }
                else if (IsBuiltBoss(prefab.name)) views.Add(SetupBoss(prefab, definition, bosses, BossClips, report));
                else views.Add(Setup(prefab, definition, controller, report));
            }

            return views;
        }

        /// <summary>
        /// A Brute (stage 5d): its controller, <see cref="BruteHeadHeight"/>, the weapon in its right hand; cast timings
        /// per ability — the shove lets go when the claws move fastest, the roar before the charge plays through the
        /// warning at about its own pace.
        /// </summary>
        private static MonsterViewDefinition SetupBrute(GameObject prefab, MonsterDefinition definition, AnimatorController controller,
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
            var scale = SetupPrefab(path, controller, view.Equipment, BruteHeadHeight);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            var runSpeed = MeasureGroundSpeed(prefab, LoadClip(BruteClips.Run), scale);
            var walkSpeed = MeasureGroundSpeed(prefab, LoadClip(BruteClips.Walk), scale);
            var rage = LoadClip(BruteClips.Casts[0]);
            var push = LoadClip(BruteClips.Casts[1]);
            var roar = LoadClip(BruteClips.Casts[2]);
            var pushMoment = MeasureSwing(prefab, push).Moment;
            // The charge's warning is ~0.9 s: the roar plays at about 1× and the run takes over when it ends.
            var roarMoment = Mathf.Clamp(ChargeWarning / Mathf.Max(0.05f, roar.length), 0.05f, 1f);
            // The rage roars through its warning (1 s) the same way.
            var rageMoment = Mathf.Clamp(RageWarning / Mathf.Max(0.05f, rage.length), 0.05f, 1f);
            view.Configure(definition.Id, prefab, runSpeed, walkSpeed, rageMoment, rage.length,
                new CastClipTiming(pushMoment, push.length), new CastClipTiming(roarMoment, roar.length));
            EditorUtility.SetDirty(view);
            report.Add($"  {prefab.name} ({definition.Id}): brute, scale {scale:0.00}, run {runSpeed:0.00} c/s, walk {walkSpeed:0.00} c/s, " +
                       $"push lets go at {pushMoment:0.00} of {push.length:0.00} s, roar {roar.length:0.00} s, equipment {view.Equipment.Length}.");
            return view;
        }

        private const float MaxRoarSpeed = 2f;

        /// <summary>Draft mass of a Brute (the player's is 1): the player hardly moves it, blasts barely.</summary>
        public const float BruteMass = 6f;

        /// <summary>Draft warning of the Brute's charge, s.</summary>
        private const float ChargeWarning = 0.9f;

        /// <summary>Draft roar of the rage, s (its warning).</summary>
        private const float RageWarning = 1f;

        /// <summary>
        /// The Brute's rage (stage 5e, decision of 2026-10-18): at 30 % HP it roars and hits × 1.5. Put first (nothing may
        /// delay it; its cast clip is the first) into a Brute that has none — also an existing one: its other numbers are
        /// kept, a rage is only added once.
        /// </summary>
        public static void EnsureRage(MonsterDefinition definition)
        {
            foreach (var ability in definition.Abilities)
                if (ability is RageAbility)
                    return;
            var abilities = new List<AbilityModule>(definition.Abilities);
            abilities.Insert(0,
                new RageAbility { HealthBelow = 0.3f, Warning = RageWarning, Recovery = 0.2f, DamageFactor = 1.5f, SpeedFactor = 1f, CastSound = "rage_roar" });
            definition.SetAbilities(abilities.ToArray());
            EditorUtility.SetDirty(definition);
        }

        /// <summary>Draft numbers of a new officer (stage 5c decisions; balance — stage 9).</summary>
        public static void Seed(MonsterDefinition definition, string prefabName)
        {
            var stats = definition.Stats;
            stats.Radius = 0.34f;
            stats.AttackReach = 0.9f;
            stats.CorpseTime = 4f;
            stats.EmergeTime = 0f;
            stats.SightRange = 9f;
            if (IsBuiltBoss(prefabName))
            {
                SeedBoss(definition, prefabName);
                return;
            }

            if (IsBuiltElite(prefabName))
            {
                SeedElite(definition, prefabName);
                return;
            }

            definition.SetDrop(EnsureOfficerDrop());
            if (IsBrute(prefabName))
            {
                // The Brute (stage 5d): slow and heavy, shoves a player that comes close, charges one at a distance,
                // breaks doors down.
                definition.SetPerception(MonsterPerception.Wanderer);
                stats.MaxHp = 400f;
                stats.Speed = 1.8f;
                stats.Radius = 0.45f;
                stats.AttackReach = 1.1f;
                stats.Damage = 18;
                stats.AttackInterval = 1.6f;
                stats.TurnSpeed = 300f;
                stats.Mass = BruteMass;
                stats.Doors = MonsterDoorUse.Break;
                stats.DoorDamage = 20f;
                var shove = new PushAbility
                {
                    Cooldown = 5f, FirstDelay = 0.5f, MinRange = 0f, MaxRange = 1.8f, NeedsSight = true, Warning = 0.7f, Recovery = 0.4f,
                    Radius = 2.2f, Damage = 12f, Knockback = 2.5f,
                };
                var charge = new ChargeAbility
                {
                    Cooldown = 7f, FirstDelay = 1f, MinRange = 3f, MaxRange = 9f, NeedsSight = true, Warning = ChargeWarning, Recovery = 0.6f,
                    Distance = 9f, Speed = 10f, Damage = 25f, Knockback = 2.5f, CrowdPush = 7f,
                };
                // Order = priority and the cast clip (BruteClips.Casts): the rage, the shove, the charge.
                definition.SetAbilities(shove, charge);
                EnsureRage(definition);
                EditorUtility.SetDirty(definition);
                return;
            }
            if (prefabName.StartsWith("Shouter", StringComparison.Ordinal))
            {
                // The caller: notices the player, screams once (the call) and runs at them itself.
                definition.SetPerception(MonsterPerception.Caller);
                stats.MaxHp = 120f;
                stats.Speed = 2f;
                stats.Damage = 10;
                stats.AttackInterval = 1.3f;
                stats.CallRadius = 14f;
                definition.SetAbilities();
                EditorUtility.SetDirty(definition);
                return;
            }

            if (!prefabName.StartsWith("Distant", StringComparison.Ordinal)) return;
            // The ranged one: shoots from 6–8 cells, steps back from a player closer than 3, in melee at a wall.
            definition.SetPerception(MonsterPerception.Wanderer);
            stats.MaxHp = 70f;
            stats.Speed = 2.2f;
            stats.Damage = 6;
            stats.AttackInterval = 1.2f;
            stats.HoldRange = 7f;
            stats.RetreatRange = 3f;
            var shot = new ProjectileAbility { Cooldown = 2.5f, FirstDelay = 0.8f, MinRange = 1.5f, MaxRange = 9f, Warning = 0.8f };
            if (prefabName.EndsWith("_Forest", StringComparison.Ordinal))
            {
                // Spits acid along an arc: a puddle where the player stood.
                shot.Form = ProjectileForm.Arc;
                shot.ReleaseSound = "spit";
                shot.Speed = 9f;
                shot.Damage = 8f;
                shot.ArcRadius = 0.9f;
                shot.Cooldown = 3.5f;
                shot.Warning = 0.9f;
                shot.SetImpact(new PlaceZoneEffect { Zone = EnsureAcidZone(), Duration = 5f, Strength = 1f });
                ((PlaceZoneEffect)shot.Impact[0]).SetRadius(1.2f);
            }
            else if (prefabName.EndsWith("_Snow", StringComparison.Ordinal))
            {
                // A fan of three icicles: they slow.
                shot.Form = ProjectileForm.Fan;
                shot.ReleaseSound = "icicles";
                shot.Count = 3;
                shot.FanAngle = 30f;
                shot.Speed = 12f;
                shot.Damage = 10f;
                shot.Cooldown = 3f;
                shot.Status = EnsureStatus("Status_Slow", new Color(0.55f, 0.85f, 1f, 1f), new SlowStatus { SpeedFactor = 0.5f });
                shot.StatusDuration = 2f;
            }
            else
            {
                // The skeleton archer: one arrow, damage only.
                shot.Form = ProjectileForm.Straight;
                shot.ReleaseSound = "arrow";
                shot.Speed = 14f;
                shot.Damage = 18f;
                shot.Range = 12f;
            }

            definition.SetAbilities(shot);
            EditorUtility.SetDirty(definition);
        }

        private static MonsterViewDefinition Setup(GameObject prefab, MonsterDefinition definition, AnimatorController controller,
            List<string> report)
        {
            var viewPath = $"{ViewsPath}/{prefab.name}.asset";
            var view = AssetDatabase.LoadAssetAtPath<MonsterViewDefinition>(viewPath);
            var created = view == null;
            if (created)
            {
                view = ScriptableObject.CreateInstance<MonsterViewDefinition>();
                AssetDatabase.CreateAsset(view, viewPath);
                SeedLook(view, prefab.name);
            }

            var archer = prefab.name == "Distant_Castle";
            RuntimeAnimatorController used = controller;
            if (archer) used = Override(controller, prefab.name, ArcherClips);

            var path = AssetDatabase.GetAssetPath(prefab);
            var scale = SetupPrefab(path, used, view.Equipment);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            var walk = LoadClip(archer ? "Officer_Bow_Walk" : Walk);
            var run = LoadClip(archer ? "Officer_Bow_Run" : Run);
            var cast = LoadClip(archer ? "Officer_Bow_Shoot" : Throw);
            var runSpeed = MeasureGroundSpeed(prefab, run, scale);
            var walkSpeed = MeasureGroundSpeed(prefab, walk, scale);
            // A throw lets go when the hand moves fastest; a bow — at the full draw (the hands farthest apart).
            var moment = archer ? MeasureFullDraw(prefab, cast) : MeasureSwing(prefab, cast).Moment;
            view.Configure(definition.Id, prefab, runSpeed, walkSpeed, moment, cast.length);
            EditorUtility.SetDirty(view);
            report.Add($"  {prefab.name} ({definition.Id}): officer, scale {scale:0.00}, run {runSpeed:0.00} c/s, walk " +
                       $"{walkSpeed:0.00} c/s, cast lets go at {moment:0.00} of {cast.length:0.00} s, equipment {view.Equipment.Length}" +
                       (archer ? ", bow clips" : "") + ".");
            return view;
        }

        /// <summary>
        /// Share of a bow clip at which the arrow leaves: the hands are farthest apart in its second half (the first is
        /// the arrow taken from the quiver).
        /// </summary>
        private static float MeasureFullDraw(GameObject model, AnimationClip clip)
        {
            const int samples = 60;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            AnimationMode.StartAnimationMode();
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = instance.GetComponent<Animator>();
                var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var best = -1f;
                var moment = 0.75f;
                for (var i = samples / 2; i <= samples; i++)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(instance, clip, clip.length * i / samples);
                    AnimationMode.EndSampling();
                    var distance = Vector3.Distance(right.position, left.position);
                    if (distance <= best) continue;
                    best = distance;
                    moment = i / (float)samples;
                }

                return moment;
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Equipment, splash and projectile of a new look (then kept: edited in the asset).</summary>
        private static void SeedLook(MonsterViewDefinition view, string prefabName)
        {
            var equipment = new List<HordeEquipment>();
            GameObject projectile = null;
            var color = new Color(0.6f, 1f, 0.3f, 1f);
            var size = 0.35f;
            var splash = new Color(0.55f, 0.85f, 0.25f, 1f);
            switch (prefabName)
            {
                case "Distant_Castle":
                    equipment.Add(new HordeEquipment { Bone = HumanBodyBones.LeftHand, Model = Weapon("Distant/SM_Prop_Bow_02") });
                    equipment.Add(new HordeEquipment { Bone = HumanBodyBones.Chest, Model = Weapon("Distant/SM_Prop_Quiver_02") });
                    projectile = Weapon("Distant/SM_Prop_Arrow_02_Optimized");
                    splash = new Color(0.9f, 0.88f, 0.8f, 1f);
                    break;
                case "Distant_Forest":
                    color = new Color(0.55f, 1f, 0.2f, 1f);
                    size = 0.4f;
                    break;
                case "Distant_Snow":
                    color = new Color(0.65f, 0.9f, 1f, 1f);
                    size = 0.3f;
                    splash = new Color(0.6f, 0.75f, 1f, 1f);
                    break;
                case "Shouter_Common":
                    splash = new Color(0.75f, 0.2f, 0.15f, 1f);
                    break;
                case "Ram_Castle":
                    // The orc: a two-handed hammer.
                    equipment.Add(new HordeEquipment { Bone = HumanBodyBones.RightHand, Model = Weapon("TwoHand/SM_Wep_Hammer_05") });
                    splash = new Color(0.45f, 0.6f, 0.25f, 1f);
                    break;
                case "Ram_Forest":
                    // The goat demon: an axe.
                    equipment.Add(new HordeEquipment { Bone = HumanBodyBones.RightHand, Model = Weapon("OneHand/SM_Wep_Axe_02") });
                    splash = new Color(0.7f, 0.25f, 0.15f, 1f);
                    break;
                case "Ram_Snow":
                    // The yeti: a club.
                    equipment.Add(new HordeEquipment { Bone = HumanBodyBones.RightHand, Model = Weapon("OneHand/SM_Wep_Troll_01") });
                    splash = new Color(0.75f, 0.85f, 1f, 1f);
                    break;
                case "Armadillo_Common":
                    // A shield in the left hand (its armored front), a sword in the right.
                    equipment.Add(new HordeEquipment { Bone = HumanBodyBones.LeftHand, Model = Weapon("Shield/SM_Wep_Shield_04") });
                    equipment.Add(new HordeEquipment { Bone = HumanBodyBones.RightHand, Model = Weapon("OneHand/SM_Wep_Sword_09") });
                    splash = new Color(0.6f, 0.55f, 0.45f, 1f);
                    break;
                case "Shaman_Castle":
                case "Shaman_Forest":
                case "Shaman_Snow":
                    equipment.Add(new HordeEquipment { Bone = HumanBodyBones.RightHand, Model = Weapon("Magic/SM_Wep_Staff_09") });
                    splash = prefabName == "Shaman_Snow" ? new Color(0.7f, 0.8f, 1f, 1f) : new Color(0.55f, 0.75f, 0.3f, 1f);
                    break;
                case "Capture_Castle":
                case "Capture_Snow":
                    // A sickle in the hand, a sickle at the end of the chain (stage 8b).
                    equipment.Add(new HordeEquipment { Bone = HumanBodyBones.RightHand, Model = Weapon("Capture/SM_Wep_Sickle_01") });
                    projectile = Weapon("Capture/SM_Wep_Sickle_01");
                    size = 1f;
                    splash = prefabName == "Capture_Snow" ? new Color(0.7f, 0.8f, 1f, 1f) : new Color(0.7f, 0.25f, 0.2f, 1f);
                    break;
                case "Capture_Forest":
                    // The vine's end at the end of the vine.
                    equipment.Add(new HordeEquipment { Bone = HumanBodyBones.RightHand, Model = Weapon("Capture/SM_Wep_Sickle_01") });
                    projectile = Weapon("Capture/SM_Tree_Vines_01");
                    size = 0.35f;
                    splash = new Color(0.55f, 0.75f, 0.3f, 1f);
                    break;
            }

            equipment.RemoveAll(item => item.Model == null);
            view.ConfigureLook(equipment.ToArray(), splash, projectile, color, size);
        }

        private static GameObject Weapon(string path) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Weapons/Purge/For_monsters/{path}.prefab");

        /// <summary>
        /// The prefab in place: controller, no root motion, the scale for <see cref="HeadHeight"/>, the equipment on its
        /// bones, Standard → Purge/Lit. Returns the scale.
        /// </summary>
        private static float SetupPrefab(string path, RuntimeAnimatorController controller, HordeEquipment[] equipment,
            float headHeight = HeadHeight)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var animator = root.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                    throw new InvalidOperationException($"'{path}' needs a Humanoid Animator on its root.");
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                // Views hidden by visibility are deactivated; on reveal they continue (a corpse stays dead).
                animator.keepAnimatorStateOnDisable = true;

                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                var unscaled = head.position.y / Mathf.Max(1e-3f, root.transform.localScale.x);
                var scale = headHeight / Mathf.Max(0.1f, unscaled);
                root.transform.localScale = Vector3.one * scale;

                // Equipment: what the look lists, nothing else.
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    if (child != null && child.name.StartsWith(EquipmentPrefix, StringComparison.Ordinal))
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                foreach (var item in equipment)
                {
                    if (item?.Model == null) continue;
                    var bone = animator.GetBoneTransform(item.Bone);
                    if (bone == null)
                    {
                        Debug.LogWarning($"[Purge] Equipment '{item.Model.name}' of '{path}': no bone {item.Bone} — skipped.");
                        continue;
                    }

                    var offset = EnsureEquipmentOffset(item.Model);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(item.Model, bone);
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    instance.name = EquipmentPrefix + item.Model.name;
                    instance.transform.localPosition = offset.GetColumn(3);
                    instance.transform.localRotation = offset.rotation;
                    instance.transform.localScale = offset.lossyScale;
                    var component = instance.GetComponent<EquipmentModel>();
                    if (component != null) UnityEngine.Object.DestroyImmediate(component);
                }

                ToPurgeLit(root, path);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return scale;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Matrix4x4 EnsureEquipmentOffset(GameObject model)
        {
            var component = model.GetComponent<EquipmentModel>();
            if (component != null) return component.Offset;
            var path = AssetDatabase.GetAssetPath(model);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                contents.AddComponent<EquipmentModel>();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<EquipmentModel>().Offset;
        }

        /// <summary>Opaque Standard materials of the model and its equipment → Purge/Lit (others are reported).</summary>
        private static void ToPurgeLit(GameObject root, string path)
        {
            var lit = Shader.Find("Purge/Lit");
            if (lit == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == lit) continue;
                if (material.shader != null && material.shader.name == "SyntyStudios/Blood")
                {
                    // Synty's bloodied characters (the orc Brute): the clean base texture only — the style has no blood.
                    var texture = material.GetTexture("_Texture");
                    material.shader = lit;
                    material.shaderKeywords = new string[0];
                    material.renderQueue = -1;
                    material.mainTexture = texture;
                    material.color = Color.white;
                    EditorUtility.SetDirty(material);
                    continue;
                }

                if (material.shader == null || material.shader.name != "Standard" ||
                    material.HasProperty("_Mode") && material.GetFloat("_Mode") > 0.5f)
                {
                    if (renderer.gameObject.activeInHierarchy)
                        Debug.LogWarning($"[Purge] '{path}': material '{material.name}' ({material.shader?.name}) is not opaque " +
                                         "Standard — left as it is.");
                    continue;
                }

                material.shader = lit;
                material.shaderKeywords = new string[0];
                material.renderQueue = -1;
                EditorUtility.SetDirty(material);
            }
        }

        /// <summary>
        /// The officers' controller (rebuilt in place, same asset): idle / walk / run by Pace (playback by the speed),
        /// from any state — death (by side), stun, cast, roar, attacks, hit reactions.
        /// </summary>
        private static AnimatorController BuildController(string path, ClipSet clips)
        {
            var controller = CreateOrClearController(path);
            controller.AddParameter(P.Pace, AnimatorControllerParameterType.Int);
            AddFloat(controller, P.Playback, 1f);
            controller.AddParameter(P.Attack, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(P.AttackIndex, AnimatorControllerParameterType.Int);
            AddFloat(controller, P.AttackSpeed, 1f);
            controller.AddParameter(P.Hit, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(P.HitIndex, AnimatorControllerParameterType.Int);
            controller.AddParameter(P.Stunned, AnimatorControllerParameterType.Bool);
            controller.AddParameter(P.Alert, AnimatorControllerParameterType.Bool);
            controller.AddParameter(P.Cast, AnimatorControllerParameterType.Bool);
            AddFloat(controller, P.CastSpeed, 1f);
            controller.AddParameter(P.CastIndex, AnimatorControllerParameterType.Int);
            controller.AddParameter(P.Dead, AnimatorControllerParameterType.Bool);
            controller.AddParameter(P.DeathIndex, AnimatorControllerParameterType.Int);
            controller.AddParameter(P.Emerging, AnimatorControllerParameterType.Bool);
            AddFloat(controller, P.EmergeProgress, 0f);

            var machine = controller.layers[0].stateMachine;
            var idle = machine.AddState("Idle", new Vector3(300f, 0f));
            idle.motion = LoadClip(clips.Idle);
            machine.defaultState = idle;
            var walk = Locomotion(machine, "Walk", clips.Walk, new Vector3(300f, 120f));
            var run = Locomotion(machine, "Run", clips.Run, new Vector3(300f, 240f));
            var paces = new[] { idle, walk, run };
            for (var from = 0; from < paces.Length; from++)
            for (var to = 0; to < paces.Length; to++)
                if (from != to)
                    Transition(paces[from], paces[to], 0.2f).AddCondition(AnimatorConditionMode.Equals, to, P.Pace);

            // Any state, in priority: death, stun, cast, roar, attack, hit.
            for (var d = 0; d < clips.Deaths.Length; d++)
            {
                var death = machine.AddState("Death" + (d + 1), new Vector3(700f, d * 60f));
                death.motion = LoadClip(clips.Deaths[d]);
                var enter = Any(machine, death, 0.1f);
                enter.AddCondition(AnimatorConditionMode.If, 0f, P.Dead);
                enter.AddCondition(AnimatorConditionMode.Equals, d, P.DeathIndex);
            }

            if (clips.Rise != null)
            {
                // Raised (stage 5e): gets up from the ground, the clip's time = the share risen.
                var rise = machine.AddState("Rise", new Vector3(0f, -120f));
                rise.motion = LoadClip(clips.Rise);
                rise.timeParameter = P.EmergeProgress;
                rise.timeParameterActive = true;
                var toRise = Any(machine, rise, 0f);
                toRise.AddCondition(AnimatorConditionMode.If, 0f, P.Emerging);
                toRise.AddCondition(AnimatorConditionMode.IfNot, 0f, P.Dead);
                Transition(rise, idle, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0f, P.Emerging);
            }

            var stun = machine.AddState("Stun", new Vector3(0f, 0f));
            stun.motion = LoadClip(clips.Stun);
            var toStun = Any(machine, stun, 0.15f);
            toStun.AddCondition(AnimatorConditionMode.If, 0f, P.Stunned);
            toStun.AddCondition(AnimatorConditionMode.IfNot, 0f, P.Dead);
            Transition(stun, idle, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, P.Stunned);

            // A cast clip per ability index (one clip = for every ability).
            for (var c = 0; c < clips.Casts.Length; c++)
            {
                var cast = machine.AddState(clips.Casts.Length > 1 ? "Cast" + (c + 1) : "Cast", new Vector3(0f, 120f + c * 60f));
                cast.motion = LoadClip(clips.Casts[c]);
                cast.speedParameter = P.CastSpeed;
                cast.speedParameterActive = true;
                var toCast = Any(machine, cast, 0.1f);
                toCast.AddCondition(AnimatorConditionMode.If, 0f, P.Cast);
                if (clips.Casts.Length > 1) toCast.AddCondition(AnimatorConditionMode.Equals, c, P.CastIndex);
                toCast.AddCondition(AnimatorConditionMode.IfNot, 0f, P.Dead);
                toCast.AddCondition(AnimatorConditionMode.IfNot, 0f, P.Stunned);
                Transition(cast, idle, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0f, P.Cast);
            }

            // The roar: squeezed into the alert.
            var roar = machine.AddState("Roar", new Vector3(0f, 360f));
            roar.motion = LoadClip(clips.Roar);
            // A long roar (the Creature's, 2.7 s) is not squeezed past 2×: its start plays, then idle.
            roar.speed = Mathf.Min(MaxRoarSpeed, roar.motion.averageDuration / Purge.Gameplay.Horde.HordePerception.AlertTime);
            var toRoar = Any(machine, roar, 0.1f);
            toRoar.AddCondition(AnimatorConditionMode.If, 0f, P.Alert);
            toRoar.AddCondition(AnimatorConditionMode.IfNot, 0f, P.Dead);
            Transition(roar, idle, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, P.Alert);

            for (var a = 0; a < clips.Attacks.Length; a++)
            {
                var attack = machine.AddState("Attack" + (a + 1), new Vector3(1000f, a * 80f));
                attack.motion = LoadClip(clips.Attacks[a]);
                // At AttackSpeed = 1 the state lasts 1 s; the presenter sets 1 / attack interval.
                attack.speed = attack.motion.averageDuration;
                attack.speedParameter = P.AttackSpeed;
                attack.speedParameterActive = true;
                var enter = Any(machine, attack, 0.1f);
                enter.canTransitionToSelf = true;
                enter.AddCondition(AnimatorConditionMode.If, 0f, P.Attack);
                enter.AddCondition(AnimatorConditionMode.Equals, a, P.AttackIndex);
                enter.AddCondition(AnimatorConditionMode.IfNot, 0f, P.Dead);
                Back(attack, idle, 0.9f);
            }

            for (var h = 0; h < clips.Hits.Length; h++)
            {
                var hit = machine.AddState("Hit" + (h + 1), new Vector3(1000f, 300f + h * 80f));
                hit.motion = LoadClip(clips.Hits[h]);
                var enter = Any(machine, hit, 0.08f);
                enter.canTransitionToSelf = true;
                enter.AddCondition(AnimatorConditionMode.If, 0f, P.Hit);
                enter.AddCondition(AnimatorConditionMode.Equals, h, P.HitIndex);
                enter.AddCondition(AnimatorConditionMode.IfNot, 0f, P.Dead);
                enter.AddCondition(AnimatorConditionMode.IfNot, 0f, P.Cast);
                Back(hit, idle, 0.6f);
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorState Locomotion(AnimatorStateMachine machine, string name, string clip, Vector3 position)
        {
            var state = machine.AddState(name, position);
            state.motion = LoadClip(clip);
            state.speedParameter = P.Playback;
            state.speedParameterActive = true;
            return state;
        }

        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            var transition = from.AddTransition(to);
            Configure(transition, duration);
            return transition;
        }

        private static AnimatorStateTransition Any(AnimatorStateMachine machine, AnimatorState to, float duration)
        {
            var transition = machine.AddAnyStateTransition(to);
            Configure(transition, duration);
            transition.canTransitionToSelf = false;
            return transition;
        }

        /// <summary>Back to idle near the end of a one-shot clip (the paces take it from there).</summary>
        private static void Back(AnimatorState from, AnimatorState idle, float exitTime)
        {
            var back = from.AddTransition(idle);
            back.hasExitTime = true;
            back.exitTime = exitTime;
            back.duration = 0.15f;
            back.hasFixedDuration = true;
        }

        /// <summary>An override controller of the officers' one with some clips replaced (rebuilt in place).</summary>
        private static AnimatorOverrideController Override(AnimatorController controller, string name,
            (string Base, string Override)[] clips)
        {
            var path = $"{OverridesPath}/{name}.overrideController";
            var result = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (result == null)
            {
                result = new AnimatorOverrideController(controller);
                AssetDatabase.CreateAsset(result, path);
            }

            result.runtimeAnimatorController = controller;
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            result.GetOverrides(overrides);
            for (var i = 0; i < overrides.Count; i++)
            {
                var original = overrides[i].Key;
                AnimationClip replacement = null;
                foreach (var (from, to) in clips)
                    if (original.name == from)
                        replacement = LoadClip(to);
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, replacement);
            }

            result.ApplyOverrides(overrides);
            EditorUtility.SetDirty(result);
            return result;
        }

        private static DropTable EnsureOfficerDrop()
        {
            var drop = AssetDatabase.LoadAssetAtPath<DropTable>(OfficerDropPath);
            if (drop != null) return drop;
            // The rank-and-file's table with richer odds (ТЗ: ранг определяет дроп).
            if (!AssetDatabase.CopyAsset(MassDropPath, OfficerDropPath))
                throw new InvalidOperationException($"Could not copy '{MassDropPath}' to '{OfficerDropPath}'.");
            drop = AssetDatabase.LoadAssetAtPath<DropTable>(OfficerDropPath);
            drop.Configure(ContentIds.FromName("OfficerDrop"), nothing: 25f, ammo: 35f, medkit: 18f, throwable: 12f, weapon: 10f);
            EditorUtility.SetDirty(drop);
            return drop;
        }

        private static StatusDefinition EnsureStatus(string name, Color color, params StatusModule[] modules)
        {
            var path = $"{EffectsPath}/{name}.asset";
            var status = AssetDatabase.LoadAssetAtPath<StatusDefinition>(path);
            if (status != null) return status;
            status = ScriptableObject.CreateInstance<StatusDefinition>();
            status.Configure(ContentIds.FromName(name), modules);
            AssetDatabase.CreateAsset(status, path);
            var serialized = new SerializedObject(status);
            serialized.FindProperty("_color").colorValue = color;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return status;
        }

        /// <summary>Acid (stage 5c): a puddle whose state eats away at those in it and a moment after.</summary>
        private static ZoneDefinition EnsureAcidZone()
        {
            const string path = EffectsPath + "/Zone_Acid.asset";
            var zone = AssetDatabase.LoadAssetAtPath<ZoneDefinition>(path);
            if (zone != null) return zone;
            var acid = EnsureStatus("Status_Acid", new Color(0.55f, 1f, 0.2f, 1f), new DamageOverTimeStatus { DamagePerSecond = 12f });
            zone = ScriptableObject.CreateInstance<ZoneDefinition>();
            zone.Configure(ContentIds.FromName("Zone_Acid"), new ZoneStatus { Status = acid, Duration = 1.5f });
            AssetDatabase.CreateAsset(zone, path);
            var serialized = new SerializedObject(zone);
            serialized.FindProperty("_core").colorValue = new Color(0.75f, 1f, 0.3f, 1f);
            serialized.FindProperty("_edge").colorValue = new Color(0.25f, 0.7f, 0.1f, 1f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return zone;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
