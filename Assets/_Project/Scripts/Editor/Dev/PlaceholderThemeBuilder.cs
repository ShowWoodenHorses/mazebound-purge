using System.Collections.Generic;
using Maze.Core.Definitions;
using Maze.Core.Visual;
using Maze.Presentation.Visual;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Creates a placeholder VisualTheme with primitive-based prefabs (no colliders: physics is not used, ТЗ §3).
    /// Wall prefabs follow the canonical orientations from WallShapes: End -> N, Straight -> N+S,
    /// Corner -> N+E, TJunction -> N+E+S. Doors at rotation 0 block a North-South passage.
    /// </summary>
    internal static class PlaceholderThemeBuilder
    {
        private const string Root = "Assets/_Project/Art/Placeholders";
        private const string ThemePath = "Assets/_Project/Data/Themes";
        private const float WallHeight = 1.5f;
        private const float WallThickness = 0.5f;

        private static readonly (string Tag, Color Color)[] PairColors =
        {
            ("red", new Color(0.85f, 0.15f, 0.15f)),
            ("blue", new Color(0.15f, 0.35f, 0.9f)),
            ("green", new Color(0.15f, 0.75f, 0.25f)),
            ("yellow", new Color(0.95f, 0.85f, 0.1f)),
            ("orange", new Color(1f, 0.5f, 0.05f)),
            ("cyan", new Color(0.1f, 0.85f, 0.85f)),
            ("purple", new Color(0.65f, 0.2f, 0.9f)),
            ("pink", new Color(1f, 0.4f, 0.7f)),
        };

        private static readonly Color WoodColor = new Color(0.45f, 0.28f, 0.12f);
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        /// <summary>
        /// Rebuilds only the door prefabs in place (same paths and GUIDs, so the theme keeps its references):
        /// closed panel, open panel against the West side and a padlock on coloured (lockable) doors.
        /// </summary>
        [MenuItem("Maze/Dev/Rebuild Placeholder Doors")]
        public static void RebuildDoors()
        {
            Materials.Clear();
            Door("door_wood", WoodColor, lockable: false);
            foreach (var (tag, color) in PairColors)
                Door($"door_{tag}", color, lockable: true);
            AssetDatabase.SaveAssets();
            Debug.Log("[Maze] Placeholder door prefabs rebuilt.");
        }

        [MenuItem("Maze/Dev/Create Placeholder Theme")]
        public static void Create()
        {
            EnsureFolder(Root);
            EnsureFolder(Root + "/Materials");
            EnsureFolder(ThemePath);
            Materials.Clear();

            var theme = CreateAsset<VisualTheme>($"{ThemePath}/PlaceholderTheme.asset");

            AddSet(theme, VisualKind.Floor, "floor_01",
                (Variant("floor_01", 80, Floor("floor_01", new Color(0.55f, 0.55f, 0.52f)))),
                (Variant("floor_02", 10, Floor("floor_02", new Color(0.47f, 0.47f, 0.45f)))),
                (Variant("floor_03", 10, Floor("floor_03", new Color(0.50f, 0.53f, 0.58f)))),
                (Variant("floor_special_blood", 0, Floor("floor_special_blood", new Color(0.45f, 0.08f, 0.08f)), VisualCategory.Special)));

            var wallColor = new Color(0.32f, 0.30f, 0.34f);
            var wallAlt = new Color(0.38f, 0.33f, 0.30f);
            AddSet(theme, VisualKind.Wall, "wall_isolated",
                Variant("wall_isolated", 1, Wall("wall_isolated", wallColor), VisualCategory.Isolated),
                Variant("wall_end", 1, Wall("wall_end", wallColor, Direction.N), VisualCategory.End),
                Variant("wall_straight_01", 80, Wall("wall_straight_01", wallColor, Direction.N, Direction.S), VisualCategory.Straight),
                Variant("wall_straight_02", 20, Wall("wall_straight_02", wallAlt, Direction.N, Direction.S), VisualCategory.Straight),
                Variant("wall_corner", 1, Wall("wall_corner", wallColor, Direction.N, Direction.E), VisualCategory.Corner),
                Variant("wall_t", 1, Wall("wall_t", wallColor, Direction.N, Direction.E, Direction.S), VisualCategory.TJunction),
                Variant("wall_cross", 1, Wall("wall_cross", wallColor, Direction.N, Direction.E, Direction.S, Direction.W), VisualCategory.Cross));

            var doors = new List<VisualVariant>
            {
                Variant("door_wood", 1, Door("door_wood", WoodColor, lockable: false)),
            };
            var keys = new List<VisualVariant>();
            foreach (var (tag, color) in PairColors)
            {
                doors.Add(Variant($"door_{tag}", 1, Door($"door_{tag}", color, lockable: true), colorTag: tag));
                keys.Add(Variant($"key_{tag}", 1, Small($"key_{tag}", PrimitiveType.Cube, color, new Vector3(0.25f, 0.1f, 0.4f)), colorTag: tag));
            }

            AddSet(theme, VisualKind.Door, null, doors.ToArray());
            AddSet(theme, VisualKind.Key, null, keys.ToArray());
            AddSet(theme, VisualKind.Exit, null, Variant("exit_01", 1, Exit("exit_01")));
            AddSet(theme, VisualKind.Medkit, null,
                Variant("medkit_01", 1, Small("medkit_01", PrimitiveType.Cube, new Color(0.95f, 0.95f, 0.95f), new Vector3(0.4f, 0.25f, 0.3f))));
            AddSet(theme, VisualKind.Weapon, null,
                Variant("weapon_01", 1, Small("weapon_01", PrimitiveType.Cube, new Color(1f, 0.55f, 0.1f), new Vector3(0.15f, 0.12f, 0.6f))));
            AddSet(theme, VisualKind.Zombie, null,
                Variant("zombie_01", 1, Small("zombie_01", PrimitiveType.Capsule, new Color(0.35f, 0.6f, 0.3f), new Vector3(0.6f, 0.9f, 0.6f))),
                Variant("zombie_02", 1, Small("zombie_02", PrimitiveType.Capsule, new Color(0.45f, 0.5f, 0.3f), new Vector3(0.6f, 0.9f, 0.6f))));
            AddSet(theme, VisualKind.MapFragment, null,
                Variant("fragment_01", 1, Small("fragment_01", PrimitiveType.Cube, new Color(0.6f, 0.35f, 0.9f), new Vector3(0.4f, 0.05f, 0.5f))));

            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            Selection.activeObject = theme;
            EditorGUIUtility.PingObject(theme);
            Debug.Log($"[Maze] Placeholder theme created at {AssetDatabase.GetAssetPath(theme)}");
        }

        private const string PlayerDataPath = "Assets/_Project/Data/Player";
        private const string ZombieDataPath = "Assets/_Project/Data/Zombies";

        /// <summary>
        /// Placeholder player: capsule with a "nose" showing the facing (+Z), plus PlayerDefinition and PlayerVisual
        /// assets made Addressable at their fixed addresses. Existing definition values are kept.
        /// </summary>
        [MenuItem("Maze/Dev/Create Placeholder Player")]
        public static void CreatePlayer()
        {
            EnsureFolder(Root);
            EnsureFolder(Root + "/Materials");
            EnsureFolder(PlayerDataPath);
            Materials.Clear();

            var root = new GameObject("player_placeholder");
            AddPart(root, PrimitiveType.Capsule, new Color(0.95f, 0.75f, 0.2f), new Vector3(0f, 0.6f, 0f), new Vector3(0.55f, 0.6f, 0.55f));
            AddPart(root, PrimitiveType.Cube, new Color(0.2f, 0.2f, 0.25f), new Vector3(0f, 0.95f, 0.28f), new Vector3(0.3f, 0.12f, 0.12f));
            var prefab = SavePrefab(root);

            CreateAsset<PlayerDefinition>($"{PlayerDataPath}/PlayerDefinition.asset");
            var visual = CreateAsset<PlayerVisualDefinition>($"{PlayerDataPath}/PlayerVisual.asset");
            visual.Prefab = new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)));
            EditorUtility.SetDirty(visual);

            var problem = LevelDesigner.LevelSync.SyncShared(UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.GetSettings(true));
            AssetDatabase.SaveAssets();
            if (problem != null) Debug.LogWarning("[Maze] " + problem);
            Debug.Log($"[Maze] Placeholder player created in {PlayerDataPath} and made Addressable.");
        }

        /// <summary>
        /// Placeholder zombie types in Data/Zombies, one per detection type (ТЗ §73): Walker (vision), Listener
        /// (hearing), Hunter (vision + hearing radius). Existing assets are kept with their values.
        /// </summary>
        [MenuItem("Maze/Dev/Create Placeholder Zombies")]
        public static void CreateZombies()
        {
            EnsureFolder(ZombieDataPath);
            Zombie("ZombieWalker", "walker", ZombieDetectionType.VisionOnly);
            Zombie("ZombieListener", "listener", ZombieDetectionType.HearingOnly);
            Zombie("ZombieHunter", "hunter", ZombieDetectionType.VisionAndHearing);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] Placeholder zombies created in {ZombieDataPath}.");
        }

        private static void Zombie(string assetName, string id, ZombieDetectionType detection)
        {
            var path = $"{ZombieDataPath}/{assetName}.asset";
            if (AssetDatabase.LoadAssetAtPath<ZombieDefinition>(path) != null)
                return;

            var zombie = ScriptableObject.CreateInstance<ZombieDefinition>();
            zombie.Configure(id, detection);
            AssetDatabase.CreateAsset(zombie, path);
        }

        private static AssetReferenceGameObject Reference(GameObject prefab) =>
            new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)));

        private enum Direction { N, E, S, W }

        private static VisualVariant Variant(string id, int weight, GameObject prefab,
            VisualCategory category = VisualCategory.General, string colorTag = null)
        {
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
            return new VisualVariant(id, weight, category, null, new AssetReferenceGameObject(guid), colorTag);
        }

        private static void AddSet(VisualTheme theme, VisualKind kind, string defaultVariantId, params VisualVariant[] variants)
        {
            var set = CreateAsset<VisualSet>($"{ThemePath}/Placeholder{kind}Set.asset");
            set.Kind = kind;
            set.DefaultVariantId = defaultVariantId;
            set.MutableVariants.Clear();
            set.MutableVariants.AddRange(variants);
            EditorUtility.SetDirty(set);
            theme.SetSet(kind, set);
        }

        private static GameObject Floor(string name, Color color)
        {
            var root = new GameObject(name);
            AddPart(root, PrimitiveType.Cube, color, new Vector3(0f, -0.05f, 0f), new Vector3(1f, 0.1f, 1f), geometry: true);
            return SavePrefab(root);
        }

        /// <summary>Central pillar plus an arm towards each connected side.</summary>
        private static GameObject Wall(string name, Color color, params Direction[] arms)
        {
            var root = new GameObject(name);
            var y = WallHeight * 0.5f;
            AddPart(root, PrimitiveType.Cube, color, new Vector3(0f, y, 0f), new Vector3(WallThickness, WallHeight, WallThickness), geometry: true);

            const float armLength = 0.5f - WallThickness * 0.5f;
            const float armCenter = WallThickness * 0.5f + armLength * 0.5f;
            foreach (var arm in arms)
            {
                var alongZ = arm == Direction.N || arm == Direction.S;
                var sign = arm == Direction.N || arm == Direction.E ? 1f : -1f;
                var position = alongZ ? new Vector3(0f, y, sign * armCenter) : new Vector3(sign * armCenter, y, 0f);
                var scale = alongZ ? new Vector3(WallThickness, WallHeight, armLength) : new Vector3(armLength, WallHeight, WallThickness);
                AddPart(root, PrimitiveType.Cube, color, position, scale, geometry: true);
            }

            return SavePrefab(root);
        }

        /// <summary>
        /// Door panel spanning East-West: at rotation 0 it blocks a North-South passage. States for
        /// <see cref="DoorVisual"/>: "Closed" panel, "Open" panel swung against the West side, "Lock" padlock.
        /// </summary>
        private static GameObject Door(string name, Color color, bool lockable)
        {
            var root = new GameObject(name);
            var closed = Child(root, "Closed");
            AddPart(closed, PrimitiveType.Cube, color, new Vector3(0f, 0.7f, 0f), new Vector3(1f, 1.4f, 0.15f));
            var open = Child(root, "Open");
            AddPart(open, PrimitiveType.Cube, color, new Vector3(-0.42f, 0.7f, 0.45f), new Vector3(0.15f, 1.4f, 0.9f));

            GameObject padlock = null;
            if (lockable)
            {
                padlock = Child(root, "Lock");
                var lockColor = new Color(0.12f, 0.12f, 0.12f);
                AddPart(padlock, PrimitiveType.Cube, lockColor, new Vector3(0f, 0.75f, 0f), new Vector3(0.22f, 0.26f, 0.3f));
            }

            var visual = new SerializedObject(root.AddComponent<DoorVisual>());
            visual.FindProperty("_closed").objectReferenceValue = closed;
            visual.FindProperty("_open").objectReferenceValue = open;
            visual.FindProperty("_locked").objectReferenceValue = padlock;
            visual.ApplyModifiedPropertiesWithoutUndo();
            open.SetActive(false);
            return SavePrefab(root);
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static GameObject Exit(string name)
        {
            var root = new GameObject(name);
            var color = new Color(0.1f, 0.8f, 0.9f);
            AddPart(root, PrimitiveType.Cube, color, new Vector3(0f, 0.02f, 0f), new Vector3(0.9f, 0.04f, 0.9f));
            AddPart(root, PrimitiveType.Cube, color, new Vector3(0f, 0.6f, 0.35f), new Vector3(0.9f, 1.2f, 0.1f));
            return SavePrefab(root);
        }

        private static GameObject Small(string name, PrimitiveType type, Color color, Vector3 size)
        {
            var root = new GameObject(name);
            var halfHeight = type == PrimitiveType.Capsule ? size.y : size.y * 0.5f; // capsule mesh is 2 units tall
            AddPart(root, type, color, new Vector3(0f, halfHeight + 0.05f, 0f), size);
            return SavePrefab(root);
        }

        /// <param name="geometry">Floor/wall part: uses the Maze/Geometry shader so single cells can be hidden.</param>
        private static void AddPart(GameObject root, PrimitiveType type, Color color, Vector3 position, Vector3 scale,
            bool geometry = false)
        {
            var part = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(color, geometry);
        }

        private static Material MaterialFor(Color color, bool geometry)
        {
            var key = (geometry ? "G_" : "M_") + ColorUtility.ToHtmlStringRGB(color);
            if (Materials.TryGetValue(key, out var material))
                return material;

            var path = $"{Root}/Materials/{key}.mat";
            material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(geometry ? GeometryShader.Name : "Standard")) { color = color };
                AssetDatabase.CreateAsset(material, path);
            }

            Materials[key] = material;
            return material;
        }

        private static GameObject SavePrefab(GameObject root)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Root}/{root.name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static T CreateAsset<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
                return existing;

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
