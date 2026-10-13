using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Collect Used Third-Party Assets. Asset packs are downloaded into Assets/_Project/Import, which is
    /// git-ignored (hundreds of MB, mostly unused). This keeps the project self-contained:
    /// <list type="number">
    /// <item>strips physics (colliders, rigidbodies — forbidden in this project; packs add them by default) from every
    /// prefab outside Import;</item>
    /// <item>moves everything the project uses from Import/&lt;Pack&gt;/… to Art/ThirdParty/&lt;Pack&gt;/… —
    /// <see cref="AssetDatabase.MoveAsset"/> keeps GUIDs, so prefabs, levels and Addressables stay linked;</item>
    /// <item>checks that nothing outside Import references Import any more;</item>
    /// <item>caps texture sizes for Android and WebGL (<see cref="MobileTextureSize"/>; smaller for normal maps,
    /// emission masks, characters and tiny objects in <see cref="SmallObjectFolders"/>) with normal compression: packs ship 4096² atlases, ~10–22 MB of video memory
    /// each on a phone, while the camera sees a few metres of the maze.</item>
    /// </list>
    /// Run again after taking anything new from a pack. What stays in Import is unused and can be deleted.
    /// </summary>
    internal static class ThirdPartyAssetCollector
    {
        private const string ImportRoot = "Assets/_Project/Import";
        private const string TargetRoot = "Assets/_Project/Art/ThirdParty";
        /// <summary>
        /// 1024: the camera looks almost straight down at ~200 screen pixels per metre and the packs' textures are mostly
        /// colour atlases — 2048 doubled the web download (~4x the texture bytes) without a visible difference.
        /// </summary>
        private const int MobileTextureSize = 1024;
        private static readonly string[] MobilePlatforms = { "Android", "WebGL" };

        /// <summary>Normal maps: the camera looks down, the relief barely shows — half the colour size.</summary>
        private const int NormalMapSize = 512;

        /// <summary>Emission masks (mostly black, a few glowing spots).</summary>
        private const int EmissionSize = 256;

        /// <summary>Characters are a finger tall on screen.</summary>
        private const int CharacterTextureSize = 512;

        /// <summary>Folders (under <see cref="TargetRoot"/>) of objects only a few centimetres on screen, and their size cap.</summary>
        private static readonly (string Folder, int Size)[] SmallObjectFolders = { ("Gabies_Assets/Keys", 256) };

        [MenuItem("Maze/Dev/Collect Used Third-Party Assets")]
        public static void Collect()
        {
            var stripped = StripPhysics();
            AssetDatabase.SaveAssets();

            var used = FindUsedImportAssets();
            var moved = 0;
            var failed = new List<string>();
            foreach (var path in used)
            {
                var target = TargetRoot + path.Substring(ImportRoot.Length);
                EnsureFolder(target.Substring(0, target.LastIndexOf('/')));
                var error = AssetDatabase.MoveAsset(path, target);
                if (string.IsNullOrEmpty(error)) moved++;
                else failed.Add($"{path}: {error}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var capped = CapTextureSizes();

            var remaining = FindUsedImportAssets();
            foreach (var error in failed)
                Debug.LogError("[Maze] Move failed: " + error);
            foreach (var path in remaining)
                Debug.LogError("[Maze] Still referenced from Import: " + path);

            Debug.Log($"[Maze] Third-party assets: physics stripped from {stripped} prefab(s), {moved} file(s) moved to " +
                      $"{TargetRoot}, {capped} texture(s) capped for mobile, {remaining.Count} still in Import" +
                      (remaining.Count == 0 ? " — Import is unused and can be deleted." : "."));
        }

        /// <summary>Import assets referenced (directly or not) by any asset outside Import.</summary>
        private static List<string> FindUsedImportAssets()
        {
            var roots = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !IsInImport(p) && !AssetDatabase.IsValidFolder(p))
                .ToArray();
            return AssetDatabase.GetDependencies(roots, true)
                .Where(IsInImport)
                .Where(p => !AssetDatabase.IsValidFolder(p))
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        private static int StripPhysics()
        {
            var count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsInImport(path)) continue;

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || (asset.GetComponentsInChildren<Collider>(true).Length == 0 &&
                                      asset.GetComponentsInChildren<Rigidbody>(true).Length == 0))
                    continue;

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                        UnityEngine.Object.DestroyImmediate(body);
                    foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                        UnityEngine.Object.DestroyImmediate(collider);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    count++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return count;
        }

        [MenuItem("Maze/Dev/Cap Third-Party Texture Sizes")]
        public static void CapTextureSizesMenu() =>
            Debug.Log($"[Maze] {CapTextureSizes()} third-party texture(s) capped for {string.Join(", ", MobilePlatforms)}.");

        /// <summary>Overrides for <see cref="MobilePlatforms"/>; returns how many textures were changed.</summary>
        private static int CapTextureSizes()
        {
            var changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TargetRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;

                var cap = MobileTextureSize;
                var file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (importer.textureType == TextureImporterType.NormalMap) cap = Math.Min(cap, NormalMapSize);
                if (file.IndexOf("emis", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    file.IndexOf("emmis", StringComparison.OrdinalIgnoreCase) >= 0)
                    cap = Math.Min(cap, EmissionSize);
                if (path.IndexOf("/Characters/", StringComparison.Ordinal) >= 0) cap = Math.Min(cap, CharacterTextureSize);
                foreach (var (folder, folderCap) in SmallObjectFolders)
                    if (path.StartsWith(TargetRoot + "/" + folder + "/", StringComparison.Ordinal))
                        cap = Math.Min(cap, folderCap);
                importer.GetSourceTextureWidthAndHeight(out var width, out var height);
                var size = Math.Min(cap, Math.Max(32, Mathf.NextPowerOfTwo(Math.Max(width, height))));

                var dirty = false;
                foreach (var platform in MobilePlatforms)
                {
                    var settings = importer.GetPlatformTextureSettings(platform);
                    if (settings.overridden && settings.maxTextureSize == size &&
                        settings.textureCompression == TextureImporterCompression.Compressed)
                        continue;

                    settings.overridden = true;
                    settings.maxTextureSize = size;
                    settings.textureCompression = TextureImporterCompression.Compressed;
                    settings.format = TextureImporterFormat.Automatic;
                    importer.SetPlatformTextureSettings(settings);
                    dirty = true;
                }

                if (!dirty) continue;
                importer.SaveAndReimport();
                changed++;
            }

            return changed;
        }

        private static bool IsInImport(string path) =>
            path == ImportRoot || path.StartsWith(ImportRoot + "/", StringComparison.Ordinal);

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1));
        }
    }
}
