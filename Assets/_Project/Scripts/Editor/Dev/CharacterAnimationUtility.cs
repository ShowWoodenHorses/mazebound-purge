using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Shared steps of the character animation builders (<see cref="PlayerAnimationsBuilder"/>,
    /// <see cref="ZombieAnimationsBuilder"/>): Mixamo clips → Humanoid, controller helpers, prefab setup.
    /// </summary>
    internal static class CharacterAnimationUtility
    {
        public const string AnimationsPath = "Assets/_Project/Art/Animations";

        /// <summary>
        /// Imports every "{prefix}*.fbx" in <see cref="AnimationsPath"/> as Humanoid (own avatar per file, retargeted
        /// onto any Humanoid model), one clip per file named after the file, root motion baked into the pose
        /// (gameplay moves and turns characters; root motion is never applied). <paramref name="trim"/> may keep only
        /// a part of a clip (seconds of the source take).
        /// </summary>
        public static void ImportClips(string prefix, Func<string, bool> isLooping, Func<string, (float Start, float End)?> trim = null)
        {
            foreach (var guid in AssetDatabase.FindAssets(prefix + " t:Model", new[] { AnimationsPath }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                if (importer.animationType != ModelImporterAnimationType.Human ||
                    importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.SaveAndReimport();
                }

                var defaults = importer.defaultClipAnimations;
                if (defaults.Length == 0)
                    throw new InvalidOperationException($"'{path}' has no animation take.");

                var clip = defaults[0];
                var loop = isLooping(name);
                clip.name = name;
                clip.loopTime = loop;
                clip.loopPose = loop;
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
                if (trim?.Invoke(name) is { } range)
                {
                    // Seconds of the source take → its frames.
                    var frameRate = importer.importedTakeInfos.Length > 0 ? importer.importedTakeInfos[0].sampleRate : 30f;
                    clip.firstFrame = Mathf.Max(clip.firstFrame, range.Start * frameRate);
                    clip.lastFrame = Mathf.Min(clip.lastFrame, range.End * frameRate);
                }

                importer.clipAnimations = new[] { clip };
                importer.SaveAndReimport();
            }
        }

        public static AnimationClip LoadClip(string name)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath($"{AnimationsPath}/{name}.fbx"))
                if (asset is AnimationClip clip && clip.name == name)
                    return clip;
            throw new InvalidOperationException($"Clip '{name}' not found in {AnimationsPath}/{name}.fbx.");
        }

        /// <summary>
        /// An empty controller at <paramref name="path"/> (one empty base layer, no parameters). An existing asset is
        /// cleared in place, keeping its GUID: deleting and recreating it would break the references prefabs hold.
        /// </summary>
        public static AnimatorController CreateOrClearController(string path)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return AnimatorController.CreateAnimatorControllerAtPath(path);

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset != controller && asset != null)
                    UnityEngine.Object.DestroyImmediate(asset, true);
            controller.parameters = Array.Empty<AnimatorControllerParameter>();
            controller.layers = Array.Empty<AnimatorControllerLayer>();
            controller.AddLayer("Base Layer");
            return controller;
        }

        /// <summary>An avatar mask at <paramref name="path"/>, reused in place when it exists (keeps its GUID).</summary>
        public static AvatarMask CreateOrLoadMask(string path)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask != null)
                return mask;

            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, path);
            return mask;
        }

        /// <summary>A transition without exit time.</summary>
        public static void Configure(AnimatorStateTransition transition, float duration)
        {
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.hasFixedDuration = true;
        }

        public static void AddFloat(AnimatorController controller, string name, float defaultValue)
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = defaultValue,
            });
        }

        public static void SetPosition(AnimatorStateMachine machine, AnimatorState state, Vector3 position)
        {
            var states = machine.states;
            for (var i = 0; i < states.Length; i++)
                if (states[i].state == state)
                    states[i].position = position;
            machine.states = states;
        }

        /// <summary>
        /// Ground speed of an in-place locomotion clip on <paramref name="model"/> at <paramref name="scale"/>, m/s.
        /// In place, a foot moves backward only while it stands on the ground, so the speed is the backward travel of
        /// both feet over the time they spend moving backward (works for shuffling feet that never leave the floor).
        /// Playing the clip at actualSpeed / this keeps the feet from sliding.
        /// </summary>
        public static float MeasureGroundSpeed(GameObject model, AnimationClip clip, float scale)
        {
            const int samples = 60;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            AnimationMode.StartAnimationMode();
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one * scale;
                var animator = instance.GetComponent<Animator>();
                var feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
                var previous = new float[feet.Length];
                var dt = clip.length / samples;
                float travel = 0f, time = 0f;
                for (var i = 0; i <= samples; i++)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(instance, clip, i * dt);
                    AnimationMode.EndSampling();
                    for (var f = 0; f < feet.Length; f++)
                    {
                        var z = feet[f].position.z;
                        if (i > 0 && z < previous[f])
                        {
                            travel += previous[f] - z;
                            time += dt;
                        }

                        previous[f] = z;
                    }
                }

                if (time <= 0f || travel <= 0f)
                    throw new InvalidOperationException($"Cannot measure the ground speed of '{clip.name}' (no foot moving back).");
                return travel / time;
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// When and which way a melee clip hits: the moment the right hand moves fastest (smoothed over three samples),
        /// as a share of the clip (0..1), and the side it moves to then: +1 = to the character's right (left to right),
        /// -1 = to its left. The model faces +Z.
        /// </summary>
        public static (float Moment, float Sweep) MeasureSwing(GameObject model, AnimationClip clip)
        {
            const int samples = 60;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            AnimationMode.StartAnimationMode();
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var hand = instance.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand);
                if (hand == null)
                    throw new InvalidOperationException($"'{model.name}' has no right hand bone.");

                var positions = new Vector3[samples + 1];
                for (var i = 0; i <= samples; i++)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(instance, clip, clip.length * i / samples);
                    AnimationMode.EndSampling();
                    positions[i] = hand.position;
                }

                var best = -1f;
                var moment = 0.5f;
                var sweep = 1f;
                for (var i = 2; i < samples; i++)
                {
                    // Movement over samples i-2..i+1, centred between i-1 and i.
                    var movement = positions[i + 1] - positions[i - 2];
                    var speed = movement.magnitude;
                    if (speed <= best) continue;
                    best = speed;
                    moment = (i - 0.5f) / samples;
                    sweep = movement.x >= 0f ? 1f : -1f;
                }

                return (moment, sweep);
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Puts the controller on the prefab's root Animator (Humanoid avatar required), no root motion, scale.</summary>
        public static void SetupPrefab(string prefabPath, RuntimeAnimatorController controller, float scale)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var animator = root.GetComponent<Animator>();
                if (animator == null)
                    throw new InvalidOperationException($"'{prefabPath}' has no Animator on its root.");
                if (animator.avatar == null || !animator.avatar.isHuman)
                    throw new InvalidOperationException($"'{prefabPath}' needs a Humanoid avatar.");

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                // Views hidden by visibility are deactivated; on reveal they continue (a corpse stays dead).
                animator.keepAnimatorStateOnDisable = true;
                root.transform.localScale = Vector3.one * scale;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
