using System;
using System.Collections.Generic;
using System.IO;
using Purge.Core.Horde;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Horde Visuals: bakes the zombies for mass rendering (<see cref="HordeVisualDefinition"/>,
    /// shader Purge/Horde).
    /// <list type="number">
    /// <item>Model — the skinned mesh of the first prefab in Art/Zombie; look variants — the albedo of every prefab
    /// there (one material each).</item>
    /// <item>Clips — every "Zombie_*" clip in Art/Animations whose name says what it is (idle, walk, run, attack,
    /// death; others are skipped), sampled at <see cref="FrameRate"/>.</item>
    /// <item>The mesh is welded by position + UV (normals dropped: the shader takes flat normals from derivatives),
    /// UV1.x = vertex column, UV2 = two strongest bones (indices, weights renormalised).</item>
    /// <item>Bone texture: 3 texels per bone (rows of the 3×4 matrix mesh → model root), a row per frame; vertex
    /// texture: model-space position per vertex (skinned on the CPU with all four weights), a row per frame. Both
    /// RGBAHalf, point-sampled.</item>
    /// <item>Assets are updated in place (same GUIDs): Art/Horde (mesh, textures, materials), Data/Horde (visual
    /// definition; the stress test settings are created once and keep their values).</item>
    /// </list>
    /// </summary>
    internal static class HordeVisualsBuilder
    {
        private const string ZombieArtPath = "Assets/_Project/Art/Zombie";
        private const string ArtPath = "Assets/_Project/Art/Horde";
        private const string DataPath = "Assets/_Project/Data/Horde";
        private const string VisualPath = DataPath + "/ZombieHorde.asset";
        private const string StressTestPath = DataPath + "/StressTest.asset";
        private const string EffectsPath = "Assets/_Project/Art/Effects";
        private const string ClipPrefix = "Zombie_";
        private const float FrameRate = 30f;

        private static readonly (string Word, HordeClipKind Kind)[] ClipKinds =
        {
            ("idle", HordeClipKind.Idle), ("walk", HordeClipKind.Walk), ("run", HordeClipKind.Run),
            ("attack", HordeClipKind.Attack), ("death", HordeClipKind.Death),
        };

        [MenuItem("Purge/Dev/Build Horde Visuals")]
        public static void Build()
        {
            try
            {
                var prefabs = FindPrefabs();
                if (prefabs.Count == 0)
                    throw new InvalidOperationException($"No zombie prefabs in {ZombieArtPath}.");

                EnsureFolder(ArtPath);
                EnsureFolder(DataPath);
                var visual = Bake(prefabs);
                var stress = AssetDatabase.LoadAssetAtPath<StressTestDefinition>(StressTestPath);
                if (stress == null)
                {
                    stress = ScriptableObject.CreateInstance<StressTestDefinition>();
                    AssetDatabase.CreateAsset(stress, StressTestPath);
                }

                stress.Configure(visual, AssetDatabase.LoadAssetAtPath<Material>(EffectsPath + "/fx_tracer.mat"),
                    AssetDatabase.LoadAssetAtPath<Material>(EffectsPath + "/fx_additive.mat"), SaveFireMaterial());
                EditorUtility.SetDirty(stress);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogError("[Purge] Building horde visuals failed: " + e.Message);
                throw;
            }
        }

        private static List<GameObject> FindPrefabs()
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ZombieArtPath }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(StringComparer.Ordinal);
            return paths.ConvertAll(AssetDatabase.LoadAssetAtPath<GameObject>);
        }

        private static List<(AnimationClip Clip, HordeClipKind Kind)> FindClips()
        {
            var clips = new List<(AnimationClip, HordeClipKind)>();
            var guids = AssetDatabase.FindAssets(ClipPrefix + " t:Model", new[] { CharacterAnimationUtility.AnimationsPath });
            var names = new List<string>();
            foreach (var guid in guids)
            {
                var name = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                if (name.StartsWith(ClipPrefix, StringComparison.Ordinal))
                    names.Add(name);
            }

            names.Sort(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var lower = name.Substring(ClipPrefix.Length).ToLowerInvariant();
                foreach (var (word, kind) in ClipKinds)
                {
                    if (!lower.StartsWith(word, StringComparison.Ordinal))
                        continue;
                    clips.Add((CharacterAnimationUtility.LoadClip(name), kind));
                    break;
                }
            }

            return clips;
        }

        private static HordeVisualDefinition Bake(List<GameObject> prefabs)
        {
            var clips = FindClips();
            if (clips.Count == 0)
                throw new InvalidOperationException($"No '{ClipPrefix}*' idle/walk/run/attack/death clips.");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[0]);
            AnimationMode.StartAnimationMode();
            try
            {
                var scale = prefabs[0].transform.localScale.x;
                var root = instance.transform;
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.localScale = Vector3.one;
                var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                if (renderer == null)
                    throw new InvalidOperationException($"'{prefabs[0].name}' has no SkinnedMeshRenderer.");

                var source = renderer.sharedMesh;
                var bones = renderer.bones;
                var bindposes = source.bindposes;
                var weld = Weld(source, out var welded);
                var mesh = SaveMesh(source, weld, welded);

                // Frames of every clip, one after another.
                var baked = new List<HordeClip>();
                var frameCount = 0;
                foreach (var (clip, kind) in clips)
                {
                    var loop = clip.isLooping;
                    var count = loop
                        ? Mathf.Max(2, Mathf.RoundToInt(clip.length * FrameRate))
                        : Mathf.Max(2, Mathf.CeilToInt(clip.length * FrameRate) + 1);
                    baked.Add(new HordeClip
                    {
                        Name = clip.name, Kind = kind, FirstFrame = frameCount, FrameCount = count,
                        Length = clip.length, Loop = loop,
                    });
                    frameCount += count;
                }

                var boneTexture = new Color[bones.Length * 3 * frameCount];
                var vertexTexture = new Color[welded.Count * frameCount];
                var vertices = source.vertices;
                var weights = source.boneWeights;
                var matrices = new Matrix4x4[bones.Length];
                var boneWidth = bones.Length * 3;

                for (var c = 0; c < clips.Count; c++)
                {
                    var clip = clips[c].Clip;
                    var info = baked[c];
                    for (var f = 0; f < info.FrameCount; f++)
                    {
                        var time = info.Loop
                            ? clip.length * f / info.FrameCount
                            : Mathf.Min(clip.length, f / FrameRate);
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(instance, clip, time);
                        AnimationMode.EndSampling();

                        var row = info.FirstFrame + f;
                        var toRoot = root.worldToLocalMatrix;
                        for (var b = 0; b < bones.Length; b++)
                        {
                            var m = toRoot * bones[b].localToWorldMatrix * bindposes[b];
                            matrices[b] = m;
                            var offset = row * boneWidth + b * 3;
                            boneTexture[offset] = new Color(m.m00, m.m01, m.m02, m.m03);
                            boneTexture[offset + 1] = new Color(m.m10, m.m11, m.m12, m.m13);
                            boneTexture[offset + 2] = new Color(m.m20, m.m21, m.m22, m.m23);
                        }

                        for (var v = 0; v < welded.Count; v++)
                        {
                            var original = welded[v];
                            var p = Skin(vertices[original], weights[original], matrices);
                            vertexTexture[row * welded.Count + v] = new Color(p.x, p.y, p.z, 1f);
                        }
                    }
                }

                var boneAsset = SaveTexture(ArtPath + "/ZombieHorde_Bones.asset", boneWidth, frameCount, boneTexture);
                var vertexAsset = SaveTexture(ArtPath + "/ZombieHorde_Vertices.asset", welded.Count, frameCount, vertexTexture);
                var materials = SaveMaterials(prefabs);

                var visual = AssetDatabase.LoadAssetAtPath<HordeVisualDefinition>(VisualPath);
                if (visual == null)
                {
                    visual = ScriptableObject.CreateInstance<HordeVisualDefinition>();
                    AssetDatabase.CreateAsset(visual, VisualPath);
                }

                visual.Configure(mesh, materials, vertexAsset, boneAsset, bones.Length, FrameRate, baked.ToArray(), scale,
                    source.vertexCount);
                EditorUtility.SetDirty(visual);

                var vertexBytes = welded.Count * frameCount * 8;
                var boneBytes = boneWidth * frameCount * 8;
                Debug.Log($"[Purge] Horde visuals built: {source.vertexCount} → {welded.Count} vertices, " +
                          $"{bones.Length} bones, {clips.Count} clips / {frameCount} frames at {FrameRate} fps; " +
                          $"VAT {welded.Count}x{frameCount} ({vertexBytes / 1024} KB), bones {boneWidth}x{frameCount} " +
                          $"({boneBytes / 1024} KB), {materials.Length} variant(s).");
                return visual;
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Vector3 Skin(Vector3 vertex, BoneWeight weight, Matrix4x4[] matrices)
        {
            var p = matrices[weight.boneIndex0].MultiplyPoint3x4(vertex) * weight.weight0;
            if (weight.weight1 > 0f) p += matrices[weight.boneIndex1].MultiplyPoint3x4(vertex) * weight.weight1;
            if (weight.weight2 > 0f) p += matrices[weight.boneIndex2].MultiplyPoint3x4(vertex) * weight.weight2;
            if (weight.weight3 > 0f) p += matrices[weight.boneIndex3].MultiplyPoint3x4(vertex) * weight.weight3;
            return p;
        }

        /// <summary>Source vertex index → welded index; <paramref name="welded"/> — first source vertex of each welded one.</summary>
        internal static int[] Weld(Mesh source, out List<int> welded)
        {
            var vertices = source.vertices;
            var uvs = source.uv;
            var map = new int[vertices.Length];
            var lookup = new Dictionary<(Vector3, Vector2), int>();
            welded = new List<int>();
            for (var i = 0; i < vertices.Length; i++)
            {
                var key = (Round(vertices[i]), Round(uvs[i]));
                if (!lookup.TryGetValue(key, out var index))
                {
                    index = welded.Count;
                    lookup.Add(key, index);
                    welded.Add(i);
                }

                map[i] = index;
            }

            return map;
        }

        private static Vector3 Round(Vector3 v) =>
            new Vector3(Mathf.Round(v.x * 1e5f), Mathf.Round(v.y * 1e5f), Mathf.Round(v.z * 1e5f));

        private static Vector2 Round(Vector2 v) => new Vector2(Mathf.Round(v.x * 1e4f), Mathf.Round(v.y * 1e4f));

        private static Mesh SaveMesh(Mesh source, int[] map, List<int> welded)
        {
            var vertices = source.vertices;
            var uvs = source.uv;
            var weights = source.boneWeights;
            var positions = new Vector3[welded.Count];
            var uv0 = new Vector2[welded.Count];
            var uv1 = new Vector2[welded.Count];
            var uv2 = new Vector4[welded.Count];
            for (var v = 0; v < welded.Count; v++)
            {
                var original = welded[v];
                positions[v] = vertices[original];
                uv0[v] = uvs[original];
                uv1[v] = new Vector2(v, 0f);
                uv2[v] = TopTwoBones(weights[original]);
            }

            var triangles = new List<int>();
            for (var s = 0; s < source.subMeshCount; s++)
                foreach (var index in source.GetTriangles(s))
                    triangles.Add(map[index]);

            var path = ArtPath + "/ZombieHorde_Mesh.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, path);
            }

            mesh.Clear();
            mesh.name = "ZombieHorde_Mesh";
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = positions;
            mesh.uv = uv0;
            mesh.uv2 = uv1;
            mesh.SetUVs(2, uv2);
            mesh.SetTriangles(triangles, 0);
            // Animated: generous bounds around the standing model (instanced draws use their own bounds anyway).
            mesh.bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(3f, 2.5f, 3f));
            mesh.UploadMeshData(false);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        internal static Vector4 TopTwoBones(BoneWeight w)
        {
            var entries = new[]
            {
                (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3),
            };
            Array.Sort(entries, (a, b) => b.Item2.CompareTo(a.Item2));
            var sum = entries[0].Item2 + entries[1].Item2;
            if (sum <= 0f)
                return new Vector4(entries[0].Item1, 0f, 1f, 0f);
            return new Vector4(entries[0].Item1, entries[1].Item1, entries[0].Item2 / sum, entries[1].Item2 / sum);
        }

        internal static Texture2D SaveTexture(string path, int width, int height, Color[] pixels)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
                AssetDatabase.CreateAsset(texture, path);
            }
            else
            {
                texture.Reinitialize(width, height, TextureFormat.RGBAHalf, false);
            }

            texture.name = Path.GetFileNameWithoutExtension(path);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            EditorUtility.SetDirty(texture);
            return texture;
        }

        private static Material[] SaveMaterials(List<GameObject> prefabs)
        {
            var shader = Shader.Find("Purge/Horde") ?? throw new InvalidOperationException("Shader Purge/Horde not found.");
            var materials = new Material[prefabs.Count];
            for (var i = 0; i < prefabs.Count; i++)
            {
                var source = prefabs[i].GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
                var path = $"{ArtPath}/ZombieHorde_{i + 1:00}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }

                material.shader = shader;
                material.SetTexture("_MainTex", source.GetTexture("_MainTex"));
                material.SetColor("_Color", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
                material.SetTexture("_VatTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath + "/ZombieHorde_Vertices.asset"));
                material.SetTexture("_BoneTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPath + "/ZombieHorde_Bones.asset"));
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                materials[i] = material;
            }

            return materials;
        }

        private static Material SaveFireMaterial()
        {
            var shader = Shader.Find("Purge/FireCells") ?? throw new InvalidOperationException("Shader Purge/FireCells not found.");
            var path = ArtPath + "/FireCells.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            // Look is tuned in the shader's defaults.
            var defaults = new Material(shader);
            material.CopyPropertiesFromMaterial(defaults);
            UnityEngine.Object.DestroyImmediate(defaults);
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
