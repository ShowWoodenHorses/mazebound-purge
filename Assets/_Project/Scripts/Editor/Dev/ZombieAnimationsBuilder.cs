using System;
using System.Linq;
using Maze.Core.Definitions;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Gameplay.Zombies;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static Maze.Editor.Dev.CharacterAnimationUtility;
using Z = Maze.Presentation.Visual.ZombieAnimatorParameters;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Zombie Animations: one Animator Controller for every zombie look.
    /// <list type="number">
    /// <item>Mixamo clips "Zombie_*" → Humanoid (see <see cref="CharacterAnimationUtility.ImportClips"/>); idles,
    /// walk and run loop.</item>
    /// <item>Controller: Idle (two variants by IdleVariant), Walk (patrol, return), Scream (Alert, the roar part of
    /// the clip squeezed into <see cref="ZombieController.AlertDuration"/>), Run (chase), three attacks, death. The measured ground speeds of walk and run are stored as parameters (WalkGroundSpeed, RunGroundSpeed);
    /// the presenter turns them and the speeds of <see cref="ZombieDefinition"/> into playback multipliers
    /// (<see cref="Maze.Presentation.Visual.LocomotionAnimation"/>). Attacks last 1 s at AttackSpeed = 1.</item>
    /// <item>Every prefab in Art/Zombie gets the controller (no root motion) and the scale; the placeholder theme's
    /// zombie set gets one variant per prefab tied to a zombie type (chosen in the set, see
    /// <see cref="UpdateZombieSet"/>), the prefabs are made Addressable, and zombies of saved levels whose visual no
    /// longer fits are reassigned.</item>
    /// </list>
    /// Re-running rebuilds the controller in place (same asset, references stay valid).
    /// </summary>
    internal static class ZombieAnimationsBuilder
    {
        private const string ZombieArtPath = "Assets/_Project/Art/Zombie";
        private const string ControllerPath = ZombieArtPath + "/Zombie.controller";
        private const string ZombieSetPath = "Assets/_Project/Data/Themes/PlaceholderZombieSet.asset";
        private const string ZombieDataPath = "Assets/_Project/Data/Zombies";

        /// <summary>Same as the player: the models are ~1.8 m tall, cells 1 m, walls 1.5 m.</summary>
        private const float ModelScale = 0.75f;
        private const float MovingThreshold = 0.1f;

        private const string Idle1 = "Zombie_idle_1";
        private const string Idle2 = "Zombie_idle_2";
        private const string Walk = "Zombie_walk";
        private const string Run = "Zombie_run";
        private const string Death = "Zombie_death";
        private const string Scream = "Zombie_scream";

        // The roar part of Zombie_scream (2.8 s): rearing back and lunging into the scream pose. The rest (holding
        // the pose, standing up) is cut: the Alert lasts only ZombieController.AlertDuration, then the zombie runs.
        private const float ScreamStart = 0.45f;
        private const float ScreamEnd = 1.3f;
        private static readonly string[] Attacks = { "Zombie_attack_1", "Zombie_attack_2", "Zombie_attack_3" };
        private static readonly string[] Looping = { Idle1, Idle2, Walk, Run };

        [MenuItem("Maze/Dev/Build Zombie Animations")]
        public static void Build()
        {
            try
            {
                ImportClips("Zombie_", name => Array.IndexOf(Looping, name) >= 0,
                    name => name == Scream ? (ScreamStart, ScreamEnd) : ((float, float)?)null);
                var prefabPaths = FindPrefabs();
                if (prefabPaths.Length == 0)
                    throw new InvalidOperationException($"No zombie prefabs in {ZombieArtPath}.");

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[0]);
                var controller = CreateController(model);
                foreach (var path in prefabPaths)
                    SetupPrefab(path, controller, ModelScale);

                var reassigned = UpdateZombieSet(prefabPaths);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Maze] Zombie animations built: {ControllerPath}, {prefabPaths.Length} prefab(s), " +
                          $"{reassigned} zombie visual(s) in levels reassigned.");
            }
            catch (Exception e)
            {
                Debug.LogError("[Maze] Building zombie animations failed: " + e.Message);
                throw;
            }
        }

        private static string[] FindPrefabs()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { ZombieArtPath });
            var paths = new string[guids.Length];
            for (var i = 0; i < guids.Length; i++)
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            Array.Sort(paths, StringComparer.Ordinal);
            return paths;
        }

        private static AnimatorController CreateController(GameObject model)
        {
            var controller = CreateOrClearController(ControllerPath);

            controller.AddParameter(Z.Speed, AnimatorControllerParameterType.Float);
            controller.AddParameter(Z.Chasing, AnimatorControllerParameterType.Bool);
            controller.AddParameter(Z.Alert, AnimatorControllerParameterType.Bool);
            AddFloat(controller, Z.WalkPlayback, 1f);
            AddFloat(controller, Z.RunPlayback, 1f);
            AddFloat(controller, Z.WalkGroundSpeed, MeasureGroundSpeed(model, LoadClip(Walk), ModelScale));
            AddFloat(controller, Z.RunGroundSpeed, MeasureGroundSpeed(model, LoadClip(Run), ModelScale));
            controller.AddParameter(Z.IdleVariant, AnimatorControllerParameterType.Float);
            controller.AddParameter(Z.Attack, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(Z.AttackIndex, AnimatorControllerParameterType.Int);
            AddFloat(controller, Z.AttackSpeed, 1f);
            controller.AddParameter(Z.Dead, AnimatorControllerParameterType.Bool);

            var machine = controller.layers[0].stateMachine;

            var idle = controller.CreateBlendTreeInController("Idle", out var idleTree, 0);
            idleTree.blendType = BlendTreeType.Simple1D;
            idleTree.blendParameter = Z.IdleVariant;
            idleTree.useAutomaticThresholds = false;
            idleTree.AddChild(LoadClip(Idle1), 0f);
            idleTree.AddChild(LoadClip(Idle2), 1f);
            SetPosition(machine, idle, new Vector3(300f, 0f));
            machine.defaultState = idle;

            var walk = AddMove(machine, "Walk", LoadClip(Walk), Z.WalkPlayback, new Vector3(300f, 120f));
            var run = AddMove(machine, "Run", LoadClip(Run), Z.RunPlayback, new Vector3(300f, 240f));

            StartMoving(idle, walk, chasing: false, 0.2f);
            StartMoving(idle, run, chasing: true, 0.2f);
            Transition(walk, idle, 0.25f).AddCondition(AnimatorConditionMode.Less, MovingThreshold, Z.Speed);
            Transition(run, idle, 0.25f).AddCondition(AnimatorConditionMode.Less, MovingThreshold, Z.Speed);
            Transition(walk, run, 0.2f).AddCondition(AnimatorConditionMode.If, 0f, Z.Chasing);
            Transition(run, walk, 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0f, Z.Chasing);

            for (var i = 0; i < Attacks.Length; i++)
            {
                var attack = machine.AddState("Attack" + (i + 1), new Vector3(650f, i * 80f));
                attack.motion = LoadClip(Attacks[i]);
                // At AttackSpeed = 1 the state lasts 1 s; the presenter sets 1 / attack interval.
                attack.speed = attack.motion.averageDuration;
                attack.speedParameter = Z.AttackSpeed;
                attack.speedParameterActive = true;

                var enter = machine.AddAnyStateTransition(attack);
                Configure(enter, 0.1f);
                enter.canTransitionToSelf = true;
                enter.AddCondition(AnimatorConditionMode.If, 0f, Z.Attack);
                enter.AddCondition(AnimatorConditionMode.Equals, i, Z.AttackIndex);
                enter.AddCondition(AnimatorConditionMode.IfNot, 0f, Z.Dead);

                var back = attack.AddTransition(idle);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.15f;
                back.hasFixedDuration = true;
                // The player stepped away: follow at once.
                StartMoving(attack, run, chasing: true, 0.15f);
                StartMoving(attack, walk, chasing: false, 0.15f);
            }

            // Roar before a chase: whatever the zombie did; ends into the run (or an attack trigger cuts it).
            var scream = machine.AddState("Scream", new Vector3(0f, 240f));
            scream.motion = LoadClip(Scream);
            scream.speed = scream.motion.averageDuration / ZombieController.AlertDuration;
            var toScream = machine.AddAnyStateTransition(scream);
            Configure(toScream, 0.1f);
            toScream.canTransitionToSelf = false;
            toScream.AddCondition(AnimatorConditionMode.If, 0f, Z.Alert);
            toScream.AddCondition(AnimatorConditionMode.IfNot, 0f, Z.Dead);
            StartMoving(scream, run, chasing: true, 0.15f);
            Transition(scream, idle, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, Z.Alert);

            var death = machine.AddState("Death", new Vector3(650f, 300f));
            death.motion = LoadClip(Death);
            var toDeath = machine.AddAnyStateTransition(death);
            Configure(toDeath, 0.1f);
            toDeath.canTransitionToSelf = false;
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, Z.Dead);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>A locomotion state played at the multiplier the presenter derives from the real speed.</summary>
        private static AnimatorState AddMove(AnimatorStateMachine machine, string name, AnimationClip clip, string playbackParameter,
            Vector3 position)
        {
            var state = machine.AddState(name, position);
            state.motion = clip;
            state.speedParameter = playbackParameter;
            state.speedParameterActive = true;
            return state;
        }

        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            var transition = from.AddTransition(to);
            Configure(transition, duration);
            return transition;
        }

        /// <summary><paramref name="from"/> → walk/run when the zombie moves (and is or is not chasing).</summary>
        private static void StartMoving(AnimatorState from, AnimatorState to, bool chasing, float duration)
        {
            var transition = Transition(from, to, duration);
            transition.AddCondition(AnimatorConditionMode.Greater, MovingThreshold, Z.Speed);
            transition.AddCondition(chasing ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, Z.Chasing);
        }

        /// <summary>
        /// One variant per prefab in Art/Zombie. A prefab already in the set keeps its variant (id, weight and the zombie
        /// type set there by hand); a new one shows a type that has no look yet (else the first type) — reported, so
        /// the type can be checked in the set. Returns how many saved zombie visuals were reassigned.
        /// </summary>
        private static int UpdateZombieSet(string[] prefabPaths)
        {
            var set = AssetDatabase.LoadAssetAtPath<VisualSet>(ZombieSetPath);
            if (set == null)
                throw new InvalidOperationException($"'{ZombieSetPath}' not found: run Maze → Dev → Create Placeholder Theme first.");

            var types = AssetDatabase.FindAssets("t:ZombieDefinition", new[] { ZombieDataPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<ZombieDefinition>)
                .Where(type => type != null)
                .ToList();
            if (types.Count == 0)
                throw new InvalidOperationException($"No zombie types in {ZombieDataPath}: run Maze → Dev → Create Placeholder Zombies.");

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var visualsGroup = settings.FindGroup(LevelDesigner.LevelSync.VisualsGroup) ?? settings.DefaultGroup;
            var old = set.MutableVariants.Where(variant => variant?.Prefab != null).ToList();
            var guids = prefabPaths.Select(AssetDatabase.AssetPathToGUID).ToList();
            var staying = old.Where(variant => guids.Contains(variant.Prefab.AssetGUID)).ToList();
            var variants = set.MutableVariants;
            variants.Clear();
            foreach (var path in prefabPaths)
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (settings.FindAssetEntry(guid) == null)
                    settings.CreateOrMoveEntry(guid, visualsGroup);

                var kept = staying.FirstOrDefault(variant => variant.Prefab.AssetGUID == guid);
                if (kept != null)
                {
                    if (kept.Definition == null)
                        Debug.LogWarning($"[Maze] Zombie look '{kept.Id}' ({path}) has no zombie type: set Definition in {ZombieSetPath}.");
                    variants.Add(kept);
                    continue;
                }

                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                var type = types.FirstOrDefault(t => staying.All(v => v.Definition != t) && variants.All(v => v.Definition != t)) ?? types[0];
                var id = WeaponsBuilder.IdOf(name);
                if (!id.StartsWith("zombie", StringComparison.Ordinal)) id = "zombie_" + id;
                var unique = id;
                for (var n = 2; variants.Concat(staying).Any(v => v.Id == unique); n++) unique = $"{id}_{n}";
                variants.Add(new VisualVariant(unique, definition: type, prefab: new AssetReferenceGameObject(guid)));
                Debug.LogWarning($"[Maze] New zombie look '{unique}' ({path}) shows the type '{type.name}': check it " +
                                 $"(Definition of the variant) in {ZombieSetPath}, then run Build Zombie Animations again.");
            }

            foreach (var type in types)
                if (variants.All(variant => variant.Definition != type))
                    Debug.LogWarning($"[Maze] Zombie type '{type.name}' has no look: such zombies are invisible in game.");

            set.DefaultVariantId = null;
            EditorUtility.SetDirty(set);
            EditorUtility.SetDirty(settings);

            var reassigned = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:LevelData"))
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid));
                if (level == null || level.VisualTheme == null || level.VisualTheme.GetSet(VisualKind.Zombie) != set)
                    continue;

                var changed = false;
                foreach (var zombie in level.ZombieSpawns)
                {
                    var variant = set.FindVariant(level.VisualData.GetObjectAssignment(zombie.Id).VariantId);
                    if (variant != null && variant.Matches(VisualCategory.General, zombie.Definition))
                        continue;

                    VisualAssigner.AssignObject(level, zombie);
                    changed = true;
                    reassigned++;
                }

                if (changed) EditorUtility.SetDirty(level);
            }

            return reassigned;
        }
    }
}
