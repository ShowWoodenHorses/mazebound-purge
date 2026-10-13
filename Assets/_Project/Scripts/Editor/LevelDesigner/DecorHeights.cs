using Maze.Core.Visual;
using UnityEngine;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>
    /// Measures decor prefabs into <see cref="VisualVariant.Height"/> (top of the meshes above the floor, with the
    /// prefab's root scale): auto placement skips variants taller than the level's Max Auto Decor Height.
    /// Called before every auto placement (Generate New, Regenerate Visuals, Place Decor), so swapping a model
    /// never leaves a stale height.
    /// </summary>
    internal static class DecorHeights
    {
        public static void Refresh(VisualTheme theme)
        {
            if (theme != null) Refresh(theme.GetSet(VisualKind.Decor));
        }

        public static void Refresh(VisualSet set)
        {
            if (set == null) return;

            var changed = false;
            foreach (var variant in set.Variants)
            {
                var prefab = variant.Prefab != null ? variant.Prefab.editorAsset as GameObject : null;
                if (prefab == null) continue;

                var height = Mathf.Max(0f, MeshBounds(prefab).max.y);
                height = Mathf.Round(height * 1000f) / 1000f;
                if (Mathf.Approximately(variant.Height, height)) continue;
                variant.Height = height;
                changed = true;
            }

            if (changed) UnityEditor.EditorUtility.SetDirty(set);
        }

        /// <summary>Bounds of all meshes as placed in a cell: root position and rotation ignored, root scale kept.</summary>
        public static Bounds MeshBounds(GameObject root)
        {
            var toPlaced = Matrix4x4.Scale(root.transform.localScale) * root.transform.worldToLocalMatrix;
            var bounds = new Bounds();
            var first = true;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var matrix = toPlaced * filter.transform.localToWorldMatrix;
                var local = filter.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var point = matrix.MultiplyPoint3x4(corner);
                    if (first)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return bounds;
        }
    }
}
