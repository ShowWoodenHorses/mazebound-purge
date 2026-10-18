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
    /// The elite of Build Monsters (stage 5e; ТЗ: элита — Броненосец общий, Шаман — механика общая, модель биомная):
    /// <list type="bullet">
    /// <item><b>Armadillo</b> (Art/Monster/Common/Special/Armadillo_*): Sword&amp;Shield clips "Elite_Shield_*", its own
    /// controller, a shield in the left hand and a sword in the right; half the damage from the front
    /// (<see cref="MonsterStats.FrontArmor"/>).</item>
    /// <item><b>Shaman</b> (Shaman_&lt;Biome&gt;): Wizard clips "Elite_Wizard_*", a staff; keeps its distance, raises the
    /// fallen (<see cref="RaiseAbility"/>) and strikes the player's spot after a delay (<see cref="AreaStrikeAbility"/>):
    /// the Castle — a fireball (explosion), the Forest — a poison cloud (zone), the Snow — a freeze (state).</item>
    /// </list>
    /// Clips are taken in place like the officers' (attacks, hits, the rise: the drift goes into root motion, never
    /// applied; the presenter keeps the hips at the monster's point). Other "Special" prefabs (the Hook) wait for their stage.
    /// </summary>
    internal static partial class OfficersBuilder
    {
        public const string ArmadilloControllerPath = "Assets/_Project/Art/Monster/Armadillo.controller";
        public const string ShamanControllerPath = "Assets/_Project/Art/Monster/Shaman.controller";
        private const string EliteDropPath = "Assets/_Project/Data/Drops/EliteDrop.asset";
        private const string ElitePrefix = "Elite_";

        /// <summary>Head height of the elite, m: between the officers (1.3) and the Brute (1.6).</summary>
        public const float EliteHeadHeight = 1.45f;

        /// <summary>"Elite_*" clip → the Ultimate Animation Collection file it is copied from.</summary>
        private static readonly (string Name, string Source)[] EliteClipTable =
        {
            ("Elite_Shield_Idle", "Humanoid@IdleCombatS&S"),
            ("Elite_Shield_Walk", "Humanoid@WalkForwardS&S"),
            ("Elite_Shield_Run", "Humanoid@RunForwardS&S"),
            ("Elite_Shield_Attack_1", "Humanoid@SwordAttack1S&S"),
            ("Elite_Shield_Attack_2", "Humanoid@ShieldAttack1S&S"),
            ("Elite_Shield_Attack_3", "Humanoid@SwordAttack2S&S"),
            ("Elite_Shield_Hit_Front", "Humanoid@GetHitLightFrontS&S"),
            ("Elite_Shield_Hit_Left", "Humanoid@GetHitLightLeftS&S"),
            ("Elite_Shield_Hit_Right", "Humanoid@GetHitLightRightS&S"),
            ("Elite_Shield_Taunt", "Humanoid@Taunt1S&S"),
            ("Elite_Shield_Death_Front", "Humanoid@DeathFrontS&S"),
            ("Elite_Shield_Death_Back", "Humanoid@DeathBackS&S"),
            ("Elite_Shield_Death_Left", "Humanoid@DeathLeftS&S"),
            ("Elite_Shield_Death_Right", "Humanoid@DeathRightS&S"),
            ("Elite_Shield_Rise", "Humanoid@GetBackUpFrontS&S"),

            ("Elite_Wizard_Idle", "Humanoid@IdleWizard"),
            ("Elite_Wizard_Walk", "Humanoid@WalkForwardWizard"),
            ("Elite_Wizard_Run", "Humanoid@RunForwardWizard"),
            // Measured 2026-10-18 (hands over the head, reach forward): no melee in the Wizard set — the plain attack is
            // a forward spell squeezed into the attack interval.
            ("Elite_Wizard_Attack_1", "Humanoid@CastSpell5Wizard"),
            ("Elite_Wizard_Attack_2", "Humanoid@CastSpell2Wizard"),
            ("Elite_Wizard_Attack_3", "Humanoid@CastSpell12Wizard"),
            ("Elite_Wizard_Hit_Front", "Humanoid@GetHitFrontLightWizard"),
            ("Elite_Wizard_Hit_Left", "Humanoid@GetHitLeftLightWizard"),
            ("Elite_Wizard_Hit_Right", "Humanoid@GetHitRightLightWizard"),
            ("Elite_Wizard_Roar", "Humanoid@CastSpell10Wizard"),
            // Both hands over the head (4.3 s) — the raise; a short thrust forward (2.2 s) — the area strike.
            ("Elite_Wizard_Raise", "Humanoid@CastSpell4Wizard"),
            ("Elite_Wizard_Area", "Humanoid@CastSpell6Wizard"),
            ("Elite_Wizard_Death_Front", "Humanoid@DeathFrontWizard"),
            ("Elite_Wizard_Death_Back", "Humanoid@DeathBackWizard"),
            ("Elite_Wizard_Death_Left", "Humanoid@DeathLeftWizard"),
            ("Elite_Wizard_Death_Right", "Humanoid@DeathRightWizard"),
            ("Elite_Wizard_Rise", "Humanoid@GetBackUpFrontWizard"),
        };

        private static readonly string[] EliteLooping =
        {
            "Elite_Shield_Idle", "Elite_Shield_Walk", "Elite_Shield_Run", "Elite_Wizard_Idle", "Elite_Wizard_Walk", "Elite_Wizard_Run",
        };

        /// <summary>The Armadillo: no abilities — its plain attacks with the sword and the shield.</summary>
        private static readonly ClipSet ArmadilloClips = new ClipSet
        {
            Idle = "Elite_Shield_Idle", Walk = "Elite_Shield_Walk", Run = "Elite_Shield_Run", Stun = Stun, Roar = "Elite_Shield_Taunt",
            Attacks = new[] { "Elite_Shield_Attack_1", "Elite_Shield_Attack_2", "Elite_Shield_Attack_3" },
            Hits = new[] { "Elite_Shield_Hit_Front", "Elite_Shield_Hit_Left", "Elite_Shield_Hit_Right" },
            Deaths = new[] { "Elite_Shield_Death_Front", "Elite_Shield_Death_Back", "Elite_Shield_Death_Left", "Elite_Shield_Death_Right" },
            Casts = Array.Empty<string>(),
            Rise = "Elite_Shield_Rise",
        };

        /// <summary>The Shaman: abilities [raise, area strike].</summary>
        private static readonly ClipSet ShamanClips = new ClipSet
        {
            Idle = "Elite_Wizard_Idle", Walk = "Elite_Wizard_Walk", Run = "Elite_Wizard_Run", Stun = Stun, Roar = "Elite_Wizard_Roar",
            Attacks = new[] { "Elite_Wizard_Attack_1", "Elite_Wizard_Attack_2", "Elite_Wizard_Attack_3" },
            Hits = new[] { "Elite_Wizard_Hit_Front", "Elite_Wizard_Hit_Left", "Elite_Wizard_Hit_Right" },
            Deaths = new[] { "Elite_Wizard_Death_Front", "Elite_Wizard_Death_Back", "Elite_Wizard_Death_Left", "Elite_Wizard_Death_Right" },
            Casts = new[] { "Elite_Wizard_Raise", "Elite_Wizard_Area" },
            Rise = "Elite_Wizard_Rise",
        };

        public static bool IsArmadillo(string prefabName) => prefabName.StartsWith("Armadillo", StringComparison.Ordinal);
        public static bool IsShaman(string prefabName) => prefabName.StartsWith("Shaman", StringComparison.Ordinal);

        /// <summary>The elite this stage builds (the Hook — later).</summary>
        public static bool IsBuiltElite(string prefabName) => IsArmadillo(prefabName) || IsShaman(prefabName);

        /// <summary>Copies the missing "Elite_*" clips and imports them (in place but the loops); returns how many were copied.</summary>
        private static int PrepareEliteClips(Dictionary<string, string> uac)
        {
            var copied = 0;
            foreach (var (name, source) in EliteClipTable)
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

            // Attacks lunge, hits step back, the rise crawls: in place, as the officers' copies; deaths fall away.
            ImportClips(ElitePrefix, name => Array.IndexOf(EliteLooping, name) >= 0, null,
                name => Array.IndexOf(EliteLooping, name) < 0 && name.IndexOf("_Death_", StringComparison.Ordinal) < 0);
            return copied;
        }

        /// <summary>
        /// An elite monster: its controller, <see cref="EliteHeadHeight"/>, the equipment of its look; a cast timing per
        /// ability (the clip lets go when the right hand moves fastest).
        /// </summary>
        private static MonsterViewDefinition SetupElite(GameObject prefab, MonsterDefinition definition, AnimatorController controller,
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

            var path = AssetDatabase.GetAssetPath(prefab);
            var scale = SetupPrefab(path, controller, view.Equipment, EliteHeadHeight);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            var runSpeed = MeasureGroundSpeed(prefab, LoadClip(clips.Run), scale);
            var walkSpeed = MeasureGroundSpeed(prefab, LoadClip(clips.Walk), scale);
            var timings = new List<CastClipTiming>();
            foreach (var name in clips.Casts)
            {
                var clip = LoadClip(name);
                timings.Add(new CastClipTiming(MeasureSwing(prefab, clip).Moment, clip.length));
            }

            var first = timings.Count > 0 ? timings[0] : new CastClipTiming(0.5f, 1f);
            if (timings.Count > 0) timings.RemoveAt(0);
            view.Configure(definition.Id, prefab, runSpeed, walkSpeed, first.Moment, first.Length, timings.ToArray());
            EditorUtility.SetDirty(view);
            report.Add($"  {prefab.name} ({definition.Id}): elite, scale {scale:0.00}, run {runSpeed:0.00} c/s, walk {walkSpeed:0.00} c/s, " +
                       $"casts {clips.Casts.Length}, equipment {view.Equipment.Length}.");
            return view;
        }

        /// <summary>Draft numbers of a new elite monster (stage 5e decisions of 2026-10-18; balance — stage 9).</summary>
        private static void SeedElite(MonsterDefinition definition, string prefabName)
        {
            var stats = definition.Stats;
            stats.CorpseTime = 4f;
            stats.EmergeTime = 0f;
            stats.SightRange = 9f;
            definition.SetDrop(EnsureEliteDrop());
            definition.SetPerception(MonsterPerception.Wanderer);
            if (IsArmadillo(prefabName))
            {
                // Slow, heavy, half the damage from the front: walk round it, or hit it with zones and fire.
                stats.MaxHp = 320f;
                stats.Speed = 1.7f;
                stats.Radius = 0.42f;
                stats.AttackReach = 1f;
                stats.Damage = 14;
                stats.AttackInterval = 1.4f;
                stats.TurnSpeed = 220f;
                stats.FrontArmor = 0.5f;
                stats.FrontArc = 120f;
                definition.SetAbilities();
                EditorUtility.SetDirty(definition);
                return;
            }

            // The Shaman: keeps its distance like the ranged officers, raises the fallen near it, strikes the player's spot.
            stats.MaxHp = 200f;
            stats.Speed = 2f;
            stats.Radius = 0.36f;
            stats.AttackReach = 0.9f;
            stats.Damage = 8;
            stats.AttackInterval = 1.4f;
            stats.HoldRange = 7f;
            stats.RetreatRange = 3f;
            var raise = new RaiseAbility
            {
                Cooldown = 9f, FirstDelay = 1.5f, MinRange = 0f, MaxRange = 16f, NeedsSight = false, Warning = 1.2f, Recovery = 0.5f,
                Radius = 7f, Count = RaiseAbility.MaxCount, MaxAge = 20f, RiseTime = 2f,
            };
            var area = new AreaStrikeAbility
            {
                Cooldown = 5f, FirstDelay = 1f, MinRange = 2f, MaxRange = 10f, NeedsSight = true, Warning = 1.2f, Recovery = 0.4f,
                Radius = 1.6f,
            };
            if (prefabName.EndsWith("_Castle", StringComparison.Ordinal))
            {
                // A fireball: an explosion where the player stood (it hurts the monsters there too).
                var fireball = new ExplosionEffect { Damage = 30f, EdgeDamage = 0.4f, SoundRadius = 10f };
                fireball.SetRadius(1.6f);
                area.SetImpact(fireball);
            }
            else if (prefabName.EndsWith("_Forest", StringComparison.Ordinal))
            {
                // A poison cloud: a zone of the monsters that eats away at the player in it.
                var cloud = new PlaceZoneEffect { Zone = EnsurePoisonZone(), Duration = 6f, Strength = 1f };
                cloud.SetRadius(1.6f);
                area.SetImpact(cloud);
            }
            else
            {
                // A freeze: the player caught in it cannot move for a moment, and takes a little damage.
                area.Damage = 6f;
                area.Status = EnsureFreeze();
                area.StatusDuration = 1.2f;
            }

            // Order = priority and the cast clip (ShamanClips.Casts): the raise first.
            definition.SetAbilities(raise, area);
            EditorUtility.SetDirty(definition);
        }

        private static DropTable EnsureEliteDrop()
        {
            var drop = AssetDatabase.LoadAssetAtPath<DropTable>(EliteDropPath);
            if (drop != null) return drop;
            // The officers' table with richer odds again (ТЗ: ранг определяет дроп; ключи к сундукам — позже).
            if (!AssetDatabase.CopyAsset(OfficerDropPath, EliteDropPath))
                throw new InvalidOperationException($"Could not copy '{OfficerDropPath}' to '{EliteDropPath}'.");
            drop = AssetDatabase.LoadAssetAtPath<DropTable>(EliteDropPath);
            drop.Configure(ContentIds.FromName("EliteDrop"), nothing: 5f, ammo: 35f, medkit: 22f, throwable: 18f, weapon: 20f);
            EditorUtility.SetDirty(drop);
            return drop;
        }

        /// <summary>The Snow Shaman's freeze (stage 5e): a stun of ice colour (the player's control immunity follows it).</summary>
        private static StatusDefinition EnsureFreeze() =>
            EnsureStatus("Status_Freeze", new Color(0.6f, 0.9f, 1f, 1f), new StunStatus());

        /// <summary>The Forest Shaman's poison cloud (stage 5e): a zone whose state eats away at those in it.</summary>
        private static ZoneDefinition EnsurePoisonZone()
        {
            const string path = EffectsPath + "/Zone_Poison.asset";
            var zone = AssetDatabase.LoadAssetAtPath<ZoneDefinition>(path);
            if (zone != null) return zone;
            var poison = EnsureStatus("Status_Poison", new Color(0.6f, 0.35f, 0.85f, 1f), new DamageOverTimeStatus { DamagePerSecond = 8f });
            zone = ScriptableObject.CreateInstance<ZoneDefinition>();
            zone.Configure(ContentIds.FromName("Zone_Poison"), new ZoneStatus { Status = poison, Duration = 1f });
            AssetDatabase.CreateAsset(zone, path);
            var serialized = new SerializedObject(zone);
            serialized.FindProperty("_core").colorValue = new Color(0.7f, 0.45f, 0.95f, 1f);
            serialized.FindProperty("_edge").colorValue = new Color(0.35f, 0.15f, 0.55f, 1f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return zone;
        }
    }
}
