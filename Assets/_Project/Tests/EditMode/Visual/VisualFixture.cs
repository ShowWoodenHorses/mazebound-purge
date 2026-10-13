using System;
using System.Collections.Generic;
using Maze.Core.Common;
using Maze.Core.Definitions;
using Maze.Core.Level;
using Maze.Core.Visual;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace Maze.Tests.EditMode.Visual
{
    /// <summary>Level with a fully populated test theme. Dispose destroys every created asset.</summary>
    internal sealed class VisualFixture : IDisposable
    {
        private readonly List<Object> _created = new List<Object>();

        public VisualFixture(int width = 21, int height = 21, int mazeSeed = 1, int visualSeed = 1, float loopDensity = 0.2f)
        {
            Level = Create<LevelData>();
            Level.Generation.Width = width;
            Level.Generation.Height = height;
            Level.Generation.MazeSeed = mazeSeed;
            Level.Generation.VisualSeed = visualSeed;
            Level.Generation.LoopDensity = loopDensity;

            Pistol = Create<WeaponDefinition>();
            Knife = Create<WeaponDefinition>();
            Walker = Create<ZombieDefinition>();

            Theme = Create<VisualTheme>();
            Floor = AddSet(VisualKind.Floor, null,
                Variant("floor_01", 80),
                Variant("floor_02", 10),
                Variant("floor_03", 10),
                Variant("floor_unused", 0),
                Variant("floor_special_blood", 5, VisualCategory.Special));
            Wall = AddSet(VisualKind.Wall, "wall_default",
                Variant("wall_default", 1),
                Variant("wall_straight_a", 70, VisualCategory.Straight),
                Variant("wall_straight_b", 30, VisualCategory.Straight),
                Variant("wall_corner", 1, VisualCategory.Corner),
                Variant("wall_t", 1, VisualCategory.TJunction),
                Variant("wall_end", 1, VisualCategory.End),
                Variant("wall_cross", 1, VisualCategory.Cross));
            Door = AddSet(VisualKind.Door, null,
                Variant("door_01", 1),
                Variant("door_02", 1),
                Variant("door_red", 1, colorTag: "red"),
                Variant("door_blue", 1, colorTag: "blue"));
            Exit = AddSet(VisualKind.Exit, null, Variant("exit_01", 1));
            Weapon = AddSet(VisualKind.Weapon, null,
                Variant("pistol_black", 1, definition: Pistol),
                Variant("pistol_red", 1, definition: Pistol),
                Variant("knife_01", 1, definition: Knife));
            Key = AddSet(VisualKind.Key, null,
                Variant("key_red", 1, colorTag: "red"),
                Variant("key_blue", 1, colorTag: "blue"));
            AddSet(VisualKind.Medkit, null, Variant("medkit_01", 1));
            AddSet(VisualKind.Zombie, null, Variant("zombie_01", 1), Variant("zombie_02", 1));
            AddSet(VisualKind.MapFragment, null, Variant("fragment_01", 1));

            Level.VisualTheme = Theme;
        }

        public LevelData Level { get; }
        public VisualTheme Theme { get; }
        public VisualSet Floor { get; }
        public VisualSet Wall { get; }
        public VisualSet Door { get; }
        public VisualSet Key { get; }
        public VisualSet Exit { get; }
        public VisualSet Weapon { get; }
        public WeaponDefinition Pistol { get; }
        public WeaponDefinition Knife { get; }
        public ZombieDefinition Walker { get; }

        /// <summary>Variant with a syntactically valid (fake) Addressables GUID, derived from its id.</summary>
        public static VisualVariant Variant(string id, int weight, VisualCategory category = VisualCategory.General,
            ScriptableObject definition = null, string colorTag = null)
        {
            var hash = StableHash.Of(id);
            var guid = $"{hash:x16}{StableHash.Mix(hash):x16}";
            return new VisualVariant(id, weight, category, definition, new AssetReferenceGameObject(guid), colorTag);
        }

        public VisualSet AddSet(VisualKind kind, string defaultVariantId, params VisualVariant[] variants)
        {
            var set = Create<VisualSet>();
            set.Kind = kind;
            set.DefaultVariantId = defaultVariantId;
            set.MutableVariants.AddRange(variants);
            Theme.SetSet(kind, set);
            return set;
        }

        public T Create<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            _created.Add(asset);
            return asset;
        }

        public void Dispose()
        {
            foreach (var asset in _created)
                Object.DestroyImmediate(asset);
        }
    }
}
