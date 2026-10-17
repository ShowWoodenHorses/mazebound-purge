using System;
using System.Collections.Generic;
using System.Linq;
using Purge.Core.Definitions;
using Purge.Core.Visual;
using Purge.Presentation.Visual;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static Purge.Editor.Dev.CharacterAnimationUtility;
using P = Purge.Presentation.Visual.PlayerAnimatorParameters;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Player Animations: turns the player model and the clips "Player_*" into the game's player
    /// prefab. Clips (stage 4а) — Ultimate Animation Collection: one set per way of holding a weapon
    /// (<see cref="WeaponHold"/>, <see cref="Sets"/>) — aiming idle, run in 8 directions relative to the body, shot,
    /// reload; melee — combat idle, 8-way run, three attacks; plus hit and death. Only <c>Player_use</c> stays from Maze.
    /// <list type="number">
    /// <item>Clips the project lacks are copied once from the pack in Import (<see cref="UacPath"/>) to
    /// <c>Art/Animations/Player_&lt;set&gt;_&lt;action&gt;.fbx</c>; after that the pack is not needed.</item>
    /// <item>Clips are imported as Humanoid (own avatar per file, retargeted onto the model), root motion baked into
    /// the pose (movement is done by gameplay); idle/run loop.</item>
    /// <item>Animator Controller: base layer — per set a 2D blend tree (idle in the centre, runs at the 8 directions)
    /// by <see cref="P.MoveX"/>/<see cref="P.MoveY"/> (movement relative to the body × speed): the legs run where the
    /// player goes while the body faces the aim; death. Layer UpperBody (arms, spine, head) — the set's shot and reload,
    /// melee attacks, hit, use. States where the left hand leaves the gun are tagged <see cref="P.NoHandIK"/>, death
    /// <see cref="P.NoWeaponPose"/> (<see cref="CharacterWeaponRig.PoseGun"/>). The moment each attack clip hits is
    /// measured and stored as the defaults of <see cref="P.AttackContact"/>0..2, the side the weapon moves to then as
    /// <see cref="P.AttackSweep"/>0..2.</item>
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
        private const string UacPath = "Assets/_Project/Import/Animations/Ultimate Animation Collection";

        /// <summary>The model is ~1.8 m tall; cells are 1 m and walls 1.5 m.</summary>
        private const float ModelScale = 0.75f;

        private const float UseDuration = 0.8f;
        private const float HitDuration = 0.6f;

        private const string Use = "Player_use";
        private const string Hit = "Player_Hit";
        private const string Death = "Player_Die";

        /// <summary>The 8 run directions: UAC's name part and the direction relative to the body (x right, y forward).</summary>
        private static readonly (string Uac, string Short, Vector2 Direction)[] Directions =
        {
            ("Forward", "F", new Vector2(0f, 1f)),
            ("ForwardRight", "FR", new Vector2(0.7071f, 0.7071f)),
            ("Right", "R", new Vector2(1f, 0f)),
            ("BackwardsRight", "BR", new Vector2(0.7071f, -0.7071f)),
            ("Backwards", "B", new Vector2(0f, -1f)),
            ("BackwardsLeft", "BL", new Vector2(-0.7071f, -0.7071f)),
            ("Left", "L", new Vector2(-1f, 0f)),
            ("ForwardLeft", "FL", new Vector2(-0.7071f, 0.7071f)),
        };

        /// <summary>A set of clips: how a weapon is held (null = no weapon) and the UAC files it is made of.</summary>
        private sealed class ClipSet
        {
            public string Name;
            public int Value; // P.Weapon
            public string Idle;
            public string Run; // {0} = direction
            public string Shoot;
            public string Reload;
            public string[] Attacks;
        }

        /// <summary>
        /// Animation sets. A new way of holding a weapon (<see cref="WeaponHold"/>) needs a line here and in
        /// <see cref="P.SetOf"/>.
        /// </summary>
        private static readonly ClipSet[] Sets =
        {
            new ClipSet { Name = "Unarmed", Value = P.WeaponNone, Idle = "IdleUnarmed", Run = "Run{0}Unarmed" },
            new ClipSet
            {
                Name = "Pistol", Value = P.SetOf(WeaponHold.Pistol), Idle = "IdleAim2HandedGun", Run = "Run{0}Aiming2HandedGun",
                Shoot = "ShootPrimary2HandedGun", Reload = "Reload2HandedGun",
            },
            new ClipSet
            {
                Name = "Rifle", Value = P.SetOf(WeaponHold.Rifle), Idle = "IdleAimAssaultRifle", Run = "Run{0}AimingAssaultRifle",
                Shoot = "ShootPrimaryAssaultRifle", Reload = "ReloadAssaultRifle",
            },
            new ClipSet
            {
                Name = "Shotgun", Value = P.SetOf(WeaponHold.Shotgun), Idle = "IdleAimShotgun", Run = "Run{0}AimingShotgun",
                Shoot = "ShootShotgun", Reload = "ReloadShotgun",
            },
            new ClipSet
            {
                Name = "Heavy", Value = P.SetOf(WeaponHold.Heavy), Idle = "IdleAimMinigun", Run = "Run{0}AimingMinigun",
                Shoot = "ShootMinigun", Reload = "ReloadMinigun",
            },
            new ClipSet
            {
                Name = "Shoulder", Value = P.SetOf(WeaponHold.Shoulder), Idle = "IdleAimBazooka", Run = "Run{0}AimingBazooka",
                Shoot = "ShootBazooka", Reload = "ReloadBazooka",
            },
            new ClipSet
            {
                Name = "Melee", Value = P.SetOf(WeaponHold.Melee), Idle = "IdleCombat1hMelee", Run = "Run{0}1hMelee",
                Attacks = new[] { "AttackA1hMelee", "AttackB1hMelee", "AttackC1hMelee" },
            },
        };

        /// <summary>Single clips outside the sets: project name ← UAC file.</summary>
        private static readonly (string Name, string Uac)[] Singles =
        {
            (Hit, "GetHitFrontLightAssaultRifle"),
            (Death, "DeathBackAssaultRifle"),
        };

        private const string PlayerDefinitionPath = "Assets/_Project/Data/Player/PlayerDefinition.asset";

        private static string IdleClip(ClipSet set) => $"Player_{set.Name}_Idle";
        private static string RunClip(ClipSet set, int direction) => $"Player_{set.Name}_Run{Directions[direction].Short}";
        private static string ShootClip(ClipSet set) => $"Player_{set.Name}_Shoot";
        private static string ReloadClip(ClipSet set) => $"Player_{set.Name}_Reload";
        private static string AttackClip(ClipSet set, int index) => $"Player_{set.Name}_Attack{index + 1}";

        /// <summary>Clip whose pose shows how a gun of <paramref name="hold"/> is held (Build Weapons: the left hand grip).</summary>
        public static string GripPoseClip(WeaponHold hold)
        {
            var value = P.SetOf(hold);
            var set = Sets.FirstOrDefault(s => s.Value == value) ?? Sets.First(s => s.Name == "Rifle");
            return IdleClip(set);
        }

        [MenuItem("Purge/Dev/Build Player Animations")]
        public static void Build()
        {
            try
            {
                var copied = CopyClips();
                var looping = new HashSet<string>();
                foreach (var set in Sets)
                {
                    looping.Add(IdleClip(set));
                    for (var d = 0; d < Directions.Length; d++) looping.Add(RunClip(set, d));
                }

                CharacterAnimationUtility.ImportClips("Player_", looping.Contains);
                var mask = CreateUpperBodyMask();
                var prefabPath = PlayerPrefabPath();
                var controller = CreateController(mask, prefabPath);
                var prefab = SetupPrefab(prefabPath, controller);
                AssignToPlayerVisual(prefab);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Purge] Player animations built: {ControllerPath}, prefab {prefabPath}" +
                          (copied > 0 ? $", {copied} clip(s) copied from the pack." : "."));
            }
            catch (Exception e)
            {
                Debug.LogError("[Purge] Building player animations failed: " + e.Message);
                throw;
            }
        }

        /// <summary>Copies every clip of the sets the project lacks from the UAC pack; returns how many.</summary>
        private static int CopyClips()
        {
            var wanted = new List<(string Name, string Uac)>(Singles);
            foreach (var set in Sets)
            {
                wanted.Add((IdleClip(set), set.Idle));
                for (var d = 0; d < Directions.Length; d++)
                    wanted.Add((RunClip(set, d), string.Format(set.Run, Directions[d].Uac)));
                if (set.Shoot != null) wanted.Add((ShootClip(set), set.Shoot));
                if (set.Reload != null) wanted.Add((ReloadClip(set), set.Reload));
                if (set.Attacks != null)
                    for (var i = 0; i < set.Attacks.Length; i++)
                        wanted.Add((AttackClip(set, i), set.Attacks[i]));
            }

            var copied = 0;
            Dictionary<string, string> pack = null;
            foreach (var (name, uac) in wanted)
            {
                var target = $"{AnimationsPath}/{name}.fbx";
                if (AssetDatabase.LoadMainAssetAtPath(target) != null) continue;
                pack ??= PackClips();
                if (!pack.TryGetValue("Humanoid@" + uac, out var source))
                    throw new InvalidOperationException($"Clip '{name}' is missing and '{uac}' is not in {UacPath}: put the " +
                                                        "Ultimate Animation Collection pack into Import.");
                if (!AssetDatabase.CopyAsset(source, target))
                    throw new InvalidOperationException($"Could not copy '{source}' to '{target}'.");
                copied++;
            }

            return copied;
        }

        /// <summary>The pack's in-place clips by file name (the root-motion "_RM" ones are left out).</summary>
        private static Dictionary<string, string> PackClips()
        {
            var clips = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!AssetDatabase.IsValidFolder(UacPath)) return clips;
            foreach (var guid in AssetDatabase.FindAssets("Humanoid@ t:Model", new[] { UacPath }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!name.EndsWith("_RM", StringComparison.Ordinal) && !clips.ContainsKey(name))
                    clips[name] = path;
            }

            return clips;
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
            controller.AddParameter(P.MoveX, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.MoveY, AnimatorControllerParameterType.Float);
            controller.AddParameter(P.Weapon, AnimatorControllerParameterType.Int);
            controller.AddParameter(P.Attack, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(P.AttackIndex, AnimatorControllerParameterType.Int);
            AddFloat(controller, P.AttackSpeed, 1f);
            // Data for the views, not driven: when each attack clip hits.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var melee = Sets.First(set => set.Attacks != null);
            for (var i = 0; i < melee.Attacks.Length; i++)
            {
                var (moment, sweep) = MeasureSwing(model, LoadClip(AttackClip(melee, i)));
                AddFloat(controller, P.AttackContact + i, moment);
                AddFloat(controller, P.AttackSweep + i, sweep);
            }

            controller.AddParameter(P.Shoot, AnimatorControllerParameterType.Trigger);
            AddFloat(controller, P.ShootSpeed, 1f);
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
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            for (var s = 0; s < Sets.Length; s++)
            {
                var set = Sets[s];
                var state = controller.CreateBlendTreeInController(set.Name, out var tree, 0);
                tree.blendType = BlendTreeType.FreeformDirectional2D;
                tree.blendParameter = P.MoveX;
                tree.blendParameterY = P.MoveY;
                tree.AddChild(LoadClip(IdleClip(set)), Vector2.zero);
                for (var d = 0; d < Directions.Length; d++)
                    tree.AddChild(LoadClip(RunClip(set, d)), Directions[d].Direction);

                // Each run keeps up with MoveSpeed as far as LocomotionAnimation allows (feet slide less).
                var children = tree.children;
                for (var c = 1; c < children.Length; c++)
                    children[c].timeScale = LocomotionAnimation.Playback(moveSpeed,
                        MeasureGroundSpeed(model, (AnimationClip)children[c].motion, ModelScale));
                tree.children = children;
                SetPosition(machine, state, new Vector3(300f, s * 70f));
                if (set.Value == P.WeaponNone) machine.defaultState = state;

                var enter = machine.AddAnyStateTransition(state);
                Configure(enter, 0.2f);
                enter.canTransitionToSelf = false;
                enter.AddCondition(AnimatorConditionMode.Equals, set.Value, P.Weapon);
                enter.AddCondition(AnimatorConditionMode.IfNot, 0f, P.Dead);
            }

            var death = machine.AddState("Death", new Vector3(650f, 100f));
            death.motion = LoadClip(Death);
            death.tag = P.NoWeaponPose;
            var toDeath = machine.AddAnyStateTransition(death);
            Configure(toDeath, 0.15f);
            toDeath.canTransitionToSelf = false;
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, P.Dead);
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

            var row = 0;
            foreach (var set in Sets)
            {
                if (set.Attacks != null)
                    for (var i = 0; i < set.Attacks.Length; i++)
                    {
                        var attack = AddAction(machine, empty, $"{set.Name}Attack{i + 1}", LoadClip(AttackClip(set, i)),
                            new Vector3(650f, row++ * 70f));
                        // At AttackSpeed = 1 the state lasts 1 s; the presenter sets 1 / cooldown.
                        attack.speed = attack.motion.averageDuration;
                        attack.speedParameter = P.AttackSpeed;
                        attack.speedParameterActive = true;
                        var enter = AddEnter(machine, attack, P.Attack, 0.08f);
                        enter.AddCondition(AnimatorConditionMode.Equals, i, P.AttackIndex);
                        enter.AddCondition(AnimatorConditionMode.Equals, set.Value, P.Weapon);
                    }

                if (set.Shoot != null)
                {
                    var shoot = AddAction(machine, empty, set.Name + "Shoot", LoadClip(ShootClip(set)), new Vector3(650f, row++ * 70f));
                    // At ShootSpeed = 1 the state lasts 1 s; the presenter shortens it to the weapon's fire interval.
                    shoot.speed = shoot.motion.averageDuration;
                    shoot.speedParameter = P.ShootSpeed;
                    shoot.speedParameterActive = true;
                    AddEnter(machine, shoot, P.Shoot, 0.04f).AddCondition(AnimatorConditionMode.Equals, set.Value, P.Weapon);
                }

                if (set.Reload != null)
                {
                    var reload = machine.AddState(set.Name + "Reload", new Vector3(300f, 150f + row++ * 70f));
                    reload.motion = LoadClip(ReloadClip(set));
                    reload.tag = P.NoHandIK; // the left hand goes to the magazine
                    // At ReloadSpeed = 1 the state lasts 1 s; the presenter sets 1 / reload time.
                    reload.speed = reload.motion.averageDuration;
                    reload.speedParameter = P.ReloadSpeed;
                    reload.speedParameterActive = true;
                    var startReload = machine.AddAnyStateTransition(reload);
                    Configure(startReload, 0.15f);
                    startReload.canTransitionToSelf = false;
                    startReload.AddCondition(AnimatorConditionMode.If, 0f, P.Reloading);
                    startReload.AddCondition(AnimatorConditionMode.Equals, set.Value, P.Weapon);
                    var endReload = reload.AddTransition(empty);
                    Configure(endReload, 0.15f);
                    endReload.AddCondition(AnimatorConditionMode.IfNot, 0f, P.Reloading);
                    var switched = reload.AddTransition(empty);
                    Configure(switched, 0.15f);
                    switched.AddCondition(AnimatorConditionMode.NotEqual, set.Value, P.Weapon);
                }
            }

            var hit = AddAction(machine, empty, "Hit", LoadClip(Hit), new Vector3(0f, 100f));
            hit.speed = hit.motion.averageDuration / HitDuration;
            hit.tag = P.NoHandIK;
            AddEnter(machine, hit, P.Hit, 0.05f);

            var use = AddAction(machine, empty, "Use", LoadClip(Use), new Vector3(0f, 200f));
            use.speed = use.motion.averageDuration / UseDuration;
            use.tag = P.NoHandIK;
            AddEnter(machine, use, P.Use, 0.1f);
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
                throw new InvalidOperationException($"'{PlayerVisualPath}' not found: run Purge → Dev → Create Placeholder Player first.");

            visual.Prefab = new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)));
            EditorUtility.SetDirty(visual);

            var problem = LevelDesigner.LevelSync.SyncShared(UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.GetSettings(true));
            if (problem != null) Debug.LogWarning("[Purge] " + problem);
        }
    }
}
