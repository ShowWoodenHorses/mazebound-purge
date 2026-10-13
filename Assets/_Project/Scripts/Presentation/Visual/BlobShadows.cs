using System;
using Maze.Core.Level;
using Maze.Core.Visual;
using UnityEngine;
using UnityEngine.Rendering;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Soft round shadows under characters (instead of realtime shadows): a child quad on the floor with the theme's
    /// blob shadow material (shader Maze/BlobShadow). One shared mesh and material, so they batch by instancing;
    /// the quad is part of the view, so it hides with it and is warmed up with it. No material = no shadows.
    /// </summary>
    public sealed class BlobShadows : IDisposable
    {
        /// <summary>Just above the floor, under the fog.</summary>
        private const float Height = 0.02f;

        private readonly LevelData _level;
        private Mesh _mesh;

        public BlobShadows(LevelData level)
        {
            _level = level;
        }

        private ThemeLighting Lighting => _level.VisualTheme != null ? _level.VisualTheme.Lighting : null;

        public void Attach(GameObject view)
        {
            var lighting = Lighting;
            if (view == null || lighting == null || lighting.BlobShadowMaterial == null)
                return;

            _mesh ??= CreateQuad();
            var shadow = new GameObject("BlobShadow");
            shadow.transform.SetParent(view.transform, false);
            shadow.transform.localPosition = new Vector3(0f, Height, 0f);
            shadow.transform.localScale = Vector3.one * lighting.BlobShadowSize;
            shadow.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = shadow.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = lighting.BlobShadowMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public void Dispose()
        {
            UnityObjects.Destroy(_mesh);
            _mesh = null;
        }

        /// <summary>Unit quad in the XZ plane facing up, UV 0..1.</summary>
        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "Maze Blob Shadow", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
