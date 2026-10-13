using System;
using System.Linq;
using Maze.Core.Definitions;
using Maze.Core.Visual;
using Maze.Presentation.Visual;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static Maze.Editor.Dev.CharacterAnimationUtility;
using P = Maze.Presentation.Visual.PlayerAnimatorParameters;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Player Animations: turns the player model and the Mixamo clips "Player_*" into the game's
    /// player prefab.
    /// <list type="number">
    /// <item>Clips are imported as Humanoid (own avatar per file, retargeted onto the model), one clip per file named
    /// after the file, root motion baked into the pose (movement is done by gameplay); idle/run loop.</item>
    /// <item>Animator Controller: base layer — locomotion per weapon in hands (blend idle → run by Speed) and death;
    /// layer UpperBody (arms, spine, head) — attacks, shots, reload, hit, use, so the legs keep running. States where
    /// the left hand leaves the gun are tagged <see cref="P.NoHandIK"/>, death <see cref="P.NoWeaponPose"/>
    /// (<see cref="CharacterWeaponRig.PoseGun"/>). The moment each attack clip hits is measured and stored as the
    /// defaults of <see cref="P.AttackContact"/>0..2 (views time the swing effect and hit reactions by it), the side
    /// the weapon moves to then as <see cref="P.AttackSweep"/>0..2 (the swing effect follows it).</item>
    /// <item>The model prefab gets the controller (no root motion) and becomes <see cref="PlayerVisualDefinition"/>'s
    /// prefab (made Addressable by Build / Sync's shared step).</item>
    /// </list>
    /// Re-running rebuilds the controller in place (same asset, references stay valid).
    /// </summary>
    internal static class PlayerAnimationsBuilder
    {
        private const string PlayerArtPath = "Assets/_Project/Art/Player";
        private const string ControllerPath = PlayerArtPath + "/Player.controller";
        private const string MaskPath = PlayerArtPath + "/PlayerUpperBody.mask";
        private const string PlayerVisualPath = "Assets/_Project/Data/Player/PlayerVisual.asset";

        /// <summary>The model is ~1.8 m tall; cells are 1 m and walls 1.5 m.</summary>
        private const float ModelScale = 0.75f;

        private const float UseDuration = 0.8f;
        private const float HitDuration = 0.6f;

        private const string UnarmedIdle = "Player_without_weapon_idle";
        private const string UnarmedRun = "Player_without_weapon_run";
        private const string MeleeIdle = "Player_sword_weapon_idle";
        private const string MeleeRun = "Player_sword_weapon_run";
        private const string RangedIdle = "Player_rifle_weapon_idle";
        private const string RangedRun = "Player_rifle_weapon_run";
        private const string Reload = "Player_rifle_weapon_reload";
        private const string Shoot = "Player_rifle_weapon_shoot";
        private const string ShootAuto = "Player_rifle_weapon_shoot_auto";
        private const string Hit = "Player_take_hit";
        private const string Use = "Player_use";
        private const string Death = "Player_death";
        private static readonly string[] Attacks = { "Player_sword_attack_1", "Player_sword_attack_2", "Player_sword_attack_3" };

        private static readonly string[] Looping = { UnarmedIdle, UnarmedRun, MeleeIdle, MeleeRun, RangedIdle, RangedRun };

        private const string PlayerDefinitionPath = "Assets/_Project/Data/Player/PlayerDefinition.asset";

        [MenuItem("Maze/Dev/Build Player Animations")]
        public static void Build()
        {
            try
            {
                CharacterAnimationUtility.ImportClips("Player_", name => Array.IndexOf(Looping, name) >= 0);
                var mask = CreateUpperBodyMask();
                var prefabPath = PlayerPrefabPath();
                var controller = CreateController(mask, prefabPath);
                var prefab = SetupPrefab(prefabPath, controller);
                AssignToPlayerVisual(prefab);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Maze] Player animations built: {ControllerPath}, prefab {prefabPath}.");
            }
            catch (Exception e)
            {
                Debug.LogError("[Maze] Building player animations failed: " + e.Message);
                throw;
            }
        }

        private static AvatarMask CreateUpperBodyMask()
        {
            var mask = CreateOrLoadMask(MaskPath);
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            {
                var upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                            part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                            part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                            part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                mask.SetHumanoidBodyPartActive(part, upper);
            }

            EditorUtility.SetDirty(mask);
            return mask;
        }

        /// <summary>
        /// The player model prefab: the prefab right in Art/Player (not in subfolders). When there are several, the
        /// one <see cref="PlayerVisualDefinition"/> already uses; to switch models, leave only the new one there.
        /// </summary>
        public static string PlayerPrefabPath()
        {
            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { PlayerArtPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') == PlayerArtPath)
                .ToList();
            if (prefabs.Count == 1) return prefabs[0];
            if (prefabs.Count == 0)
                throw new InvalidOperationException($"No player model prefab in {PlayerArtPath}.");

            var current = AssetDatabase.LoadAssetAtPath<PlayerVisualDefinition>(PlayerVisualPath)?.Prefab?.AssetGUID;
            var used = prefabs.FirstOrDefault(path => AssetDatabase.AssetPathToGUID(path) == current);
            return used ?? throw new InvalidOperationException(
                $"Several prefabs in {PlayerArtPath} ({string.Join(", ", prefabs)}): leave only the player model there.");
        }

        private static AnimatorController CreateController(AvatarMask upperBodyMask, string prefabPath)
        {
            var controller = CreateOrClearController(ControllerPath);

            controller.AddParameter(P.Speed, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.Weapon, AnimatorControllerParameterType.Int);
            controller.AddParameter(P.Attack, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(P.AttackIndex, AnimatorControllerParameterType.Int);
            AddFloat(controller, P.AttackSpeed, 1f);
            // Data for the views, not driven: when each attack clip hits.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            for (var i = 0; i < Attacks.Length; i++)
            {
                var (moment, sweep) = MeasureSwing(model, LoadClip(Attacks[i]));
                AddFloat(controller, P.AttackContact + i, moment);
                AddFloat(controller, P.AttackSweep + i, sweep);
            }
            controller.AddParameter(P.Shoot, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(P.Automatic, AnimatorControllerParameterType.Bool);
            controller.AddParameter(P.Reloading, AnimatorControllerParameterType.Bool);
            AddFloat(controller, P.ReloadSpeed, 1f);
            controller.AddParameter(P.Hit, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(P.Use, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(P.Dead, AnimatorControllerParameterType.Bool);

            BuildBaseLayer(controller, prefabPath);
            BuildUpperBodyLayer(controller, upperBodyMask);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void BuildBaseLayer(AnimatorController controller, string prefabPath)
        {
            var machine = controller.layers[0].stateMachine;
            var definition = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(PlayerDefinitionPath);
            var moveSpeed = definition != null ? definition.MoveSpeed : 3.5f;
            // Runs keep up with MoveSpeed as far as LocomotionAnimation allows (feet slide less).
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            float RunTimeScale(string run) => LocomotionAnimation.Playback(moveSpeed, MeasureGroundSpeed(model, LoadClip(run), ModelScale));
            var unarmed = AddLocomotion(controller, "Unarmed", UnarmedIdle, UnarmedRun, RunTimeScale(UnarmedRun), new Vector3(300f, 0f));
            var melee = AddLocomotion(controller, "Melee", MeleeIdle, MeleeRun, RunTimeScale(MeleeRun), new Vector3(300f, 100f));
            var ranged = AddLocomotion(controller, "Ranged", RangedIdle, RangedRun, RunTimeScale(RangedRun), new Vector3(300f, 200f));
            machine.defaultState = unarmed;

            var locomotion = new[] { unarmed, melee, ranged };
            for (var from = 0; from < locomotion.Length; from++)
            for (var to = 0; to < locomotion.Length; to++)
            {
                if (from == to) continue;
                var transition = locomotion[from].AddTransition(locomotion[to]);
                Configure(transition, 0.2f);
                transition.AddCondition(AnimatorConditionMode.Equals, to, P.Weapon);
            }

            var death = machine.AddState("Death", new Vector3(600f, 100f));
            death.motion = LoadClip(Death);
            death.tag = P.NoWeaponPose;
            var toDeath = machine.AddAnyStateTransition(death);
            Configure(toDeath, 0.15f);
            toDeath.canTransitionToSelf = false;
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, P.Dead);
        }

        private static AnimatorState AddLocomotion(AnimatorController controller, string name, string idle, string run, float runTimeScale, Vector3 position)
        {
            var state = controller.CreateBlendTreeInController(name, out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = P.Speed;
            tree.useAutomaticThresholds = false;
            tree.AddChild(LoadClip(idle), 0f);
            tree.AddChild(LoadClip(run), 1f);
            var children = tree.children;
            children[1].timeScale = runTimeScale;
            tree.children = children;
            SetPosition(controller.layers[0].stateMachine, state, position);
            return state;
        }

        private static void BuildUpperBodyLayer(AnimatorController controller, AvatarMask mask)
        {
            var machine = new AnimatorStateMachine
            {
                name = P.UpperBodyLayer,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(machine, controller);
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = P.UpperBodyLayer,
                stateMachine = machine,
                avatarMask = mask,
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
            });

            var empty = machine.AddState("Empty", new Vector3(300f, 0f));
            machine.defaultState = empty;

            for (var i = 0; i < Attacks.Length; i++)
            {
                var attack = AddAction(machine, empty, "Attack" + (i + 1), LoadClip(Attacks[i]), new Vector3(600f, i * 70f));
                // At AttackSpeed = 1 the state lasts 1 s; the presenter sets 1 / cooldown.
                attack.speed = attack.motion.averageDuration;
                attack.speedParameter = P.AttackSpeed;
                attack.speedParameterActive = true;
                var enter = AddEnter(machine, attack, P.Attack, 0.08f);
                enter.AddCondition(AnimatorConditionMode.Equals, i, P.AttackIndex);
            }

            var shoot = AddAction(machine, empty, "Shoot", LoadClip(Shoot), new Vector3(600f, 230f));
            AddEnter(machine, shoot, P.Shoot, 0.05f).AddCondition(AnimatorConditionMode.IfNot, 0f, P.Automatic);
            var shootAuto = AddAction(machine, empty, "ShootAuto", LoadClip(ShootAuto), new Vector3(600f, 300f));
            AddEnter(machine, shootAuto, P.Shoot, 0.03f).AddCondition(AnimatorConditionMode.If, 0f, P.Automatic);

            var hit = AddAction(machine, empty, "Hit", LoadClip(Hit), new Vector3(0f, 100f));
            hit.speed = hit.motion.averageDuration / HitDuration;
            hit.tag = P.NoHandIK;
            AddEnter(machine, hit, P.Hit, 0.05f);

            var use = AddAction(machine, empty, "Use", LoadClip(Use), new Vector3(0f, 200f));
            use.speed = use.motion.averageDuration / UseDuration;
            use.tag = P.NoHandIK;
            AddEnter(machine, use, P.Use, 0.1f);

            var reload = machine.AddState("Reload", new Vector3(300f, 300f));
            reload.motion = LoadClip(Reload);
            reload.tag = P.NoHandIK; // the left hand goes to the magazine
            // At ReloadSpeed = 1 the state lasts 1 s; the presenter sets 1 / reload time.
            reload.speed = reload.motion.averageDuration;
            reload.speedParameter = P.ReloadSpeed;
            reload.speedParameterActive = true;
            var startReload = machine.AddAnyStateTransition(reload);
            Configure(startReload, 0.15f);
            startReload.canTransitionToSelf = false;
            startReload.AddCondition(AnimatorConditionMode.If, 0f, P.Reloading);
            var endReload = reload.AddTransition(empty);
            Configure(endReload, 0.15f);
            endReload.AddCondition(AnimatorConditionMode.IfNot, 0f, P.Reloading);
        }

        /// <summary>A one-shot state that returns to <paramref name="empty"/> when it ends.</summary>
        private static AnimatorState AddAction(AnimatorStateMachine machine, AnimatorState empty, string name, Motion motion, Vector3 position)
        {
            var state = machine.AddState(name, position);
            state.motion = motion;
            var back = state.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = 0.9f;
            back.duration = 0.15f;
            back.hasFixedDuration = true;
            return state;
        }

        /// <summary>Any State → <paramref name="state"/> on a trigger; a new trigger restarts the state.</summary>
        private static AnimatorStateTransition AddEnter(AnimatorStateMachine machine, AnimatorState state, string trigger, float duration)
        {
            var transition = machine.AddAnyStateTransition(state);
            Configure(transition, duration);
            transition.canTransitionToSelf = true;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            return transition;
        }

        private static GameObject SetupPrefab(string prefabPath, AnimatorController controller)
        {
            CharacterAnimationUtility.SetupPrefab(prefabPath, controller, ModelScale);
            return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        private static void AssignToPlayerVisual(GameObject prefab)
        {
            var visual = AssetDatabase.LoadAssetAtPath<PlayerVisualDefinition>(PlayerVisualPath);
            if (visual == null)
                throw new InvalidOperationException($"'{PlayerVisualPath}' not found: run Maze → Dev → Create Placeholder Player first.");

            visual.Prefab = new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)));
            EditorUtility.SetDirty(visual);

            var problem = LevelDesigner.LevelSync.SyncShared(UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.GetSettings(true));
            if (problem != null) Debug.LogWarning("[Maze] " + problem);
        }
    }
}
