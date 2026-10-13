using System;
using System.Collections.Generic;
using System.IO;
using Maze.Core.Visual;
using Maze.Presentation.UI.Style;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build UI Icons: draws the line icons of the UI (the drawings of the design mock-up: a 24-unit grid,
    /// one line width, white, anti-aliased by distance) as PNG sprites and fills <see cref="UiIconSet"/>; renders every
    /// held weapon model of <see cref="WeaponVisualCatalog"/> into a white silhouette (guns barrel to the right, melee
    /// blade up-right) and stores it in the catalog. PNGs are overwritten (GUIDs kept). Weapon icons are rebuilt by
    /// Build Weapons too.
    /// </summary>
    internal static class UiIconsBuilder
    {
        public const string Folder = "Assets/_Project/Art/UI/Icons";
        public const string WeaponFolder = Folder + "/Weapons";
        public const string SetPath = "Assets/_Project/Data/UI/UiIcons.asset";
        private const string CatalogPath = "Assets/_Project/Data/Weapons/WeaponVisuals.asset";

        private const int Size = 96;
        private const float Grid = 24f;
        private const float Line = 1.6f;
        private const int WeaponSize = 128;
        private const int WeaponSupersample = 4;

        [MenuItem("Maze/Dev/Build UI Icons")]
        public static void BuildMenu()
        {
            var set = Build();
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponVisualCatalog>(CatalogPath);
            if (catalog != null) BuildWeaponIcons(catalog);
            EditorGUIUtility.PingObject(set);
        }

        public static UiIconSet Build()
        {
            EnsureFolder(Folder);
            EnsureFolder(Path.GetDirectoryName(SetPath));

            var set = AssetDatabase.LoadAssetAtPath<UiIconSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<UiIconSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }

            set.Map = Draw("ui_map", MapIcon());
            set.Pause = Draw("ui_pause", new Icon().Segment(9, 6, 9, 18, 2f).Segment(15, 6, 15, 18, 2f));
            set.Settings = Draw("ui_settings", SettingsIcon());
            set.Door = Draw("ui_door", new Icon().Polyline(false, 6, 21, 6, 3.5f, 18, 3.5f, 18, 21).Segment(3.5f, 21, 20.5f, 21).Dot(15, 12.5f, 1.3f));
            set.PickUp = Draw("ui_pickup", new Icon().Segment(12, 3.5f, 12, 14).Polyline(false, 8, 10, 12, 14, 16, 10)
                .Polyline(false, 4, 15, 4, 20, 20, 20, 20, 15));
            set.Key = Draw("ui_key", new Icon().Circle(7.5f, 12, 4).Segment(11.5f, 12, 20, 12).Segment(17, 12, 17, 15).Segment(20, 12, 20, 14));
            set.Medkit = Draw("ui_medkit", new Icon().Polyline(true, 3.5f, 7, 20.5f, 7, 20.5f, 19.5f, 3.5f, 19.5f)
                .Polyline(false, 9, 7, 9, 4.5f, 15, 4.5f, 15, 7).Segment(12, 10.5f, 12, 16).Segment(9.25f, 13.25f, 14.75f, 13.25f));
            set.Skull = Draw("ui_skull", SkullIcon());
            set.Fragment = Draw("ui_fragment", new Icon().Polyline(true, 4, 5, 13, 5, 11.5f, 9.5f, 15, 12, 13, 19, 4, 19)
                .Polyline(false, 13, 5, 20, 5, 20, 19, 13, 19));
            set.Melee = Draw("ui_melee", AxeIcon());
            set.Ranged = Draw("ui_ranged", new Icon().Polyline(true, 3, 8, 20.5f, 8, 20.5f, 11.2f, 12.6f, 11.2f, 10.8f, 16.5f, 7.4f, 16.5f, 8.6f, 11.2f, 3, 11.2f)
                .Polyline(false, 14, 11.2f, 13.6f, 13.6f, 11.6f, 13.6f));
            set.Attack = Draw("ui_attack", new Icon().Circle(12, 12, 5).Segment(12, 2.5f, 12, 6).Segment(12, 18, 12, 21.5f)
                .Segment(2.5f, 12, 6, 12).Segment(18, 12, 21.5f, 12));
            set.Star = Draw("ui_star", new Icon().Polyline(true, StarPoints));
            set.StarFilled = Draw("ui_star_filled", new Icon().Polyline(true, StarPoints).Fill(StarPoints));
            set.Lock = Draw("ui_lock", new Icon().Polyline(true, 5, 10.5f, 19, 10.5f, 19, 20.5f, 5, 20.5f)
                .Polyline(false, Join(new[] { 8.5f, 10.5f }, Arc(12, 7.5f, 3.5f, 180, 0), new[] { 15.5f, 10.5f })));
            var heart = HeartPoints();
            set.Heart = Draw("ui_heart", new Icon().Polyline(true, heart));
            set.HeartFill = Draw("ui_heart_fill", new Icon().Fill(heart));
            set.ChevronLeft = Draw("ui_chevron_left", new Icon().Polyline(false, 14.5f, 5.5f, 8, 12, 14.5f, 18.5f, 2f));
            set.ChevronRight = Draw("ui_chevron_right", new Icon().Polyline(false, 9.5f, 5.5f, 16, 12, 9.5f, 18.5f, 2f));
            set.Resume = Draw("ui_resume", new Icon().Polyline(true, 8, 5, 19, 12, 8, 19));
            set.Retry = Draw("ui_retry", RetryIcon());
            set.Menu = Draw("ui_menu", new Icon().Polyline(true, 4, 4, 10.5f, 4, 10.5f, 10.5f, 4, 10.5f)
                .Polyline(true, 13.5f, 4, 20, 4, 20, 10.5f, 13.5f, 10.5f).Polyline(true, 4, 13.5f, 10.5f, 13.5f, 10.5f, 20, 4, 20)
                .Polyline(true, 13.5f, 13.5f, 20, 13.5f, 20, 20, 13.5f, 20));
            set.Exit = Draw("ui_exit", new Icon().Polyline(false, 13, 4, 5, 4, 5, 20, 13, 20).Segment(10, 12, 20, 12)
                .Polyline(false, 16.5f, 8.5f, 20, 12, 16.5f, 15.5f));
            set.Close = Draw("ui_close", new Icon().Segment(6, 6, 18, 18).Segment(18, 6, 6, 18));
            set.Player = Draw("ui_player", new Icon().Polyline(true, 12, 3.5f, 19, 20, 12, 16, 5, 20));
            set.Search = Draw("ui_search", new Icon().Circle(10.5f, 10.5f, 6f).Segment(15f, 15f, 20f, 20f));
            // Question mark in a circle (the tutorial).
            set.Help = Draw("ui_help", new Icon().Circle(12, 12, 9f)
                .Polyline(false, 9f, 9.5f, 9.6f, 7.6f, 11f, 6.8f, 13f, 6.8f, 14.6f, 7.8f, 15f, 9.4f, 14.2f, 11f, 12.6f, 12f, 12f, 13.2f, 12f, 14.6f)
                .Dot(12f, 17.4f, 1.2f));
            set.Ad = Draw("ui_ad", new Icon().Polyline(true, 3.5f, 5.5f, 20.5f, 5.5f, 20.5f, 18.5f, 3.5f, 18.5f)
                .Polyline(true, 10f, 9f, 15.5f, 12f, 10f, 15f).Fill(new[] { 10f, 9f, 15.5f, 12f, 10f, 15f }));
            set.Backdrop = DrawBackdrop();

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] UI icons built: {SetPath}.");
            return set;
        }

        // ---- icons (24-unit grid, y down like the mock-up's SVG) ----

        private static readonly float[] StarPoints =
            { 12, 3.5f, 14.6f, 8.9f, 20.5f, 9.7f, 16.2f, 13.8f, 17.2f, 19.6f, 12, 16.8f, 6.8f, 19.6f, 7.8f, 13.8f, 3.5f, 9.7f, 9.4f, 8.9f };

        private static Icon MapIcon() => new Icon()
            .Polyline(true, 3.5f, 6.5f, 9, 4.5f, 15, 6.5f, 20.5f, 4.5f, 20.5f, 17.5f, 15, 19.5f, 9, 17.5f, 3.5f, 19.5f)
            .Segment(9, 4.5f, 9, 17.5f).Segment(15, 6.5f, 15, 19.5f);

        private static Icon SettingsIcon()
        {
            var icon = new Icon().Circle(12, 12, 3.2f).Circle(12, 12, 7);
            for (var i = 0; i < 8; i++)
            {
                var a = i * 45f * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var r0 = i % 2 == 0 ? 7f : 7f;
                icon.Segment(12 + d.x * r0, 12 + d.y * r0, 12 + d.x * 9.5f, 12 + d.y * 9.5f);
            }

            return icon;
        }

        private static Icon SkullIcon() => new Icon()
            .Polyline(true, Join(Arc(12, 11, 7, 180, 0), new[] { 19f, 14.5f, 16.5f, 14.5f, 16.5f, 18, 7.5f, 18, 7.5f, 14.5f, 5, 14.5f }))
            .Dot(9.3f, 11.3f, 1.6f).Dot(14.7f, 11.3f, 1.6f);

        private static Icon AxeIcon()
        {
            var head = Join(new[] { 12.5f, 6.2f }, Quad(12.5f, 6.2f, 15.5f, 2.5f, 20.5f, 4), Quad(20.5f, 4, 19.6f, 9.2f, 15.4f, 11.2f));
            return new Icon().Segment(5, 20, 15, 7.5f).Polyline(true, head);
        }

        private static Icon RetryIcon()
        {
            var arc = Arc(12, 12, 7, 60, 330);
            var start = new Vector2(arc[0], arc[1]);
            // Arrow head at the start, pointing clockwise (on screen) along the circle.
            var a = 60f * Mathf.Deg2Rad;
            var along = new Vector2(Mathf.Sin(a), Mathf.Cos(a)); // y down
            var side = new Vector2(-along.y, along.x);
            var tip = start + along * 2.4f;
            var b1 = start + side * 2.4f;
            var b2 = start - side * 2.4f;
            return new Icon().Polyline(false, arc).Fill(tip.x, tip.y, b1.x, b1.y, b2.x, b2.y);
        }

        private static float[] HeartPoints()
        {
            const int count = 48;
            var points = new float[count * 2];
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)count * Mathf.PI * 2f;
                var sin = Mathf.Sin(t);
                var x = 16f * sin * sin * sin;
                var y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                points[i * 2] = 12f + x * 0.5f;
                points[i * 2 + 1] = 11.3f - y * 0.5f;
            }

            return points;
        }

        /// <summary>Points of an arc (y down; angles counter-clockwise on screen, degrees).</summary>
        private static float[] Arc(float cx, float cy, float r, float from, float to)
        {
            var steps = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(to - from) / 6f));
            var points = new float[(steps + 1) * 2];
            for (var i = 0; i <= steps; i++)
            {
                var a = Mathf.Lerp(from, to, i / (float)steps) * Mathf.Deg2Rad;
                points[i * 2] = cx + Mathf.Cos(a) * r;
                points[i * 2 + 1] = cy - Mathf.Sin(a) * r;
            }

            return points;
        }

        /// <summary>Quadratic curve points after the start (the start itself is the previous point).</summary>
        private static float[] Quad(float x0, float y0, float cx, float cy, float x1, float y1)
        {
            const int steps = 10;
            var points = new float[steps * 2];
            for (var i = 1; i <= steps; i++)
            {
                var t = i / (float)steps;
                var u = 1f - t;
                points[(i - 1) * 2] = u * u * x0 + 2f * u * t * cx + t * t * x1;
                points[(i - 1) * 2 + 1] = u * u * y0 + 2f * u * t * cy + t * t * y1;
            }

            return points;
        }

        private static float[] Join(params float[][] parts)
        {
            var list = new List<float>();
            foreach (var part in parts) list.AddRange(part);
            return list.ToArray();
        }

        /// <summary>Strokes (segments with a width) and filled polygons in grid units.</summary>
        private sealed class Icon
        {
            public readonly List<(Vector2 A, Vector2 B, float Width)> Segments = new List<(Vector2, Vector2, float)>();
            public readonly List<Vector2[]> Fills = new List<Vector2[]>();

            public Icon Segment(float x0, float y0, float x1, float y1, float width = Line)
            {
                Segments.Add((new Vector2(x0, y0), new Vector2(x1, y1), width));
                return this;
            }

            /// <summary>Points as x, y pairs; an odd count ends with the line width.</summary>
            public Icon Polyline(bool closed, params float[] points)
            {
                var width = points.Length % 2 == 1 ? points[points.Length - 1] : Line;
                var count = points.Length / 2;
                for (var i = 0; i + 1 < count; i++)
                    Segment(points[i * 2], points[i * 2 + 1], points[i * 2 + 2], points[i * 2 + 3], width);
                if (closed && count > 2)
                    Segment(points[(count - 1) * 2], points[(count - 1) * 2 + 1], points[0], points[1], width);
                return this;
            }

            public Icon Circle(float cx, float cy, float r) => Polyline(true, Arc(cx, cy, r, 0, 360));

            public Icon Dot(float cx, float cy, float r) => Fill(Arc(cx, cy, r, 0, 360));

            public Icon Fill(params float[] points)
            {
                var polygon = new Vector2[points.Length / 2];
                for (var i = 0; i < polygon.Length; i++)
                    polygon[i] = new Vector2(points[i * 2], points[i * 2 + 1]);
                Fills.Add(polygon);
                return this;
            }

            /// <summary>Coverage 0..1 at a point (grid units), <paramref name="pixel"/> = grid units per pixel.</summary>
            public float Coverage(Vector2 p, float pixel)
            {
                var distance = float.MaxValue;
                foreach (var (a, b, width) in Segments)
                    distance = Mathf.Min(distance, SegmentDistance(p, a, b) - width * 0.5f);
                foreach (var polygon in Fills)
                    distance = Mathf.Min(distance, PolygonDistance(p, polygon));
                return Mathf.Clamp01(0.5f - distance / pixel);
            }
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Signed distance to a polygon (negative inside; even-odd rule).</summary>
        private static float PolygonDistance(Vector2 p, Vector2[] polygon)
        {
            var distance = float.MaxValue;
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                distance = Mathf.Min(distance, SegmentDistance(p, polygon[j], polygon[i]));
                var a = polygon[i];
                var b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }

            return inside ? -distance : distance;
        }

        private static Sprite Draw(string name, Icon icon)
        {
            var pixels = new Color32[Size * Size];
            var pixel = Grid / Size;
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                // Texture rows go up, the grid goes down.
                var p = new Vector2((x + 0.5f) * pixel, Grid - (y + 0.5f) * pixel);
                var alpha = icon.Coverage(p, pixel);
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }

            return SavePng($"{Folder}/{name}.png", pixels, Size);
        }

        // ---- menu backdrop ----

        private const int BackdropWidth = 960;
        private const int BackdropHeight = 540;
        private const int BackdropCell = 30;

        /// <summary>
        /// A faint top-down maze (randomized DFS, fixed seed) on the dark ground with a fine grid, a warm torch glow on
        /// the right and a vignette — the menus' background, drawn once.
        /// </summary>
        private static Texture2D DrawBackdrop()
        {
            // Odd cell counts: walls on the borders, passages between (as the level generator).
            var columns = BackdropWidth / BackdropCell | 1;
            var rows = BackdropHeight / BackdropCell | 1;
            var wall = new bool[columns, rows];
            for (var x = 0; x < columns; x++)
            for (var y = 0; y < rows; y++)
                wall[x, y] = true;

            var random = new Core.Common.DeterministicRandom(2026);
            var stack = new Stack<Vector2Int>();
            stack.Push(new Vector2Int(1, 1));
            wall[1, 1] = false;
            var steps = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            while (stack.Count > 0)
            {
                var cell = stack.Peek();
                var options = new List<Vector2Int>();
                foreach (var step in steps)
                {
                    var next = cell + step * 2;
                    if (next.x > 0 && next.y > 0 && next.x < columns - 1 && next.y < rows - 1 && wall[next.x, next.y])
                        options.Add(step);
                }

                if (options.Count == 0)
                {
                    stack.Pop();
                    continue;
                }

                var chosen = options[random.NextInt(options.Count)];
                wall[cell.x + chosen.x, cell.y + chosen.y] = false;
                wall[cell.x + chosen.x * 2, cell.y + chosen.y * 2] = false;
                stack.Push(cell + chosen * 2);
            }

            // A few loops so it reads as a maze, not a tree.
            for (var i = 0; i < columns * rows / 14; i++)
            {
                var x = 1 + random.NextInt(columns - 2);
                var y = 1 + random.NextInt(rows - 2);
                if ((x + y) % 2 == 1) wall[x, y] = false;
            }

            var ground = new Color(0.098f, 0.09f, 0.078f);
            var wallTop = new Color(0.132f, 0.122f, 0.106f);
            var wallEdge = new Color(0.155f, 0.144f, 0.125f);
            var torch = new Color(0.914f, 0.66f, 0.29f);
            var glowCenter = new Vector2(BackdropWidth * 0.7f, BackdropHeight * 0.62f);
            var offsetX = (BackdropWidth - columns * BackdropCell) / 2;
            var offsetY = (BackdropHeight - rows * BackdropCell) / 2;
            var pixels = new Color32[BackdropWidth * BackdropHeight];
            for (var y = 0; y < BackdropHeight; y++)
            for (var x = 0; x < BackdropWidth; x++)
            {
                var cx = Mathf.FloorToInt((x - offsetX) / (float)BackdropCell);
                var cy = Mathf.FloorToInt((y - offsetY) / (float)BackdropCell);
                var inside = cx >= 0 && cy >= 0 && cx < columns && cy < rows;
                var isWall = inside && wall[cx, cy];
                var lx = (x - offsetX) - cx * BackdropCell;
                var ly = (y - offsetY) - cy * BackdropCell;

                Color color;
                if (isWall)
                {
                    var edge = lx == 0 || ly == BackdropCell - 1;
                    color = edge ? wallEdge : wallTop;
                    // Shadow cast down-right onto the floor is drawn by the floor below.
                }
                else
                {
                    color = ground;
                    var shadow = inside && ((cx > 0 && wall[cx - 1, cy] && lx < 5) || (cy + 1 < rows && wall[cx, cy + 1] && ly > BackdropCell - 7));
                    if (shadow) color *= 0.72f;
                    if (lx == 0 || ly == 0) color += new Color(0.012f, 0.012f, 0.012f);
                }

                var glow = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), glowCenter) / 300f);
                color += torch * (glow * glow * 0.12f);

                var u = (x + 0.5f) / BackdropWidth * 2f - 1f;
                var v = (y + 0.5f) / BackdropHeight * 2f - 1f;
                var vignette = Mathf.Clamp01(1.05f - Mathf.Sqrt(u * u * 0.8f + v * v * 1.1f));
                color *= Mathf.Lerp(0.3f, 1f, vignette * vignette);
                color.a = 1f;
                pixels[y * BackdropWidth + x] = color;
            }

            var path = $"{Folder}/ui_backdrop.png";
            var texture = new Texture2D(BackdropWidth, BackdropHeight, TextureFormat.RGB24, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType != TextureImporterType.Default || importer.mipmapEnabled ||
                importer.maxTextureSize != 1024 || importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---- weapon silhouettes ----

        /// <summary>Renders each held weapon model into a white silhouette and stores it in the catalog.</summary>
        public static void BuildWeaponIcons(WeaponVisualCatalog catalog)
        {
            EnsureFolder(WeaponFolder);
            var replacement = Shader.Find("Unlit/Color");
            var scene = EditorSceneManager.NewPreviewScene();
            var renderSize = WeaponSize * WeaponSupersample;
            var target = RenderTexture.GetTemporary(renderSize, renderSize, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(renderSize, renderSize, TextureFormat.RGBA32, false);
            var cameraObject = new GameObject("IconCamera");
            try
            {
                SceneManager_Move(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.enabled = false;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.targetTexture = target;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 20f;

                var built = 0;
                foreach (var weapon in catalog.MutableWeapons)
                {
                    var path = weapon?.HeldPrefab != null ? AssetDatabase.GUIDToAssetPath(weapon.HeldPrefab.AssetGUID) : null;
                    var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null || weapon.Definition == null) continue;

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    try
                    {
                        // Barrel / blade along +Z, up +Y. Seen from +X: +Z goes right. Melee leans up-right.
                        var melee = weapon.Definition.Slot == Core.Definitions.WeaponSlot.Melee;
                        instance.transform.SetPositionAndRotation(Vector3.zero,
                            melee ? Quaternion.AngleAxis(-45f, Vector3.right) : Quaternion.identity);
                        if (!TryBounds(instance, out var bounds)) continue;

                        var extent = Mathf.Max(bounds.extents.z, bounds.extents.y) * 1.12f;
                        camera.orthographicSize = extent;
                        camera.transform.SetPositionAndRotation(bounds.center + Vector3.right * 10f,
                            Quaternion.LookRotation(Vector3.left, Vector3.up));
                        camera.RenderWithShader(replacement, string.Empty);

                        var previous = RenderTexture.active;
                        RenderTexture.active = target;
                        read.ReadPixels(new Rect(0, 0, renderSize, renderSize), 0, 0);
                        read.Apply(false);
                        RenderTexture.active = previous;

                        var sprite = SavePng($"{WeaponFolder}/weapon_{weapon.Definition.Id}.png", Downsample(read), WeaponSize);
                        weapon.Icon = sprite;
                        built++;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }

                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                var removed = DeleteUnusedWeaponIcons(catalog);
                Debug.Log($"[Maze] Weapon icons built: {built}" + (removed > 0 ? $", {removed} unused removed." : "."));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(read);
                RenderTexture.ReleaseTemporary(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// Icons in <see cref="WeaponFolder"/> no catalog weapon uses (a removed weapon, a changed id): the folder is
        /// this builder's own, so they go.
        /// </summary>
        private static int DeleteUnusedWeaponIcons(WeaponVisualCatalog catalog)
        {
            var used = new HashSet<string>();
            foreach (var weapon in catalog.MutableWeapons)
                if (weapon?.Icon != null)
                    used.Add(AssetDatabase.GetAssetPath(weapon.Icon));

            var removed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { WeaponFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (used.Contains(path) || !AssetDatabase.DeleteAsset(path)) continue;
                removed++;
            }

            return removed;
        }

        private static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

        private static bool TryBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (renderer is ParticleSystemRenderer) continue;
                if (!found) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                found = true;
            }

            return found;
        }

        /// <summary>Coverage = anything drawn; box-filtered to the icon size, white.</summary>
        private static Color32[] Downsample(Texture2D source)
        {
            var src = source.GetPixels32();
            var size = WeaponSize;
            var n = WeaponSupersample;
            var result = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var covered = 0;
                for (var sy = 0; sy < n; sy++)
                for (var sx = 0; sx < n; sx++)
                    if (src[(y * n + sy) * size * n + x * n + sx].a > 0) covered++;
                result[y * size + x] = new Color32(255, 255, 255, (byte)(covered * 255 / (n * n)));
            }

            return result;
        }

        // ---- assets ----

        private static Sprite SavePng(string path, Color32[] pixels, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var changed = importer.textureType != TextureImporterType.Sprite || !importer.mipmapEnabled ||
                          !importer.alphaIsTransparency || importer.maxTextureSize != size ||
                          importer.spriteImportMode != SpriteImportMode.Single;
            if (changed)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = true;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = size;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path) ??
                   throw new InvalidOperationException($"No sprite at '{path}'.");
        }

        private static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }
    }
}
