using System;
using System.IO;
using Maze.Core.Visual;
using Maze.Presentation.Visual;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Combat Effects: the game's own cartoon combat effects (no blood, no bullet marks) in
    /// Art/Effects, made from code so they can be rebuilt after a tweak here — textures (soft dot, star, tracer),
    /// materials (<c>Maze/Particle</c>, <c>Maze/SwingArc</c>), the tracer mesh and the prefabs — and assigned to
    /// Data/Combat/CombatVisual (kept Addressable with its prefabs). Particle systems never use the Collision module.
    /// Prefabs, materials and textures are overwritten (GUIDs kept), so the definition's references stay valid.
    /// A pack's effects can replace them later in the same slots of the definition.
    /// </summary>
    internal static class CombatEffectsBuilder
    {
        private const string Folder = "Assets/_Project/Art/Effects";
        private const string CombatVisualPath = "Assets/_Project/Data/Combat/CombatVisual.asset";
        private const string OldPlaceholders = "Assets/_Project/Art/Placeholders";

        private static readonly Color FlashColor = new Color(1f, 0.88f, 0.5f);
        private static readonly Color SparkColor = new Color(1f, 0.75f, 0.3f);
        private static readonly Color DustColor = new Color(0.72f, 0.68f, 0.6f, 0.55f);
        private static readonly Color StarColor = new Color(1f, 0.92f, 0.35f);
        private static readonly Color BrassColor = new Color(0.95f, 0.72f, 0.3f);
        private static readonly Color TracerColor = new Color(1f, 0.85f, 0.45f);

        private enum Blend { Additive, Alpha }

        [MenuItem("Maze/Dev/Build Combat Effects")]
        public static void Build()
        {
            if (Shader.Find("Maze/Particle") == null || Shader.Find("Maze/SwingArc") == null)
            {
                Debug.LogError("[Maze] Shaders 'Maze/Particle' and 'Maze/SwingArc' are required.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "Effects");

            var soft = SaveTexture("fx_soft", 64, 64, SoftDot);
            var star = SaveTexture("fx_star", 128, 128, Star);
            var tracerTexture = SaveTexture("fx_tracer", 64, 16, Tracer);

            var additive = SaveMaterial("fx_additive", "Maze/Particle", soft, Blend.Additive, Color.white);
            var dust = SaveMaterial("fx_dust", "Maze/Particle", soft, Blend.Alpha, Color.white);
            var stars = SaveMaterial("fx_star", "Maze/Particle", star, Blend.Alpha, Color.white, overModels: true);
            var shell = SaveMaterial("fx_shell", "Maze/Particle", null, Blend.Alpha, Color.white);
            var tracer = SaveMaterial("fx_tracer", "Maze/Particle", tracerTexture, Blend.Additive, TracerColor);
            var swing = SaveMaterial("fx_swing", "Maze/SwingArc", null, Blend.Alpha, new Color(1f, 1f, 1f, 0.75f));
            swing.SetFloat("_Inner", 0.7f); // a crescent, not a wedge
            var tracerMesh = SaveTracerMesh();

            var visual = AssetDatabase.LoadAssetAtPath<CombatVisualDefinition>(CombatVisualPath);
            if (visual == null)
            {
                visual = ScriptableObject.CreateInstance<CombatVisualDefinition>();
                AssetDatabase.CreateAsset(visual, CombatVisualPath);
            }

            visual.Bullet = Reference(BuildTracer(tracerMesh, tracer));
            visual.TracerLength = 1.2f;
            visual.MuzzleFlash = Reference(BuildMuzzleFlash(additive));
            visual.ShellCasing = Reference(BuildShell(shell));
            visual.Impact = Reference(BuildWallImpact(additive, dust));
            visual.TargetHit = Reference(BuildTargetHit(stars));
            visual.MeleeSwing = Reference(BuildSwing(swing));
            EditorUtility.SetDirty(visual);

            // The first placeholders (cubes and spheres) are replaced.
            foreach (var old in new[] { "fx_bullet", "fx_impact", "fx_melee_swing" })
                AssetDatabase.DeleteAsset($"{OldPlaceholders}/{old}.prefab");

            var problem = LevelDesigner.LevelSync.SyncShared(AddressableAssetSettingsDefaultObject.GetSettings(true));
            AssetDatabase.SaveAssets();
            if (problem != null) Debug.LogWarning("[Maze] " + problem);
            Debug.Log($"[Maze] Combat effects built in {Folder} and assigned to {CombatVisualPath}.");
        }

        // ---- Prefabs ----

        private static GameObject BuildTracer(Mesh mesh, Material material)
        {
            var root = new GameObject("fx_tracer");
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            SetupRenderer(root.AddComponent<MeshRenderer>(), material);
            return SavePrefab(root);
        }

        private static GameObject BuildMuzzleFlash(Material additive)
        {
            var root = Effect("fx_muzzle_flash", 0.12f);

            var flash = Particles(root, "Flash", additive, count: 1, lifetime: (0.05f, 0.07f), speed: (0f, 0f), size: (0.45f, 0.6f), FlashColor);
            Shape(flash, ParticleSystemShapeType.Sphere, radius: 0.01f);
            var main = flash.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLifetime(flash, 1f, 0.4f);

            var sparks = Particles(root, "Sparks", additive, count: 5, lifetime: (0.06f, 0.1f), speed: (4f, 7f), size: (0.05f, 0.09f), SparkColor);
            Shape(sparks, ParticleSystemShapeType.Cone, radius: 0.02f, angle: 14f);
            Stretch(sparks, 2.5f);
            FadeOut(sparks);
            return SavePrefab(root);
        }

        private static GameObject BuildShell(Material material)
        {
            var root = Effect("fx_shell", 0.75f);
            // Up and to the gun's right (+Z of the effect), then falls; fades out about when it reaches the floor.
            var shell = Particles(root, "Shell", material, count: 1, lifetime: (0.6f, 0.65f), speed: (1.8f, 2.4f), size: (1f, 1f), BrassColor);
            shell.transform.localRotation = Quaternion.Euler(-40f, 0f, 0f);
            Shape(shell, ParticleSystemShapeType.Cone, radius: 0.005f, angle: 20f);

            var main = shell.main;
            main.gravityModifier = 1f;
            main.startSize3D = true;
            main.startSizeX = 0.035f;
            main.startSizeY = 0.035f;
            main.startSizeZ = 0.09f;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var spin = shell.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-12f, 12f);
            spin.y = new ParticleSystem.MinMaxCurve(-12f, 12f);
            spin.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
            FadeOut(shell, 0.75f);

            var renderer = shell.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            renderer.alignment = ParticleSystemRenderSpace.Local;
            return SavePrefab(root);
        }

        private static GameObject BuildWallImpact(Material additive, Material dust)
        {
            var root = Effect("fx_wall_impact", 0.5f);

            var sparks = Particles(root, "Sparks", additive, count: 8, lifetime: (0.15f, 0.3f), speed: (2.5f, 5f), size: (0.04f, 0.07f), SparkColor);
            Shape(sparks, ParticleSystemShapeType.Cone, radius: 0.02f, angle: 55f);
            var main = sparks.main;
            main.gravityModifier = 0.6f;
            Stretch(sparks, 2.5f);
            FadeOut(sparks);

            var flash = Particles(root, "Flash", additive, count: 1, lifetime: (0.05f, 0.05f), speed: (0f, 0f), size: (0.35f, 0.35f), FlashColor);
            Shape(flash, ParticleSystemShapeType.Sphere, radius: 0.01f);

            var puff = Particles(root, "Dust", dust, count: 3, lifetime: (0.35f, 0.5f), speed: (0.4f, 0.8f), size: (0.22f, 0.35f), DustColor);
            Shape(puff, ParticleSystemShapeType.Cone, radius: 0.05f, angle: 40f);
            var puffMain = puff.main;
            puffMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLifetime(puff, 0.6f, 1.5f);
            FadeOut(puff, 0.3f);
            return SavePrefab(root);
        }

        private static GameObject BuildTargetHit(Material stars)
        {
            var root = Effect("fx_hit_star", 0.32f);

            // One big star that pops (grows past its size, settles) and fades, plus small ones flying out.
            var star = Particles(root, "Star", stars, count: 1, lifetime: (0.26f, 0.26f), speed: (0f, 0f), size: (0.7f, 0.7f), StarColor);
            Shape(star, ParticleSystemShapeType.Sphere, radius: 0.01f);
            var main = star.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var size = star.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f), new Keyframe(0.25f, 1.15f), new Keyframe(0.5f, 0.95f), new Keyframe(1f, 0.85f)));
            FadeOut(star, 0.4f);

            var small = Particles(root, "SmallStars", stars, count: 5, lifetime: (0.2f, 0.3f), speed: (2f, 3.2f), size: (0.12f, 0.2f), StarColor);
            Shape(small, ParticleSystemShapeType.Hemisphere, radius: 0.05f);
            var smallMain = small.main;
            smallMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            FadeOut(small);
            return SavePrefab(root);
        }

        private static GameObject BuildSwing(Material material)
        {
            var root = Effect("fx_melee_swing", 0.22f);
            var arc = root.AddComponent<SwingArc>();

            var quad = new GameObject("Arc");
            quad.transform.SetParent(root.transform, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lying flat, +v forward, facing up
            quad.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var renderer = quad.AddComponent<MeshRenderer>();
            SetupRenderer(renderer, material);
            arc.Quad = renderer;
            arc.Setup(100f, 1.2f, true);
            return SavePrefab(root);
        }

        // ---- Particle helpers ----

        private static GameObject Effect(string name, float duration)
        {
            var root = new GameObject(name);
            root.AddComponent<CombatEffect>().Duration = duration;
            return root;
        }

        private static ParticleSystem Particles(GameObject root, string name, Material material, int count,
            (float Min, float Max) lifetime, (float Min, float Max) speed, (float Min, float Max) size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = Mathf.Max(lifetime.Max, 0.05f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.Min, lifetime.Max);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.Min, speed.Max);
            main.startSize = new ParticleSystem.MinMaxCurve(size.Min, size.Max);
            main.startColor = color;
            main.maxParticles = Mathf.Max(count, 1) * 2;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = system.shape;
            shape.enabled = false;

            var collision = system.collision;
            collision.enabled = false; // no physics in the project

            SetupRenderer(go.GetComponent<ParticleSystemRenderer>(), material);
            return system;
        }

        private static void Shape(ParticleSystem system, ParticleSystemShapeType type, float radius, float angle = 0f)
        {
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = type;
            shape.radius = radius;
            shape.angle = angle;
        }

        private static void Stretch(ParticleSystem system, float length)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = length;
            renderer.velocityScale = 0.02f;
        }

        private static void SizeOverLifetime(ParticleSystem system, float from, float to)
        {
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
        }

        /// <summary>Alpha stays, then falls to zero over the last <paramref name="share"/> of the lifetime.</summary>
        private static void FadeOut(ParticleSystem system, float share = 0.6f)
        {
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f - share), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
        }

        private static void SetupRenderer(Renderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        // ---- Assets ----

        private static GameObject SavePrefab(GameObject root)
        {
            var path = $"{Folder}/{root.name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static AssetReferenceGameObject Reference(GameObject prefab) =>
            new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)));

        private static Material SaveMaterial(string name, string shaderName, Texture texture, Blend blend, Color color,
            bool overModels = false)
        {
            var path = $"{Folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", color);
            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)(blend == Blend.Additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            }
            if (material.HasProperty("_ZTest"))
                material.SetFloat("_ZTest", (float)(overModels ? CompareFunction.Always : CompareFunction.LessEqual));

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Two crossed strips from z = -1 (tail, u = 0) to z = 0 (head, u = 1): visible from any side.</summary>
        private static Mesh SaveTracerMesh()
        {
            const float halfWidth = 0.05f;
            var path = $"{Folder}/fx_tracer_mesh.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, path);
            }

            mesh.Clear();
            mesh.name = "fx_tracer_mesh";
            mesh.vertices = new[]
            {
                new Vector3(-halfWidth, 0f, -1f), new Vector3(halfWidth, 0f, -1f), new Vector3(-halfWidth, 0f, 0f), new Vector3(halfWidth, 0f, 0f),
                new Vector3(0f, -halfWidth, -1f), new Vector3(0f, halfWidth, -1f), new Vector3(0f, -halfWidth, 0f), new Vector3(0f, halfWidth, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Texture2D SaveTexture(string name, int width, int height, Func<float, float, Color> pixel)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                pixels[y * width + x] = pixel((x + 0.5f) / width, (y + 0.5f) / height);
            texture.SetPixels(pixels);
            texture.Apply();

            var path = $"{Folder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.maxTextureSize = Mathf.Max(width, height);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---- Texture shapes (u, v in 0..1) ----

        private static Color SoftDot(float u, float v)
        {
            var r = Mathf.Clamp01(new Vector2(u * 2f - 1f, v * 2f - 1f).magnitude);
            var a = 1f - r;
            return new Color(1f, 1f, 1f, a * a);
        }

        /// <summary>Along u: transparent tail → bright head; across v: soft edges, white core.</summary>
        private static Color Tracer(float u, float v)
        {
            var across = 1f - Mathf.Abs(v * 2f - 1f);
            var along = Mathf.Pow(u, 1.5f) * Mathf.Clamp01((1f - u) * 12f);
            var core = Mathf.Clamp01(across * 1.6f - 0.3f);
            var a = along * across * across;
            return new Color(1f, 1f - 0.2f * (1f - core), 1f - 0.4f * (1f - core), a);
        }

        /// <summary>Cartoon five-pointed star: white fill, dark outline (tinted by the particle colour).</summary>
        private static Color Star(float u, float v)
        {
            const float outer = 0.95f, inner = 0.45f, outline = 0.09f, aa = 0.02f;
            var p = new Vector2(u * 2f - 1f, v * 2f - 1f);
            var distance = StarDistance(p, outer, inner);
            var alpha = Mathf.Clamp01((aa - distance) / (2f * aa));
            var fill = Mathf.Clamp01((-outline - distance + aa) / (2f * aa));
            var shade = Mathf.Lerp(0.35f, 1f, fill);
            return new Color(shade, shade, shade, alpha);
        }

        /// <summary>Signed distance to a star polygon pointing up (negative inside).</summary>
        private static float StarDistance(Vector2 p, float outer, float inner)
        {
            const int points = 5;
            var best = float.MaxValue;
            var inside = false;
            var count = points * 2;
            for (var i = 0; i < count; i++)
            {
                var a = Vertex(i, outer, inner, points);
                var b = Vertex((i + 1) % count, outer, inner, points);
                var ab = b - a;
                var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }

            return inside ? -best : best;
        }

        private static Vector2 Vertex(int index, float outer, float inner, int points)
        {
            var angle = Mathf.PI * 0.5f + index * Mathf.PI / points;
            var radius = index % 2 == 0 ? outer : inner;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
    }
}
