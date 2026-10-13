using System.Collections.Generic;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visual;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Builds the static level geometry (ТЗ §82): saved cell visuals → exact prefabs → their meshes combined
    /// per <see cref="VisibilityChunk"/> (one mesh, one sub-mesh per material) → static batching.
    /// Every vertex stores its cell, so the shader can hide single cells. No GameObject per cell or wall.
    /// </summary>
    public sealed class GeometryBuilder
    {
        public const int DefaultChunkSize = 8;

        private readonly IVisualPrefabs _prefabs;
        private readonly int _chunkSize;
        private readonly PrefabMeshParts _parts = new PrefabMeshParts();
        private readonly MeshAccumulator _accumulator = new MeshAccumulator();

        public GeometryBuilder(IVisualPrefabs prefabs, int chunkSize = DefaultChunkSize)
        {
            _prefabs = prefabs;
            _chunkSize = Mathf.Max(1, chunkSize);
        }

        public LevelGeometryView Build(LevelData level, Transform parent, bool staticBatching)
        {
            var geometry = level.Geometry;
            var root = new GameObject("Geometry").transform;
            root.SetParent(parent, false);

            var chunks = new List<VisibilityChunk>();
            var missing = 0;
            for (var y = 0; y < geometry.Height; y += _chunkSize)
            for (var x = 0; x < geometry.Width; x += _chunkSize)
            {
                var rect = new GridRect(x, y, Mathf.Min(_chunkSize, geometry.Width - x), Mathf.Min(_chunkSize, geometry.Height - y));
                var chunk = BuildChunk(level, rect, root, ref missing);
                if (chunk != null)
                    chunks.Add(chunk);
            }

            if (staticBatching && chunks.Count > 0)
                StaticBatchingUtility.Combine(root.gameObject);

            var view = new LevelGeometryView(root, chunks, new CellVisibilityMask(geometry.Width, geometry.Height), missing,
                SnowdriftMask.Create(level));
            view.ApplyVisibility();
            return view;
        }

        private VisibilityChunk BuildChunk(LevelData level, GridRect rect, Transform root, ref int missing)
        {
            var geometry = level.Geometry;
            _accumulator.Clear();

            for (var y = rect.Y; y < rect.YMax; y++)
            for (var x = rect.X; x < rect.XMax; x++)
            {
                var cell = new GridPosition(x, y);
                var cellType = geometry.GetCell(cell);
                foreach (var layer in CellLayers.All)
                {
                    if (!CellLayers.Exists(cellType, layer))
                        continue;

                    var choice = VisualResolver.ResolveCell(level, cell, layer);
                    if (!Append(CellLayers.Kind(layer), choice, cell, Vector3.zero, 90f * choice.Rotation))
                        missing++;
                }

                // Optional: no decor is normal, a decor without its prefab is missing. Its vertices keep its cell
                // (hidden with it) even where the prop sticks out.
                var decor = VisualResolver.ResolveDecor(level, cell);
                if (decor.IsEmpty)
                    continue;

                VisualResolver.ResolveDecorPose(level, cell, decor, out var offset, out var yaw);
                if (!Append(VisualKind.Decor, decor, cell, offset, yaw))
                    missing++;
            }

            if (_accumulator.IsEmpty)
                return null;

            var name = $"Chunk {rect.X / _chunkSize}_{rect.Y / _chunkSize}";
            var mesh = _accumulator.ToMesh(name);
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var materials = new Material[_accumulator.Materials.Count];
            for (var i = 0; i < materials.Length; i++)
                materials[i] = _accumulator.Materials[i];
            renderer.sharedMaterials = materials;

            return new VisibilityChunk(rect, go, mesh);
        }

        /// <summary>Adds the prefab of <paramref name="choice"/> at the cell; false when it has no prefab.</summary>
        private bool Append(VisualKind kind, VisualChoice choice, GridPosition cell, Vector3 offset, float yaw)
        {
            var prefab = choice.IsEmpty ? null : _prefabs.Get(kind, choice.VariantId);
            if (prefab == null)
                return false;

            // Same placement as Instantiate(prefab, cell + offset, yaw): the root keeps its own scale.
            var matrix = Matrix4x4.TRS(cell.ToWorld() + offset, Quaternion.Euler(0f, yaw, 0f), prefab.transform.localScale);
            foreach (var part in _parts.Get(prefab))
                _accumulator.Append(part, matrix * part.LocalMatrix, new Vector2(cell.X, cell.Y));
            return true;
        }
    }
}
