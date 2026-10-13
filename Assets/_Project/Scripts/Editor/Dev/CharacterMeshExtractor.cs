using System;
using System.Collections.Generic;
using Maze.Editor.LevelDesigner;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Extract Character Meshes. A Synty pack keeps all its characters in one model file
    /// (Characters.fbx, ~60 meshes), and a build that references any of them carries the whole file once it is an
    /// explicit Addressables entry. This copies the meshes and avatars the character prefabs (<see cref="PrefabFolders"/>)
    /// really use into <see cref="TargetFolder"/> and points the prefabs at the copies; then the model file is no longer
    /// referenced and not built. Copies are updated in place on a rerun (GUIDs kept). Run after a builder assigned a new
    /// model to a character prefab.
    /// </summary>
    internal static class CharacterMeshExtractor
    {
        private static readonly string[] PrefabFolders = { "Assets/_Project/Art/Player", "Assets/_Project/Art/Zombie" };
        private const string TargetFolder = "Assets/_Project/Art/Characters";

        [MenuItem("Maze/Dev/Extract Character Meshes")]
        public static void Extract()
        {
            if (!AssetDatabase.IsValidFolder(TargetFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "Characters");

            var copies = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            var changedPrefabs = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", PrefabFolders))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var changed = false;
                    foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var copy = CopyOf(renderer.sharedMesh, copies);
                        if (copy == null) continue;
                        renderer.sharedMesh = (Mesh)copy;
                        changed = true;
                    }

                    foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                    {
                        var copy = CopyOf(animator.avatar, copies);
                        if (copy == null) continue;
                        animator.avatar = (Avatar)copy;
                        changed = true;
                    }

                    if (!changed) continue;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changedPrefabs++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var shared = settings != null ? LevelSync.IsolateDuplicates(settings) : 0;
            Debug.Log($"[Maze] Character meshes: {copies.Count} asset(s) copied to {TargetFolder}, {changedPrefabs} prefab(s) " +
                      $"updated; '{LevelSync.SharedDependenciesGroup}' rebuilt ({shared} entries).");
        }

        /// <summary>The project's own copy of a mesh or avatar that lives inside a model file; null if it is not in one.</summary>
        private static UnityEngine.Object CopyOf(UnityEngine.Object source, Dictionary<UnityEngine.Object, UnityEngine.Object> copies)
        {
            if (source == null) return null;
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (!(AssetImporter.GetAtPath(sourcePath) is ModelImporter)) return null;
            if (copies.TryGetValue(source, out var known)) return known;

            var target = $"{TargetFolder}/{source.name}.asset";
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name;
            if (copy is Mesh mesh)
            {
                // Same as the model's import settings: no CPU copy of the mesh at runtime.
                var serialized = new SerializedObject(mesh);
                serialized.FindProperty("m_IsReadable").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var existing = AssetDatabase.LoadMainAssetAtPath(target);
            if (existing != null && existing.GetType() == copy.GetType())
            {
                EditorUtility.CopySerialized(copy, existing);
                existing.name = source.name;
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(copy);
                copy = existing;
            }
            else if (existing != null)
            {
                throw new InvalidOperationException($"'{target}' exists and is not a {copy.GetType().Name}.");
            }
            else
            {
                AssetDatabase.CreateAsset(copy, target);
            }

            copies[source] = copy;
            return copy;
        }
    }
}
