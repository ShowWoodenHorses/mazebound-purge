using System.Collections.Generic;
using Maze.Core.Common;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.Presentation.Visual
{
    /// <summary>One sub-mesh of a geometry prefab with its material and placement relative to the prefab root.</summary>
    public readonly struct MeshPart
    {
        public MeshPart(SourceMesh mesh, int subMesh, Material material, Matrix4x4 localMatrix)
        {
            Mesh = mesh;
            SubMesh = subMesh;
            Material = material;
            LocalMatrix = localMatrix;
        }

        public SourceMesh Mesh { get; }
        public int SubMesh { get; }
        public Material Material { get; }
        public Matrix4x4 LocalMatrix { get; }
    }

    /// <summary>CPU copy of a source mesh, read once per level build.</summary>
    public sealed class SourceMesh
    {
        public SourceMesh(Mesh mesh)
        {
            Name = mesh.name;
            Vertices = mesh.vertices;
            Normals = mesh.normals;
            Tangents = mesh.tangents;
            Uv = mesh.uv;
            Triangles = new int[mesh.subMeshCount][];
            for (var i = 0; i < mesh.subMeshCount; i++)
                Triangles[i] = mesh.GetSubMesh(i).topology == MeshTopology.Triangles ? mesh.GetTriangles(i) : null;
        }

        public string Name { get; }
        public Vector3[] Vertices { get; }
        public Vector3[] Normals { get; }
        public Vector4[] Tangents { get; }
        public Vector2[] Uv { get; }

        /// <summary>Per sub-mesh; null for non-triangle topology (not supported for geometry).</summary>
        public int[][] Triangles { get; }
    }

    /// <summary>
    /// Splits geometry prefabs into mesh parts (cached per prefab and per mesh). Only MeshFilter + MeshRenderer
    /// content is used: static geometry prefabs are meshes only (no lights, particles or skinned meshes).
    /// </summary>
    public sealed class PrefabMeshParts
    {
        private readonly Dictionary<GameObject, MeshPart[]> _parts = new Dictionary<GameObject, MeshPart[]>();
        private readonly Dictionary<Mesh, SourceMesh> _meshes = new Dictionary<Mesh, SourceMesh>();
        private readonly HashSet<Material> _reportedMaterials = new HashSet<Material>();

        public MeshPart[] Get(GameObject prefab)
        {
            if (_parts.TryGetValue(prefab, out var cached))
                return cached;

            var parts = new List<MeshPart>();
            var root = prefab.transform;
            var toRoot = root.worldToLocalMatrix;

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsActiveInPrefab(renderer.transform, root) || !renderer.enabled)
                    continue;

                if (!(renderer is MeshRenderer))
                {
                    GameLog.Warning(LogChannel.Visual,
                        $"Geometry prefab '{prefab.name}' has a {renderer.GetType().Name}; only MeshRenderers are used for static geometry.");
                    continue;
                }

                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;

                if (!mesh.isReadable)
                {
                    GameLog.Error(LogChannel.Visual,
                        $"Mesh '{mesh.name}' of geometry prefab '{prefab.name}' is not readable: enable Read/Write in its import settings.");
                    continue;
                }

                var source = Source(mesh);
                var materials = renderer.sharedMaterials;
                var matrix = toRoot * renderer.transform.localToWorldMatrix;
                for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    if (source.Triangles[subMesh] == null || materials.Length == 0)
                        continue;

                    var material = materials[Mathf.Min(subMesh, materials.Length - 1)];
                    if (material == null)
                        continue;

                    ReportUnsupported(material, prefab);
                    parts.Add(new MeshPart(source, subMesh, material, matrix));
                }
            }

            var result = parts.ToArray();
            _parts[prefab] = result;
            return result;
        }

        private SourceMesh Source(Mesh mesh)
        {
            if (!_meshes.TryGetValue(mesh, out var source))
            {
                source = new SourceMesh(mesh);
                _meshes[mesh] = source;
            }

            return source;
        }

        private void ReportUnsupported(Material material, GameObject prefab)
        {
            if (GeometryShader.Supports(material) || !_reportedMaterials.Add(material))
                return;

            GameLog.Warning(LogChannel.Visual,
                $"Material '{material.name}' of geometry prefab '{prefab.name}' does not use the '{GeometryShader.Name}' shader: " +
                "its cells cannot be hidden by visibility.");
        }

        // activeInHierarchy is false for prefab assets, so walk activeSelf up to the root.
        private static bool IsActiveInPrefab(Transform transform, Transform root)
        {
            for (var current = transform; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == root) return true;
            }

            return true;
        }
    }

    /// <summary>Appends transformed mesh parts into one mesh with a sub-mesh per material and the cell in UV3.</summary>
    public sealed class MeshAccumulator
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector4> _tangents = new List<Vector4>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<Vector2> _cells = new List<Vector2>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<List<int>> _indices = new List<List<int>>();

        public bool IsEmpty => _vertices.Count == 0;
        public int VertexCount => _vertices.Count;
        public IReadOnlyList<Material> Materials => _materials;

        public void Clear()
        {
            _vertices.Clear();
            _normals.Clear();
            _tangents.Clear();
            _uv.Clear();
            _cells.Clear();
            _materials.Clear();
            _indices.Clear();
        }

        public void Append(in MeshPart part, Matrix4x4 matrix, Vector2 cell)
        {
            var source = part.Mesh;
            var normalMatrix = matrix.inverse.transpose;
            var mirrored = matrix.determinant < 0f;
            var baseIndex = _vertices.Count;
            var hasNormals = source.Normals.Length == source.Vertices.Length;
            var hasTangents = source.Tangents.Length == source.Vertices.Length;
            var hasUv = source.Uv.Length == source.Vertices.Length;

            for (var i = 0; i < source.Vertices.Length; i++)
            {
                _vertices.Add(matrix.MultiplyPoint3x4(source.Vertices[i]));
                _normals.Add(hasNormals ? normalMatrix.MultiplyVector(source.Normals[i]).normalized : Vector3.up);
                if (hasTangents)
                {
                    var tangent = source.Tangents[i];
                    var direction = matrix.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                    _tangents.Add(new Vector4(direction.x, direction.y, direction.z, mirrored ? -tangent.w : tangent.w));
                }
                else
                {
                    _tangents.Add(new Vector4(1f, 0f, 0f, 1f));
                }

                _uv.Add(hasUv ? source.Uv[i] : Vector2.zero);
                _cells.Add(cell);
            }

            var indices = IndicesFor(part.Material);
            var triangles = source.Triangles[part.SubMesh];
            for (var i = 0; i < triangles.Length; i += 3)
            {
                indices.Add(baseIndex + triangles[i]);
                // Mirrored parts flip winding so faces stay front-facing.
                indices.Add(baseIndex + triangles[mirrored ? i + 2 : i + 1]);
                indices.Add(baseIndex + triangles[mirrored ? i + 1 : i + 2]);
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > ushort.MaxValue)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetTangents(_tangents);
            mesh.SetUVs(0, _uv);
            mesh.SetUVs(GeometryShader.CellUvChannel, _cells);
            mesh.subMeshCount = _indices.Count;
            for (var i = 0; i < _indices.Count; i++)
                mesh.SetTriangles(_indices[i], i, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private List<int> IndicesFor(Material material)
        {
            var index = _materials.IndexOf(material);
            if (index >= 0)
                return _indices[index];

            _materials.Add(material);
            var list = new List<int>();
            _indices.Add(list);
            return list;
        }
    }
}
