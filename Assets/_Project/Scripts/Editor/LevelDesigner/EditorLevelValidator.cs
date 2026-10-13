using System;
using System.Collections.Generic;
using System.Linq;
using Maze.Core.Level;
using Maze.Core.Validation;
using Maze.Core.Visual;
using Maze.Presentation.Visual;
using UnityEngine;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>
    /// Runtime validator plus checks that need the AssetDatabase: a prefab reference can be well-formed
    /// (valid GUID) yet point to an asset that no longer exists; geometry prefabs must be combinable at runtime.
    /// </summary>
    internal static class EditorLevelValidator
    {
        public static ValidationReport Validate(LevelData level) => LevelValidator.Validate(level, CheckAssets);

        private static void CheckAssets(LevelData level, ValidationReport report)
        {
            CheckPrefabAssetsExist(level, report);
            CheckGeometryPrefabs(level, report);
        }

        private static void CheckPrefabAssetsExist(LevelData level, ValidationReport report)
        {
            var theme = level.VisualTheme;
            if (theme == null)
                return;

            var sets = Enum.GetValues(typeof(VisualKind)).Cast<VisualKind>()
                .Select(theme.GetSet)
                .Where(set => set != null)
                .Distinct();

            foreach (var set in sets)
            foreach (var variant in set.Variants)
            {
                if (variant.Prefab == null || !variant.Prefab.RuntimeKeyIsValid())
                    continue; // already reported by LevelValidator

                if (variant.Prefab.editorAsset == null)
                    report.Add(ValidationSeverity.Error, ValidationCategory.Visual, ValidationCodes.BrokenPrefabReference,
                        $"Variant '{variant.Id}' in set '{set.name}' references a prefab that does not exist in the project.");
            }
        }

        /// <summary>
        /// Floor, wall and decor prefabs are merged into chunk meshes at runtime (GeometryBuilder): their meshes must be
        /// readable and their materials must use the Maze/Geometry shader, otherwise visibility cannot hide cells.
        /// </summary>
        private static void CheckGeometryPrefabs(LevelData level, ValidationReport report)
        {
            var theme = level.VisualTheme;
            if (theme == null)
                return;

            var reported = new HashSet<UnityEngine.Object>();
            foreach (var kind in new[] { VisualKind.Floor, VisualKind.Wall, VisualKind.Decor })
            {
                var set = theme.GetSet(kind);
                if (set == null)
                    continue;

                foreach (var variant in set.Variants)
                {
                    var prefab = variant.Prefab != null && variant.Prefab.RuntimeKeyIsValid()
                        ? variant.Prefab.editorAsset as GameObject
                        : null;
                    if (prefab == null)
                        continue;

                    foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!(renderer is MeshRenderer))
                        {
                            report.Add(ValidationSeverity.Warning, ValidationCategory.Visual, ValidationCodes.GeometryUnsupportedRenderer,
                                $"Geometry prefab '{prefab.name}' ({kind} '{variant.Id}') has a {renderer.GetType().Name}; " +
                                "only MeshRenderers are shown in game.");
                            continue;
                        }

                        var filter = renderer.GetComponent<MeshFilter>();
                        var mesh = filter != null ? filter.sharedMesh : null;
                        if (mesh != null && !mesh.isReadable && reported.Add(mesh))
                            report.Add(ValidationSeverity.Error, ValidationCategory.Visual, ValidationCodes.GeometryMeshNotReadable,
                                $"Mesh '{mesh.name}' of geometry prefab '{prefab.name}' is not readable: enable Read/Write " +
                                "in its import settings, otherwise it is not shown in game.");

                        foreach (var material in renderer.sharedMaterials)
                            if (material != null && !GeometryShader.Supports(material) && reported.Add(material))
                                report.Add(ValidationSeverity.Warning, ValidationCategory.Visual, ValidationCodes.GeometryShaderUnsupported,
                                    $"Material '{material.name}' of geometry prefab '{prefab.name}' does not use the " +
                                    $"'{GeometryShader.Name}' shader: fog of war cannot hide its cells.");
                    }
                }
            }
        }
    }
}
