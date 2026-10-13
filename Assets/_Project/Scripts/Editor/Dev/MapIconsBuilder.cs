using System;
using System.IO;
using Maze.Presentation.Map;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Map Icons: draws the map icons (start flag, door plank, padlock, exit arrow, map scroll,
    /// player arrow, skull, sword, medkit, key) "by hand" — signed-distance shapes with a slightly wobbly dark outline, white inside (tinted by
    /// <see cref="MapIconSet"/> colours) — as PNG sprites and fills <see cref="MapIconSet"/>. PNGs are overwritten
    /// (GUIDs kept); the tuned colours and sizes of an existing set stay. Any sprite may be replaced by hand afterwards.
    /// </summary>
    internal static class MapIconsBuilder
    {
        public const string Folder = "Assets/_Project/Art/UI/MapIcons";
        public const string SetPath = Folder + "/MapIcons.asset";

        private const int Size = 128;
        private const float Outline = 0.09f;
        private const float Wobble = 0.022f;
        private static readonly Color Ink = new Color(0.1f, 0.09f, 0.08f, 1f);

        [MenuItem("Maze/Dev/Build Map Icons")]
        public static void BuildMenu()
        {
            var set = Build();
            EditorGUIUtility.PingObject(set);
        }

        public static MapIconSet Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
            }

            var set = AssetDatabase.LoadAssetAtPath<MapIconSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<MapIconSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }

            set.Start = Draw("map_start", 1, Flag, null);
            set.Door = Draw("map_door", 2, DoorPlank, DoorDetails);
            set.LockedDoor = Draw("map_locked_door", 3, Padlock, Keyhole);
            set.Exit = Draw("map_exit", 4, ExitArrow, null);
            set.MapFragment = Draw("map_fragment", 5, Scroll, ScrollDetails);
            set.Player = Draw("map_player", 6, PlayerArrow, null);
            set.Zombie = Draw("map_zombie", 7, Skull, SkullDetails);
            set.Weapon = Draw("map_weapon", 8, Sword, null);
            set.Medkit = Draw("map_medkit", 9, Medkit, MedkitCross);
            set.Key = Draw("map_key", 10, Key, null);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] Map icons built: {SetPath}.");
            return set;
        }

        // ---- shapes: p in [-1, 1]², y up; negative inside ----

        private static float Flag(Vector2 p) => Mathf.Min(
            Capsule(p, new Vector2(-0.5f, -0.78f), new Vector2(-0.5f, 0.8f), 0.08f),
            Mathf.Min(
                Polygon(p, new Vector2(-0.5f, 0.78f), new Vector2(0.15f, 0.62f), new Vector2(0.72f, 0.5f),
                    new Vector2(0.2f, 0.28f), new Vector2(-0.5f, 0.12f)),
                Ellipse(p, new Vector2(-0.5f, -0.8f), new Vector2(0.38f, 0.12f))));

        private static float DoorPlank(Vector2 p) => RoundBox(p, Vector2.zero, new Vector2(0.84f, 0.26f), 0.08f);

        private static float DoorDetails(Vector2 p) => Mathf.Min(
            Circle(p, new Vector2(0.52f, 0f), 0.08f),
            Mathf.Min(Capsule(p, new Vector2(-0.3f, -0.15f), new Vector2(-0.3f, 0.15f), 0.025f),
                Capsule(p, new Vector2(0.15f, -0.15f), new Vector2(0.15f, 0.15f), 0.025f)));

        private static float Padlock(Vector2 p)
        {
            var body = RoundBox(p, new Vector2(0f, -0.28f), new Vector2(0.6f, 0.46f), 0.12f);
            var center = new Vector2(0f, 0.2f);
            var ring = Mathf.Abs((p - center).magnitude - 0.38f) - 0.11f;
            var shackle = Mathf.Max(ring, center.y - p.y); // Upper half only.
            return Mathf.Min(body, shackle);
        }

        private static float Keyhole(Vector2 p) => Mathf.Min(
            Circle(p, new Vector2(0f, -0.16f), 0.12f),
            Polygon(p, new Vector2(-0.07f, -0.2f), new Vector2(0.07f, -0.2f), new Vector2(0.12f, -0.52f), new Vector2(-0.12f, -0.52f)));

        private static float ExitArrow(Vector2 p) => Mathf.Min(
            Mathf.Min(RoundBox(p, new Vector2(0f, -0.12f), new Vector2(0.17f, 0.42f), 0.04f),
                Polygon(p, new Vector2(-0.6f, 0.22f), new Vector2(0.6f, 0.22f), new Vector2(0f, 0.88f))),
            RoundBox(p, new Vector2(0f, -0.74f), new Vector2(0.72f, 0.1f), 0.06f));

        private static float Scroll(Vector2 p) => Mathf.Min(
            RoundBox(p, Vector2.zero, new Vector2(0.56f, 0.66f), 0.05f),
            Mathf.Min(Capsule(p, new Vector2(-0.68f, 0.66f), new Vector2(0.68f, 0.66f), 0.14f),
                Capsule(p, new Vector2(-0.68f, -0.66f), new Vector2(0.68f, -0.66f), 0.14f)));

        private static float ScrollDetails(Vector2 p)
        {
            var lines = Mathf.Min(Capsule(p, new Vector2(-0.36f, 0.32f), new Vector2(0.36f, 0.3f), 0.03f),
                Capsule(p, new Vector2(-0.36f, 0.1f), new Vector2(0.2f, 0.12f), 0.03f));
            var cross = Mathf.Min(Capsule(p, new Vector2(0.06f, -0.48f), new Vector2(0.36f, -0.18f), 0.045f),
                Capsule(p, new Vector2(0.06f, -0.18f), new Vector2(0.36f, -0.48f), 0.045f));
            var trail = Capsule(p, new Vector2(-0.38f, -0.4f), new Vector2(-0.1f, -0.3f), 0.025f);
            return Mathf.Min(lines, Mathf.Min(cross, trail));
        }

        private static float PlayerArrow(Vector2 p) => Polygon(p,
            new Vector2(0f, 0.9f), new Vector2(0.66f, -0.72f), new Vector2(0f, -0.36f), new Vector2(-0.66f, -0.72f));

        private static float Skull(Vector2 p) => Mathf.Min(
            Circle(p, new Vector2(0f, 0.16f), 0.62f),
            RoundBox(p, new Vector2(0f, -0.46f), new Vector2(0.36f, 0.26f), 0.1f));

        private static float SkullDetails(Vector2 p)
        {
            var eyes = Mathf.Min(Circle(p, new Vector2(-0.25f, 0.08f), 0.16f), Circle(p, new Vector2(0.25f, 0.08f), 0.16f));
            var nose = Polygon(p, new Vector2(0f, -0.06f), new Vector2(0.08f, -0.24f), new Vector2(-0.08f, -0.24f));
            var teeth = Mathf.Min(Capsule(p, new Vector2(-0.14f, -0.44f), new Vector2(-0.14f, -0.64f), 0.025f),
                Mathf.Min(Capsule(p, new Vector2(0f, -0.44f), new Vector2(0f, -0.66f), 0.025f),
                    Capsule(p, new Vector2(0.14f, -0.44f), new Vector2(0.14f, -0.64f), 0.025f)));
            return Mathf.Min(eyes, Mathf.Min(nose, teeth));
        }

        private static float Sword(Vector2 p) => Mathf.Min(
            Mathf.Min(Polygon(p, new Vector2(-0.26f, -0.02f), new Vector2(-0.02f, -0.26f), new Vector2(0.6f, 0.4f),
                    new Vector2(0.76f, 0.76f), new Vector2(0.4f, 0.6f)),
                Capsule(p, new Vector2(-0.44f, 0.08f), new Vector2(0.08f, -0.44f), 0.08f)),
            Mathf.Min(Capsule(p, new Vector2(-0.18f, -0.18f), new Vector2(-0.52f, -0.52f), 0.075f),
                Circle(p, new Vector2(-0.6f, -0.6f), 0.12f)));

        private static float Medkit(Vector2 p)
        {
            var box = RoundBox(p, new Vector2(0f, -0.12f), new Vector2(0.74f, 0.54f), 0.12f);
            var center = new Vector2(0f, 0.42f);
            var handle = Mathf.Max(Mathf.Abs(RoundBox(p, center, new Vector2(0.3f, 0.2f), 0.1f)) - 0.07f, center.y - p.y);
            return Mathf.Min(box, handle);
        }

        private static float MedkitCross(Vector2 p) => Mathf.Min(
            RoundBox(p, new Vector2(0f, -0.12f), new Vector2(0.12f, 0.34f), 0.03f),
            RoundBox(p, new Vector2(0f, -0.12f), new Vector2(0.34f, 0.12f), 0.03f));

        private static float Key(Vector2 p)
        {
            var bow = Mathf.Abs(Circle(p, new Vector2(-0.46f, 0.1f), 0.26f)) - 0.12f;
            var shaft = Capsule(p, new Vector2(-0.2f, 0.1f), new Vector2(0.78f, 0.1f), 0.09f);
            var teeth = Mathf.Min(RoundBox(p, new Vector2(0.48f, -0.1f), new Vector2(0.07f, 0.16f), 0.03f),
                RoundBox(p, new Vector2(0.7f, -0.14f), new Vector2(0.07f, 0.2f), 0.03f));
            return Mathf.Min(bow, Mathf.Min(shaft, teeth));
        }

        // ---- drawing ----

        private static Sprite Draw(string name, int seed, Func<Vector2, float> shape, Func<Vector2, float> details)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color[Size * Size];
            var pixel = 2f / Size;
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f) * 1.08f;
                var noise = Noise(p, seed);
                var d = shape(p) + noise * Wobble;
                var outer = d - Outline * 0.5f;
                var alpha = Smooth(pixel, -pixel, outer);
                if (alpha <= 0f)
                {
                    pixels[y * Size + x] = new Color(1f, 1f, 1f, 0f);
                    continue;
                }

                var ink = Smooth(-pixel, pixel, d + Outline * 0.5f);
                if (details != null)
                    ink = Mathf.Max(ink, Smooth(pixel, -pixel, details(p) + noise * Wobble * 0.5f));
                var paper = 1f - 0.06f * (Noise(p * 3.1f, seed + 17) * 0.5f + 0.5f); // A little uneven, like pencil.
                var color = Color.Lerp(new Color(paper, paper, paper, 1f), Ink, ink);
                color.a = alpha;
                pixels[y * Size + x] = color;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            var path = $"{Folder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true; // Icons are drawn small on big levels.
            importer.maxTextureSize = Size;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>Smooth wobble of the outline, about -1..1.</summary>
        private static float Noise(Vector2 p, int seed) =>
            Mathf.Sin(p.x * 9.1f + seed * 1.7f) * Mathf.Cos(p.y * 7.3f + seed * 0.9f) * 0.6f +
            Mathf.Sin((p.x + p.y) * 15.7f + seed * 2.3f) * 0.4f;

        private static float Smooth(float from, float to, float value)
        {
            var t = Mathf.Clamp01((value - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        private static float Circle(Vector2 p, Vector2 center, float radius) => (p - center).magnitude - radius;

        private static float Ellipse(Vector2 p, Vector2 center, Vector2 radii)
        {
            var q = new Vector2((p.x - center.x) / radii.x, (p.y - center.y) / radii.y);
            return (q.magnitude - 1f) * Mathf.Min(radii.x, radii.y);
        }

        private static float RoundBox(Vector2 p, Vector2 center, Vector2 half, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - half + new Vector2(radius, radius);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            var pa = p - a;
            var ba = b - a;
            var h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - radius;
        }

        /// <summary>Signed distance to a simple polygon (any winding).</summary>
        private static float Polygon(Vector2 p, params Vector2[] v)
        {
            var d = Vector2.Dot(p - v[0], p - v[0]);
            var sign = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                var e = v[j] - v[i];
                var w = p - v[i];
                var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                var c0 = p.y >= v[i].y;
                var c1 = p.y < v[j].y;
                var c2 = e.x * w.y > e.y * w.x;
                if ((c0 && c1 && c2) || (!c0 && !c1 && !c2)) sign = -sign;
            }

            return sign * Mathf.Sqrt(d);
        }
    }
}
