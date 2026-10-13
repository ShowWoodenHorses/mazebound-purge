using System.Collections.Generic;
using System.Linq;
using Maze.Core.Visual;
using Maze.Presentation.Visual;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Prepares object models for the main theme (<see cref="ThemePath"/>) and fills its sets; variant ids are the
    /// prefab names, weights of existing variants stay. Every prefab loses physics (colliders, rigidbodies) and shadow
    /// casting; materials switch to Maze/Lit (<see cref="LitMaterialsConverter"/>). Run after adding models, then
    /// Collect Used Third-Party Assets.
    /// <list type="bullet">
    /// <item><b>Build Light Fixtures</b> — torches in <c>Art/Lighting</c> (mount plate at the origin, wall at −Z,
    /// torch pointing +Z; see <see cref="LightFixtures"/>): a simple cartoon flame (soft additive blobs rising and
    /// shrinking, a few embers; material <c>Art/Effects/fx_additive</c>) on top of the torch head, tinted by the light
    /// colour at runtime (<see cref="TorchFlame"/>). The Light set goes to the main and the placeholder themes.</item>
    /// <item><b>Build Exits</b> — ladders in <c>Art/Exit</c>: the model moves into a child so its back touches the wall
    /// behind the exit cell (−Z at rotation 0; the exit faces the open side, see ObjectOrientation).</item>
    /// </list>
    /// </summary>
    internal static class LevelPropsBuilder
    {
        private const string LightingFolder = "Assets/_Project/Art/Lighting";
        private const string ExitFolder = "Assets/_Project/Art/Exit";
        private const string ThemesFolder = "Assets/_Project/Data/Themes";
        private const string ThemePath = ThemesFolder + "/CastleBlockTheme.asset";
        private const string PlaceholderThemePath = ThemesFolder + "/PlaceholderTheme.asset";
        private const string LightSetPath = ThemesFolder + "/CastleBlockLightSet.asset";
        private const string ExitSetPath = ThemesFolder + "/CastleBlockExitSet.asset";
        private const string FlameMaterialPath = "Assets/_Project/Art/Effects/fx_additive.mat";

        private const string FlameChild = "Flame";
        private const string ModelChild = "Model";

        /// <summary>Gap between a ladder's back and the wall, m.</summary>
        private const float WallGap = 0.02f;

        private static readonly Color FlameHot = new Color(1f, 0.93f, 0.6f);
        private static readonly Color FlameWarm = new Color(1f, 0.62f, 0.22f);
        private static readonly Color EmberColor = new Color(1f, 0.7f, 0.3f);

        [MenuItem("Maze/Dev/Build Light Fixtures")]
        public static void BuildLightFixtures()
        {
            var flameMaterial = AssetDatabase.LoadAssetAtPath<Material>(FlameMaterialPath);
            if (flameMaterial == null)
            {
                Debug.LogError($"[Maze] {FlameMaterialPath} not found: run Maze → Dev → Build Combat Effects first.");
                return;
            }

            var prefabs = Prefabs(LightingFolder);
            foreach (var path in prefabs)
                Edit(path, root =>
                {
                    StripPhysics(root);
                    NoShadows(root);
                    BuildFlame(root, flameMaterial);
                });

            var set = FillSet(LightSetPath, VisualKind.Light, prefabs);
            AssignSet(ThemePath, VisualKind.Light, set);
            AssignSet(PlaceholderThemePath, VisualKind.Light, set);
            LitMaterialsConverter.Convert();
            Debug.Log($"[Maze] Light fixtures built: {set.Variants.Count} variant(s) in {LightSetPath}.");
        }

        [MenuItem("Maze/Dev/Build Exits")]
        public static void BuildExits()
        {
            var prefabs = Prefabs(ExitFolder);
            foreach (var path in prefabs)
                Edit(path, root =>
                {
                    StripPhysics(root);
                    NoShadows(root);
                    AgainstBackWall(root);
                });

            var set = FillSet(ExitSetPath, VisualKind.Exit, prefabs);
            AssignSet(ThemePath, VisualKind.Exit, set);
            LitMaterialsConverter.Convert();
            Debug.Log($"[Maze] Exits built: {set.Variants.Count} variant(s) in {ExitSetPath}.");
        }

        // ---- Light fixtures ----

        private static void BuildFlame(GameObject root, Material material)
        {
            var old = root.transform.Find(FlameChild);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var flame = new GameObject(FlameChild).transform;
            flame.SetParent(root.transform, false);
            flame.localPosition = FlamePoint(root);

            var fire = Looping(flame, "Fire", material, rate: 18f, lifetime: (0.45f, 0.65f), size: (0.17f, 0.26f), maxParticles: 18);
            var fireMain = fire.main;
            fireMain.startColor = new ParticleSystem.MinMaxGradient(FlameHot, FlameWarm);
            fireMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Shape(fire, ParticleSystemShapeType.Sphere, 0.035f);
            Rise(fire, 0.4f, 0.65f);
            var fireSize = fire.sizeOverLifetime;
            fireSize.enabled = true;
            fireSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.7f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f)));
            Fade(fire, new Color(1f, 0.45f, 0.15f), fadeIn: 0.15f);

            var embers = Looping(flame, "Embers", material, rate: 2.5f, lifetime: (0.6f, 1f), size: (0.025f, 0.04f), maxParticles: 4);
            var embersMain = embers.main;
            embersMain.startColor = EmberColor;
            embersMain.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            Shape(embers, ParticleSystemShapeType.Cone, 0.03f, angle: 18f);
            embers.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone along +Y
            Fade(embers, Color.white, fadeIn: 0f);

            var torch = root.GetComponent<TorchFlame>();
            if (torch == null) torch = root.AddComponent<TorchFlame>();
            torch.Systems = new[] { fire, embers };
            EditorUtility.SetDirty(torch);
        }

        /// <summary>Top of the torch head: centre of the highest vertices, slightly below the top.</summary>
        private static Vector3 FlamePoint(GameObject root)
        {
            var points = new List<Vector3>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                var toRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (var vertex in filter.sharedMesh.vertices)
                    points.Add(toRoot.MultiplyPoint3x4(vertex));
            }

            if (points.Count == 0)
                return Vector3.up * 0.3f;

            var top = points.Max(p => p.y);
            var head = points.Where(p => p.y >= top - 0.05f).ToList();
            return new Vector3(head.Average(p => p.x), top - 0.02f, head.Average(p => p.z));
        }

        private static ParticleSystem Looping(Transform parent, string name, Material material, float rate,
            (float Min, float Max) lifetime, (float Min, float Max) size, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.playOnAwake = true;
            main.loop = true;
            main.prewarm = true; // fully lit as soon as it is shown
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.Min, lifetime.Max);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.Min, size.Max);
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = system.emission;
            emission.rateOverTime = rate;

            var shape = system.shape;
            shape.enabled = false;

            var collision = system.collision;
            collision.enabled = false; // no physics in the project

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
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

        /// <summary>Upward drift (world up: torches are only turned around Y).</summary>
        private static void Rise(ParticleSystem system, float min, float max)
        {
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(min, max);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        /// <summary>Colour goes from white (start colour) to <paramref name="end"/>; alpha fades in, then out.</summary>
        private static void Fade(ParticleSystem system, Color end, float fadeIn)
        {
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(end, 1f) },
                fadeIn > 0f
                    ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0f, 1f) }
                    : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
        }

        // ---- Exits ----

        /// <summary>The model goes into a child placed so its back (−Z) touches the wall at z = −0.5.</summary>
        private static void AgainstBackWall(GameObject root)
        {
            var model = root.transform.Find(ModelChild);
            var filter = root.GetComponent<MeshFilter>();
            if (model == null && filter != null)
            {
                model = new GameObject(ModelChild).transform;
                model.SetParent(root.transform, false);
                model.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = root.GetComponent<MeshRenderer>();
                var moved = model.gameObject.AddComponent<MeshRenderer>();
                moved.sharedMaterials = renderer.sharedMaterials;
                moved.shadowCastingMode = ShadowCastingMode.Off;
                Object.DestroyImmediate(renderer);
                Object.DestroyImmediate(filter);
            }

            if (model == null)
                return;

            var mesh = model.GetComponent<MeshFilter>().sharedMesh;
            model.localPosition = new Vector3(-mesh.bounds.center.x, -mesh.bounds.min.y, -0.5f + WallGap - mesh.bounds.min.z);
        }

        // ---- Common ----

        private static List<string> Prefabs(string folder) =>
            AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path)
                .ToList();

        private static void Edit(string path, System.Action<GameObject> edit)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void StripPhysics(GameObject root)
        {
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                Object.DestroyImmediate(body);
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
        }

        private static void NoShadows(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        /// <summary>Variants = the prefabs (id = name); existing weights and categories stay, missing prefabs go.</summary>
        private static VisualSet FillSet(string setPath, VisualKind kind, List<string> prefabs)
        {
            var set = AssetDatabase.LoadAssetAtPath<VisualSet>(setPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<VisualSet>();
                AssetDatabase.CreateAsset(set, setPath);
            }

            set.Kind = kind;
            var variants = set.MutableVariants;
            foreach (var path in prefabs)
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                var id = System.IO.Path.GetFileNameWithoutExtension(path);
                var existing = variants.FirstOrDefault(v => v.Id == id);
                if (existing == null)
                    variants.Add(new VisualVariant(id, 1, VisualCategory.General, null, new AssetReferenceGameObject(guid)));
                else if (existing.Prefab == null || existing.Prefab.AssetGUID != guid)
                    variants[variants.IndexOf(existing)] = new VisualVariant(id, existing.Weight, existing.Category,
                        existing.Definition, new AssetReferenceGameObject(guid));
            }

            variants.RemoveAll(v => !prefabs.Any(path => AssetDatabase.AssetPathToGUID(path) == v.Prefab?.AssetGUID));
            if (set.FindVariant(set.DefaultVariantId) == null)
                set.DefaultVariantId = null;
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return set;
        }

        private static void AssignSet(string themePath, VisualKind kind, VisualSet set)
        {
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(themePath);
            if (theme == null || theme.GetSet(kind) == set)
                return;

            theme.SetSet(kind, set);
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
        }
    }
}
