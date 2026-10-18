using System.Collections.Generic;
using Purge.Presentation.Visual;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.Rendering;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Pack Effects (stage Э1): cleaned copies of the pack effects listed in <see cref="FxImportList"/>
    /// in Art/Effects/&lt;pack&gt;/ (the copy keeps its GUID on a rebuild, so references stay):
    /// <list type="bullet">
    /// <item>no physics, lamps or sound: the Collision and Lights modules are off; Light, AudioSource, colliders and every
    /// script of the pack (<c>PolygonSoundSpawn</c>…) are removed — the shot's light is <c>PurgeLighting</c>, sounds come
    /// from the audio catalog;</item>
    /// <item>materials are copied to own unlit shaders (no lamps in levels — lit particles went dark):
    /// <c>Purge/Particle</c> (additive, alpha, multiply), <c>Purge/ParticleRim</c> (the rim glow of Arsenal's meshes),
    /// opaque meshes (shards, debris) — <c>Purge/Lit</c>;</item>
    /// <item>for phones: the Noise module is off, particles are capped per system (<see cref="MaxParticles"/>), bursts and
    /// emission rate are cut to the entry's share; renderers cast and receive no shadows, use no probes;</item>
    /// <item>one-shot: no looping, no play on awake; a <see cref="CombatEffect"/> on the root lasts as long as the
    /// longest system (delay + duration + lifetime).</item>
    /// </list>
    /// The copies are Addressable (Purge Visuals). Textures and meshes stay in Import until
    /// Collect Used Third-Party Assets moves them (run it after this).
    /// </summary>
    internal static class FxPackBuilder
    {
        public const float DefaultParticleShare = 0.6f;

        /// <summary>A system of a pack effect has up to 1000; a pooled one-shot shows a few dozen.</summary>
        public const int MaxParticles = 64;

        private const string ImportRoot = "Assets/_Project/Import/FX/";
        private const string TargetRoot = "Assets/_Project/Art/Effects";
        private const float MaxDuration = 6f;

        [MenuItem("Purge/Dev/Build Pack Effects")]
        public static void Build()
        {
            foreach (var shader in new[] { "Purge/Particle", "Purge/ParticleRim", "Purge/Lit" })
                if (Shader.Find(shader) == null)
                {
                    Debug.LogError($"[Purge] Shader '{shader}' is required.");
                    return;
                }

            var list = AssetDatabase.LoadAssetAtPath<FxImportList>(FxImportList.Path);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<FxImportList>();
                AssetDatabase.CreateAsset(list, FxImportList.Path);
                Debug.Log($"[Purge] Created {FxImportList.Path}: drag effects of Import/FX into it and build again.");
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var group = settings.FindGroup(LevelDesigner.LevelSync.VisualsGroup);
            var materials = new Dictionary<Material, Material>();
            var built = 0;
            foreach (var entry in list.Effects)
            {
                var prefab = BuildEffect(entry, materials);
                if (prefab == null) continue;
                var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
                if (group != null && settings.FindAssetEntry(guid) == null)
                    settings.CreateOrMoveEntry(guid, group);
                built++;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Purge] Pack effects: {built} of {list.Effects.Count} built, {materials.Count} material(s) converted. " +
                      "Run Collect Used Third-Party Assets to move their textures and meshes out of Import.");
        }

        private static GameObject BuildEffect(FxImportList.Entry entry, Dictionary<Material, Material> materials)
        {
            var sourcePath = entry.Source != null ? AssetDatabase.GetAssetPath(entry.Source) : null;
            if (sourcePath == null || !sourcePath.StartsWith(ImportRoot))
            {
                Debug.LogWarning($"[Purge] FX import: '{sourcePath ?? "(empty)"}' is not a prefab under {ImportRoot} — skipped.");
                return null;
            }

            var pack = sourcePath.Substring(ImportRoot.Length).Split('/')[0].Replace(" ", string.Empty);
            var folder = $"{TargetRoot}/{pack}";
            EnsureFolder(folder);
            EnsureFolder(folder + "/Materials");

            var name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Source.name : entry.Name.Trim();
            var root = (GameObject)PrefabUtility.InstantiatePrefab(entry.Source);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = name;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            StripComponents(root);
            var scaled = !Mathf.Approximately(entry.Scale, 1f);
            root.transform.localScale *= entry.Scale;

            var duration = 0.05f;
            foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                duration = Mathf.Max(duration, Trim(system, entry.Particles, scaled));
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer == null) continue;
                SetupRenderer(renderer);
                renderer.sharedMaterials = Convert(renderer.sharedMaterials, folder, materials);
                if (renderer.trailMaterial != null)
                    renderer.trailMaterial = Convert(renderer.trailMaterial, folder, materials);
            }

            // Meshes of the effect that are not particles (rare): unlit-converted too, no shadows.
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                SetupRenderer(renderer);
                renderer.sharedMaterials = Convert(renderer.sharedMaterials, folder, materials);
            }

            var effect = root.GetComponent<CombatEffect>() ?? root.AddComponent<CombatEffect>();
            effect.Duration = Mathf.Min(duration, MaxDuration);
            effect.Radius = entry.Radius;
            if (entry.Radius > 0f) // scaled at runtime: every system follows the root's scale
                foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = system.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ---- Cleaning ----

        private static void StripComponents(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var remove = component is Light || component is AudioSource || component is Collider ||
                             component is Rigidbody || component is LODGroup ||
                             (component is MonoBehaviour && !(component is CombatEffect));
                if (remove) Object.DestroyImmediate(component);
            }

            // Missing scripts of the pack (a script that did not come along).
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
        }

        /// <summary>Phone-friendly one-shot. Returns how long the system shows anything, seconds.</summary>
        private static float Trim(ParticleSystem system, float share, bool scaled)
        {
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = Mathf.Min(main.maxParticles, MaxParticles);
            if (scaled) main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var collision = system.collision;
            collision.enabled = false; // no physics in the project
            var lights = system.lights;
            lights.enabled = false;
            lights.light = null; // the pack's lamp prefab would come along with the copy
            var noise = system.noise;
            noise.enabled = false;

            var emission = system.emission;
            // Emits until the end of its duration, or only at its bursts (a 5 s glow with one burst at 0 lasts its lifetime).
            var lastEmit = main.duration;
            if (emission.enabled && emission.rateOverTimeMultiplier <= 0f && emission.rateOverDistanceMultiplier <= 0f)
            {
                lastEmit = 0f;
                for (var i = 0; i < emission.burstCount; i++)
                {
                    var burst = emission.GetBurst(i);
                    lastEmit = Mathf.Max(lastEmit, burst.cycleCount > 0
                        ? burst.time + (burst.cycleCount - 1) * burst.repeatInterval
                        : main.duration);
                }
                lastEmit = Mathf.Min(lastEmit, main.duration);
            }

            if (emission.enabled)
            {
                emission.rateOverTimeMultiplier *= share;
                emission.rateOverDistanceMultiplier *= share;
                var bursts = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(bursts);
                for (var i = 0; i < bursts.Length; i++)
                {
                    var count = bursts[i].count;
                    count.constantMin = Mathf.Max(1f, Mathf.Round(count.constantMin * share));
                    count.constantMax = Mathf.Max(1f, Mathf.Round(count.constantMax * share));
                    count.constant = Mathf.Max(1f, Mathf.Round(count.constant * share));
                    bursts[i].count = count;
                }
                emission.SetBursts(bursts);
            }

            return main.startDelay.constantMax + lastEmit + main.startLifetime.constantMax;
        }

        private static void SetupRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        // ---- Materials ----

        private static Material[] Convert(Material[] sources, string folder, Dictionary<Material, Material> cache)
        {
            var result = new Material[sources.Length];
            for (var i = 0; i < sources.Length; i++)
                result[i] = Convert(sources[i], folder, cache);
            return result;
        }

        private static Material Convert(Material source, string folder, Dictionary<Material, Material> cache)
        {
            if (source == null) return null;
            if (cache.TryGetValue(source, out var done)) return done;
            // Packs also use materials of other packs and built-in ones: everything not on own shaders is converted.
            if (source.shader.name.StartsWith("Purge/"))
                return cache[source] = source; // already own (a rebuild of a copy)

            var path = $"{folder}/Materials/{source.name}.mat";
            var shaderName = source.shader.name;
            var target = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(TargetShader(shaderName));
            if (target == null)
            {
                target = new Material(shader) { name = source.name };
                AssetDatabase.CreateAsset(target, path);
            }

            target.shader = shader;
            target.renderQueue = -1;
            Fill(target, source, shaderName);
            EditorUtility.SetDirty(target);
            return cache[source] = target;
        }

        private static string TargetShader(string source)
        {
            if (source.StartsWith("PolygonArsenal/PolyRimLight")) return "Purge/ParticleRim";
            if (source == "Standard") return "Purge/Lit";
            return "Purge/Particle";
        }

        private enum Blend { Additive, Alpha, Premultiplied, Multiply, MultiplyDouble }

        private static void Fill(Material target, Material source, string shaderName)
        {
            switch (shaderName)
            {
                case "PolygonArsenal/PolyRimLightTransparent":
                case "PolygonArsenal/PolyRimLightSolid":
                    target.SetColor("_InnerColor", Get(source, "_InnerColor", Color.white));
                    target.SetColor("_RimColor", Get(source, "_RimColor", Color.white));
                    target.SetFloat("_RimWidth", Get(source, "_RimWidth", 3f));
                    target.SetFloat("_RimGlow", Get(source, "_RimGlow", 1f));
                    SetBlend(target, shaderName.EndsWith("Transparent") ? Blend.Additive : Blend.Alpha);
                    return;
                case "Standard":
                    target.SetColor("_Color", Get(source, "_Color", Color.white));
                    target.SetTexture("_MainTex", source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null);
                    return;
            }

            var texture = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
            target.SetTexture("_MainTex", texture);
            if (texture != null && source.HasProperty("_MainTex"))
            {
                target.SetTextureScale("_MainTex", source.GetTextureScale("_MainTex"));
                target.SetTextureOffset("_MainTex", source.GetTextureOffset("_MainTex"));
            }

            Color color;
            Blend blend;
            if (shaderName.StartsWith("Legacy Shaders/Particles/"))
            {
                // Legacy particle shaders double the tint (grey 0.5 = as is).
                color = Get(source, "_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f)) * 2f;
                blend = shaderName.Contains("Additive") ? Blend.Additive
                    : shaderName.Contains("Premultiply") ? Blend.Premultiplied
                    : shaderName.EndsWith("Multiply (Double)") ? Blend.MultiplyDouble
                    : shaderName.Contains("Multiply") ? Blend.Multiply
                    : Blend.Alpha;
                if (blend == Blend.Multiply || blend == Blend.MultiplyDouble) color = Color.white;
            }
            else if (shaderName == "PolygonArsenal/PolyIntense")
            {
                color = Get(source, "_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f)) * (2f * Get(source, "_Glow", 1f));
                blend = Blend.Additive;
            }
            else if (shaderName.StartsWith("Particles/Standard"))
            {
                color = Get(source, "_Color", Color.white);
                if (Get(source, "_EmissionEnabled", 0f) > 0.5f || source.IsKeywordEnabled("_EMISSION"))
                {
                    var emission = Get(source, "_EmissionColor", Color.black);
                    color = new Color(color.r + emission.r, color.g + emission.g, color.b + emission.b, color.a);
                }
                // Modes of the standard particle shader: 0 opaque, 1 cutout, 2 fade, 3 transparent, 4 additive,
                // 5 subtractive, 6 modulate.
                var mode = Mathf.RoundToInt(Get(source, "_Mode", 2f));
                blend = mode == 4 ? Blend.Additive : mode == 6 ? Blend.Multiply : Blend.Alpha;
            }
            else
            {
                // PolyLitSurface and anything unknown: vertex colour, alpha blended.
                color = Get(source, "_Color", Color.white);
                blend = Blend.Alpha;
                if (shaderName != "PolygonArsenal/PolyLitSurface" && shaderName != "Sprites/Default")
                    Debug.LogWarning($"[Purge] FX import: unknown shader '{shaderName}' of '{source.name}' — alpha blended.");
            }

            target.SetColor("_Color", color);
            SetBlend(target, blend);
        }

        private static void SetBlend(Material material, Blend blend)
        {
            BlendMode src, dst;
            switch (blend)
            {
                case Blend.Additive: src = BlendMode.SrcAlpha; dst = BlendMode.One; break;
                case Blend.Premultiplied: src = BlendMode.One; dst = BlendMode.OneMinusSrcAlpha; break;
                case Blend.Multiply: src = BlendMode.DstColor; dst = BlendMode.Zero; break;
                case Blend.MultiplyDouble: src = BlendMode.DstColor; dst = BlendMode.SrcColor; break;
                default: src = BlendMode.SrcAlpha; dst = BlendMode.OneMinusSrcAlpha; break;
            }

            material.SetFloat("_SrcBlend", (float)src);
            material.SetFloat("_DstBlend", (float)dst);
        }

        private static Color Get(Material material, string property, Color fallback) =>
            material.HasProperty(property) ? material.GetColor(property) : fallback;

        private static float Get(Material material, string property, float fallback) =>
            material.HasProperty(property) ? material.GetFloat(property) : fallback;

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var slash = folder.LastIndexOf('/');
            EnsureFolder(folder.Substring(0, slash));
            AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
        }
    }
}
