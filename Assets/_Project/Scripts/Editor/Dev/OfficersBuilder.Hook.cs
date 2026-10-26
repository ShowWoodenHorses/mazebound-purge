using System;
using System.IO;
using Purge.Core.Effects;
using Purge.Core.Horde;
using Purge.Core.Monsters;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// The Hook of Build Monsters (stage 8b; ТЗ: Крюк — элита, притягивание; лиана в Лесу, цепь в Замке и Снеге):
    /// Capture_&lt;Biome&gt; on the one-handed clips "Elite_Hook_*" (Ultimate Animation Collection, 1h Melee), a sickle in the
    /// right hand; keeps 5–7 cells away and throws its hook (<see cref="PullAbility"/>) — the head is the sickle (the
    /// Forest: the vine), the line is <see cref="MonsterViewDefinition.Tether"/>: a chain or a vine on Purge/Tether with
    /// a texture drawn here (no pack has one: links seen face-on and edge-on in turn, two braided green strands), tinted
    /// by the biome. Also the stone of the gaze (<see cref="GazeAbility"/>, the Gorgon of stage 8g).
    /// </summary>
    internal static partial class OfficersBuilder
    {
        public const string HookControllerPath = "Assets/_Project/Art/Monster/Hook.controller";
        private const string HookArtPath = "Assets/_Project/Art/Monster/Hook";

        /// <summary>The Hook: abilities [pull]; up close — its plain attacks.</summary>
        private static readonly ClipSet HookClips = new ClipSet
        {
            Idle = "Elite_Hook_Idle", Walk = "Elite_Hook_Walk", Run = "Elite_Hook_Run", Stun = Stun, Roar = "Elite_Shield_Taunt",
            Attacks = new[] { "Elite_Hook_Attack_1", "Elite_Hook_Attack_2", "Elite_Hook_Attack_3" },
            Hits = new[] { "Elite_Hook_Hit_Front", "Elite_Hook_Hit_Left", "Elite_Hook_Hit_Right" },
            Deaths = new[] { "Elite_Hook_Death_Front", "Elite_Hook_Death_Back", "Elite_Hook_Death_Left", "Elite_Hook_Death_Right" },
            Casts = new[] { "Elite_Hook_Throw" },
            Rise = "Elite_Shield_Rise",
        };

        public static bool IsHook(string prefabName) => prefabName.StartsWith("Capture_", StringComparison.Ordinal);

        /// <summary>Draft numbers of the Hook (card 8b: HP 220, 1.8 c/s, keeps 5–7 cells, pulls every 6 s, strikes up close).</summary>
        private static void SeedHook(MonsterDefinition definition, string prefabName)
        {
            var stats = definition.Stats;
            stats.MaxHp = 220f;
            stats.Speed = 1.8f;
            stats.Radius = 0.38f;
            stats.AttackReach = 1f;
            stats.Damage = 12;
            stats.AttackInterval = 1.3f;
            stats.TurnSpeed = 300f;
            stats.HoldRange = 7f;
            stats.RetreatRange = 5f;
            var forest = prefabName.EndsWith("_Forest", StringComparison.Ordinal);
            var pull = new PullAbility
            {
                Cooldown = 6f, FirstDelay = 1f, MinRange = 2.5f, MaxRange = 7.5f, NeedsSight = true, Warning = 0.6f, Recovery = 0.3f,
                Speed = 16f, Range = 8f, Damage = 6f, PullTime = 0.3f, ReleaseSound = forest ? "hook_vine" : "hook_chain",
            };
            definition.SetAbilities(pull);
            EditorUtility.SetDirty(definition);
        }

        /// <summary>The line of a Hook's look by its biome (kept when already set: edited in the asset).</summary>
        private static void SetupHookLook(MonsterViewDefinition view, string prefabName)
        {
            if (view.Tether != null) return;
            if (prefabName.EndsWith("_Forest", StringComparison.Ordinal))
                view.Tether = EnsureTether("Hook_Vine_Forest", "Vine", new Color(1f, 1f, 1f, 1f));
            else if (prefabName.EndsWith("_Snow", StringComparison.Ordinal))
                view.Tether = EnsureTether("Hook_Chain_Snow", "Chain", new Color(0.85f, 0.95f, 1.1f, 1f));
            else
                view.Tether = EnsureTether("Hook_Chain_Castle", "Chain", new Color(1f, 0.82f, 0.68f, 1f));
            view.TetherWidth = 0.14f;
            EditorUtility.SetDirty(view);
        }

        /// <summary>Stone (stage 8b): the gaze's state — a stun of stone grey (the player's control immunity follows it).</summary>
        private static StatusDefinition EnsureStone() =>
            EnsureStatus("Status_Stone", new Color(0.62f, 0.6f, 0.56f, 1f), new StunStatus());

        private static Material EnsureTether(string name, string texture, Color tint)
        {
            EnsureFolder(HookArtPath);
            var path = $"{HookArtPath}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Purge/Tether");
            if (shader == null) throw new InvalidOperationException("Shader 'Purge/Tether' is missing.");
            material = new Material(shader) { name = name, mainTexture = EnsureTetherTexture(texture) };
            material.SetColor("_Color", tint);
            material.SetFloat("_Cutoff", 0.5f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>A tiling texture of the line (along u): "Chain" or "Vine", 128 × 32, drawn once.</summary>
        private static Texture2D EnsureTetherTexture(string kind)
        {
            var path = $"{HookArtPath}/hook_{kind.ToLowerInvariant()}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int width = 128, height = 32;
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                pixels[y * width + x] = kind == "Vine" ? VinePixel(x + 0.5f, y + 0.5f) : ChainPixel(x + 0.5f, y + 0.5f);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.alphaIsTransparency = true;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Two links a period: one face-on (an oval ring), one edge-on (a bar) laid over the ends of its neighbours — the
        /// texture repeats along the line. Iron grey, lighter on top.
        /// </summary>
        private static Color32 ChainPixel(float x, float y)
        {
            const float cy = 16f;
            // The edge-on link: a bar of rounded ends across the joint of the face-on ones (centres 96 and, wrapped, -32).
            var bar = Capsule(x, y, 64f, 128f, cy, 3.6f);
            if (bar > 0f) return Metal(bar, y, cy, 3.6f, 0.95f);
            // The face-on link: an oval ring centred at 32 (and its copy 128 further for the wrap).
            var ring = Mathf.Max(Ring(x, y, 32f, cy), Ring(x - 128f, y, 32f, cy));
            if (ring > 0f) return Metal(ring, y, cy, 12f, 1f);
            return new Color32(0, 0, 0, 0);
        }

        /// <summary>Inside the oval ring: 0..1 across its thickness (0 = outside).</summary>
        private static float Ring(float x, float y, float cx, float cy)
        {
            var dx = x - cx;
            var dy = y - cy;
            var outer = dx * dx / (36f * 36f) + dy * dy / (12.5f * 12.5f);
            var inner = dx * dx / (26f * 26f) + dy * dy / (5.5f * 5.5f);
            if (outer > 1f || inner < 1f) return 0f;
            return Mathf.Clamp01(Mathf.Min(1f - outer, inner - 1f) * 6f);
        }

        /// <summary>Inside the bar from <paramref name="from"/> to <paramref name="to"/> of half thickness <paramref name="half"/>.</summary>
        private static float Capsule(float x, float y, float from, float to, float cy, float half)
        {
            var px = Mathf.Clamp(x, from + half, to - half);
            var d = new Vector2(x - px, y - cy).magnitude;
            return d <= half ? Mathf.Clamp01((half - d) / half * 2f) : 0f;
        }

        private static Color32 Metal(float edge, float y, float cy, float half, float brightness)
        {
            // Lit from above; darker at the rims.
            var light = Mathf.Lerp(0.42f, 0.85f, Mathf.Clamp01(0.5f + (y - cy) / (2f * half))) * Mathf.Lerp(0.55f, 1f, edge) * brightness;
            var grey = (byte)(Mathf.Clamp01(light) * 255f);
            return new Color32(grey, grey, (byte)Mathf.Min(255, grey + 6), 255);
        }

        /// <summary>Two green strands braided around the middle (period 64 px), a leaf on each half; darker at the rims.</summary>
        private static Color32 VinePixel(float x, float y)
        {
            const float cy = 16f;
            var phase = x / 64f * Mathf.PI * 2f;
            var a = cy + 6f * Mathf.Sin(phase);
            var b = cy - 6f * Mathf.Sin(phase);
            var front = Mathf.Cos(phase) >= 0f; // which strand passes over
            var da = Mathf.Abs(y - a);
            var db = Mathf.Abs(y - b);
            const float half = 3.4f;
            float best;
            float shade;
            if (da <= half && (front || db > half))
            {
                best = da;
                shade = 0.95f;
            }
            else if (db <= half)
            {
                best = db;
                shade = 0.8f;
            }
            else
            {
                // A leaf: a small oval sticking out at 20 and 84.
                var leaf = Mathf.Max(Leaf(x, y, 20f, cy + 9f), Leaf(x, y, 84f, cy - 9f));
                if (leaf <= 0f) return new Color32(0, 0, 0, 0);
                var g = Mathf.Lerp(0.45f, 0.8f, leaf);
                return new Color32((byte)(g * 0.45f * 255f), (byte)(g * 255f), (byte)(g * 0.25f * 255f), 255);
            }

            var edge = 1f - best / half;
            var light = Mathf.Lerp(0.45f, 1f, edge) * shade;
            return new Color32((byte)(light * 0.36f * 255f), (byte)(light * 0.6f * 255f), (byte)(light * 0.2f * 255f), 255);
        }

        private static float Leaf(float x, float y, float cx, float cy)
        {
            var dx = (x - cx) / 7f;
            var dy = (y - cy) / 3.2f;
            var d = dx * dx + dy * dy;
            return d <= 1f ? 1f - d : 0f;
        }
    }
}
