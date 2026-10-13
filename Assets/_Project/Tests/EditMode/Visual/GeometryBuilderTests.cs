using System.Collections.Generic;
using System.Linq;
using Maze.Core.Authoring;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Presentation.Visual;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.Visual
{
    public class GeometryBuilderTests
    {
        private const int CubeVertexCount = 24;

        private VisualFixture _fixture;
        private FakePrefabs _prefabs;
        private Material _floorMaterial;
        private Material _wallMaterial;
        private GameObject _parent;
        private LevelGeometryView _view;
        private readonly List<Object> _created = new List<Object>();

        private LevelData Level => _fixture.Level;

        [SetUp]
        public void SetUp()
        {
            _fixture = new VisualFixture();
            LevelAuthoring.GenerateNew(Level);

            _floorMaterial = Track(new Material(Shader.Find(GeometryShader.Name)) { name = "floor" });
            _wallMaterial = Track(new Material(Shader.Find(GeometryShader.Name)) { name = "wall" });
            _prefabs = new FakePrefabs
            {
                // Floor well below the wall (y ≈ -0.5) so wall vertices are told apart by height.
                Floor = CubePrefab("floor", _floorMaterial, new Vector3(0f, -0.5f, 0f), new Vector3(1f, 0.1f, 1f)),
                // Wall arm pointing North (+Z) from the cell centre: rotation must move it.
                Wall = CubePrefab("wall", _wallMaterial, new Vector3(0f, 0.5f, 0.3f), new Vector3(0.2f, 1f, 0.4f)),
            };
            _parent = Track(new GameObject("Level View"));
        }

        [TearDown]
        public void TearDown()
        {
            _view?.Dispose();
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            _fixture.Dispose();
        }

        private LevelGeometryView Build(int chunkSize = GeometryBuilder.DefaultChunkSize)
        {
            _view = new GeometryBuilder(_prefabs, chunkSize).Build(Level, _parent.transform, staticBatching: false);
            return _view;
        }

        [Test]
        public void Chunks_CoverEveryCellExactlyOnce()
        {
            var view = Build(chunkSize: 8);
            var geometry = Level.Geometry;

            Assert.AreEqual(9, view.Chunks.Count, "21x21 in 8x8 chunks = 3x3.");
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var cell = geometry.ToPosition(i);
                Assert.AreEqual(1, view.Chunks.Count(c => c.Cells.Contains(cell)), $"Cell {cell}");
            }
        }

        [Test]
        public void CombinedMesh_HasFloorForEveryCell_AndWallForEveryWallCell()
        {
            var view = Build();
            var geometry = Level.Geometry;
            var walls = 0;
            for (var i = 0; i < geometry.CellCount; i++)
                if (geometry.GetCell(geometry.ToPosition(i)) == CellType.Wall) walls++;

            var vertices = view.Chunks.Sum(c => c.Mesh.vertexCount);
            Assert.AreEqual((geometry.CellCount + walls) * CubeVertexCount, vertices);
            Assert.AreEqual(0, view.MissingVisuals);
        }

        [Test]
        public void EveryVertex_CarriesItsCell_AndLiesInsideThatCell()
        {
            var view = Build();
            var cells = new List<Vector2>();
            foreach (var chunk in view.Chunks)
            {
                chunk.Mesh.GetUVs(GeometryShader.CellUvChannel, cells);
                var vertices = chunk.Mesh.vertices;
                Assert.AreEqual(vertices.Length, cells.Count);
                for (var i = 0; i < vertices.Length; i++)
                {
                    var cell = new GridPosition((int)cells[i].x, (int)cells[i].y);
                    Assert.IsTrue(chunk.Cells.Contains(cell), $"{cell} outside {chunk.Cells}");
                    Assert.LessOrEqual(Mathf.Abs(vertices[i].x - cell.X), 0.5001f);
                    Assert.LessOrEqual(Mathf.Abs(vertices[i].z - cell.Y), 0.5001f);
                }
            }
        }

        [Test]
        public void Chunk_HasOneSubMeshPerMaterial()
        {
            var view = Build();
            foreach (var chunk in view.Chunks)
            {
                var materials = chunk.GameObject.GetComponent<MeshRenderer>().sharedMaterials;
                Assert.AreEqual(chunk.Mesh.subMeshCount, materials.Length);
                CollectionAssert.AllItemsAreUnique(materials);
                CollectionAssert.IsSubsetOf(materials, new[] { _floorMaterial, _wallMaterial });
            }
        }

        [Test]
        public void SavedRotation_IsApplied()
        {
            var geometry = Level.Geometry;
            var wallCell = Enumerable.Range(0, geometry.CellCount).Select(geometry.ToPosition)
                .First(p => geometry.GetCell(p) == CellType.Wall);
            Level.VisualData.SetCellOverride(wallCell, CellLayer.Wall, new VisualChoice("wall_default", 1));

            var view = Build();
            var chunk = view.Chunks.First(c => c.Cells.Contains(wallCell));
            var cells = new List<Vector2>();
            chunk.Mesh.GetUVs(GeometryShader.CellUvChannel, cells);
            var vertices = chunk.Mesh.vertices;

            // Wall part vertices of that cell are above the floor (y >= 0). Rotated a quarter turn clockwise
            // from above, the North arm points East: centre x = cell + 0.3, z = cell.
            var wallVertices = Enumerable.Range(0, vertices.Length)
                .Where(i => (int)cells[i].x == wallCell.X && (int)cells[i].y == wallCell.Y && vertices[i].y > -0.1f)
                .Select(i => vertices[i]).ToList();
            Assert.AreEqual(CubeVertexCount, wallVertices.Count);
            Assert.AreEqual(wallCell.X + 0.3f, wallVertices.Average(v => v.x), 1e-4f);
            Assert.AreEqual(wallCell.Y, wallVertices.Average(v => v.z), 1e-4f);
        }

        [Test]
        public void Decor_IsMergedIntoItsCell_MissingDecorPrefabIsCounted()
        {
            _fixture.AddSet(VisualKind.Decor, null, VisualFixture.Variant("decor_barrel", 1));
            var floor = Enumerable.Range(0, Level.Geometry.CellCount).Select(Level.Geometry.ToPosition)
                .First(p => Level.Geometry.GetCell(p) == CellType.Floor);
            LevelEditing.SetCellOverride(Level, floor, CellLayer.Decor, new VisualChoice("decor_barrel", 1));
            var without = Build().Chunks.Sum(c => c.Mesh.vertexCount);
            Assert.AreEqual(1, _view.MissingVisuals, "Decor without its prefab is missing.");
            _view.Dispose();

            _prefabs.Decor = CubePrefab("decor", _wallMaterial, new Vector3(0f, 0.2f, 0f), new Vector3(0.3f, 0.4f, 0.3f));
            var view = Build();
            Assert.AreEqual(0, view.MissingVisuals);
            Assert.AreEqual(without + CubeVertexCount, view.Chunks.Sum(c => c.Mesh.vertexCount), "One more cube.");

            var chunk = view.Chunks.Single(c => c.Cells.Contains(floor));
            var cells = new List<Vector2>();
            chunk.Mesh.GetUVs(GeometryShader.CellUvChannel, cells);
            var vertices = chunk.Mesh.vertices;
            // Above the floor cube (its top is at -0.45), around the cell centre.
            var decorVertices = Enumerable.Range(0, vertices.Length).Where(i => vertices[i].y > -0.1f &&
                Mathf.Abs(vertices[i].x - floor.X) <= 0.16f && Mathf.Abs(vertices[i].z - floor.Y) <= 0.16f).ToList();
            Assert.AreEqual(CubeVertexCount, decorVertices.Count, "The decor cube stands in its cell.");
            foreach (var i in decorVertices)
                Assert.AreEqual(new Vector2(floor.X, floor.Y), cells[i], "Hidden with its cell.");
        }

        [Test]
        public void DecorPlacement_ShiftsLiftsAndTurns_VerticesKeepTheirCell()
        {
            _fixture.AddSet(VisualKind.Decor, null, VisualFixture.Variant("decor_barrel", 1));
            var floor = Enumerable.Range(0, Level.Geometry.CellCount).Select(Level.Geometry.ToPosition)
                .First(p => Level.Geometry.GetCell(p) == CellType.Floor);
            LevelEditing.SetCellOverride(Level, floor, CellLayer.Decor, new VisualChoice("decor_barrel"));
            LevelEditing.SetDecorPlacement(Level, floor, new Vector2(0.3f, -0.2f), 1.5f, 90f); // Above the wall cubes (their top is at 1).
            // A bar 0.6 long along North (+Z): turned by 90° clockwise it lies along East (+X).
            _prefabs.Decor = CubePrefab("decor", _wallMaterial, Vector3.zero, new Vector3(0.1f, 0.1f, 0.6f));
            var view = Build();

            var chunk = view.Chunks.Single(c => c.Cells.Contains(floor));
            var cells = new List<Vector2>();
            chunk.Mesh.GetUVs(GeometryShader.CellUvChannel, cells);
            var vertices = chunk.Mesh.vertices;
            var bar = Enumerable.Range(0, vertices.Length).Where(i => vertices[i].y > 1.4f && vertices[i].y < 1.6f).ToList();
            Assert.AreEqual(CubeVertexCount, bar.Count, "Lifted to 1.5 m.");
            var min = new Vector3(bar.Min(i => vertices[i].x), 0f, bar.Min(i => vertices[i].z));
            var max = new Vector3(bar.Max(i => vertices[i].x), 0f, bar.Max(i => vertices[i].z));
            Assert.AreEqual(floor.X + 0.3f, (min.x + max.x) * 0.5f, 1e-4f, "Shifted east.");
            Assert.AreEqual(floor.Y - 0.2f, (min.z + max.z) * 0.5f, 1e-4f, "Shifted south.");
            Assert.AreEqual(0.6f, max.x - min.x, 1e-4f, "Turned: long along East.");
            foreach (var i in bar)
                Assert.AreEqual(new Vector2(floor.X, floor.Y), cells[i], "Still hidden with its cell.");
        }

        [Test]
        public void MissingPrefab_IsCounted_AndSkipped()
        {
            _prefabs.Floor = null;
            var view = Build();

            Assert.AreEqual(Level.Geometry.CellCount, view.MissingVisuals);
            Assert.IsTrue(view.Chunks.All(c => c.GameObject.GetComponent<MeshRenderer>().sharedMaterials.SequenceEqual(new[] { _wallMaterial })));
        }

        [Test]
        public void Visibility_HidesSingleCells_AndSwitchesOffChunksWithoutVisibleCells()
        {
            var view = Build(chunkSize: 8);
            var visible = new GridPosition(3, 3);

            view.SetAllVisible(false);
            view.SetCellVisible(visible, true);
            view.ApplyVisibility();

            Assert.IsTrue(view.Mask.IsVisible(visible));
            Assert.IsFalse(view.Mask.IsVisible(new GridPosition(4, 3)));
            var pixels = view.Mask.Texture.GetPixelData<byte>(0);
            Assert.AreEqual(255, pixels[3 * 21 + 3]);
            Assert.AreEqual(0, pixels[3 * 21 + 4]);

            foreach (var chunk in view.Chunks)
                Assert.AreEqual(chunk.Cells.Contains(visible), chunk.IsVisible, chunk.Cells.ToString());

            view.SetAllVisible(true);
            view.ApplyVisibility();
            Assert.IsTrue(view.Chunks.All(c => c.IsVisible));
        }

        [Test]
        public void Dispose_DestroysChunkMeshes_AndMask()
        {
            var view = Build();
            var meshes = view.Chunks.Select(c => c.Mesh).ToList();
            var texture = view.Mask.Texture;

            view.Dispose();
            _view = null;

            Assert.IsTrue(meshes.All(m => m == null));
            Assert.IsTrue(texture == null);
            Assert.IsFalse(Shader.IsKeywordEnabled(GeometryShader.VisibilityKeyword));
        }

        [Test]
        public void EntityViewFactory_PlacesSavedVariant_AndRegistryRemovesIt()
        {
            var door = new DoorData("door_1", new GridPosition(2, 5));
            Level.MutableDoors.Add(door);
            Level.VisualData.SetObjectOverride(door.Id, new VisualChoice("door_red", 1));
            _prefabs.Door = CubePrefab("door", _wallMaterial, Vector3.zero, Vector3.one);

            var view = new EntityViewFactory(_prefabs).Create(Level, door, _parent.transform);
            var registry = new EntityViewRegistry();
            registry.Add(view);

            Assert.AreEqual("door_red", _prefabs.LastRequested);
            Assert.AreEqual(new Vector3(2f, 0f, 5f), view.GameObject.transform.localPosition);
            Assert.AreEqual(90f, view.GameObject.transform.localEulerAngles.y, 1e-3f);
            Assert.IsTrue(registry.TryGet("door_1", out _));

            var instance = view.GameObject;
            Assert.IsTrue(registry.Remove("door_1"));
            Assert.IsTrue(instance == null);
            Assert.IsFalse(registry.TryGet("door_1", out _));
        }

        [Test]
        public void EntityViewFactory_AppliesPickupPlacement_ButNotToDroppedItems()
        {
            var key = new KeyData("key_1", new GridPosition(4, 6));
            Level.MutableKeys.Add(key);
            Level.VisualData.SetObjectOverride(key.Id, new VisualChoice("key_red"));
            LevelEditing.SetObjectPlacement(Level, key, new Vector2(-0.2f, 0.1f), 0.6f, 45f);
            _prefabs.Door = CubePrefab("key", _wallMaterial, Vector3.zero, Vector3.one * 0.1f);

            var view = KeyFactoryOf(_prefabs).Create(Level, key, _parent.transform);
            Assert.AreEqual(new Vector3(3.8f, 0.6f, 6.1f), view.GameObject.transform.localPosition, "On the table.");
            Assert.AreEqual(45f, view.GameObject.transform.localEulerAngles.y, 1e-3f);

            // A dropped item (same id) appears where it was dropped, on the floor.
            var dropped = KeyFactoryOf(_prefabs).Create(key.Id, VisualKind.Key, new VisualChoice("key_red"), new GridPosition(1, 1), _parent.transform);
            Assert.AreEqual(new Vector3(1f, 0f, 1f), dropped.GameObject.transform.localPosition);
            Object.DestroyImmediate(view.GameObject);
            Object.DestroyImmediate(dropped.GameObject);
        }

        /// <summary>Factory over the fake prefabs where keys reuse the door slot.</summary>
        private static EntityViewFactory KeyFactoryOf(FakePrefabs prefabs) => new EntityViewFactory(new KeysAsDoors(prefabs));

        private sealed class KeysAsDoors : IVisualPrefabs
        {
            private readonly FakePrefabs _prefabs;
            public KeysAsDoors(FakePrefabs prefabs) => _prefabs = prefabs;
            public GameObject Get(VisualKind kind, string variantId) =>
                _prefabs.Get(kind == VisualKind.Key ? VisualKind.Door : kind, variantId);
        }

        [Test]
        public void LevelVisualUsage_ListsEachUsedVariantOnce()
        {
            var keys = LevelVisualUsage.Collect(Level);

            CollectionAssert.AllItemsAreUnique(keys);
            var geometry = Level.Geometry;
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var cell = geometry.ToPosition(i);
                foreach (var layer in CellLayers.All)
                {
                    var choice = VisualResolver.ResolveCell(Level, cell, layer);
                    if (!choice.IsEmpty)
                        CollectionAssert.Contains(keys, new VisualKey(CellLayers.Kind(layer), choice.VariantId));
                }
            }

            CollectionAssert.DoesNotContain(keys.Select(k => k.VariantId), "floor_unused");
        }

        // ------------------------------------------------------------ helpers

        private T Track<T>(T o) where T : Object
        {
            _created.Add(o);
            return o;
        }

        private GameObject CubePrefab(string name, Material material, Vector3 position, Vector3 scale)
        {
            var root = Track(new GameObject(name));
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return root;
        }

        private sealed class FakePrefabs : IVisualPrefabs
        {
            public GameObject Floor;
            public GameObject Wall;
            public GameObject Door;
            public GameObject Decor;
            public string LastRequested;

            public GameObject Get(VisualKind kind, string variantId)
            {
                LastRequested = variantId;
                switch (kind)
                {
                    case VisualKind.Floor: return Floor;
                    case VisualKind.Wall: return Wall;
                    case VisualKind.Door: return Door;
                    case VisualKind.Decor: return Decor;
                    default: return null;
                }
            }
        }
    }
}
