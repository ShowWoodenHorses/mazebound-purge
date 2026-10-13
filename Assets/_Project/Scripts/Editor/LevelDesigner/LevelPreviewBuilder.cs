using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Presentation.Visual;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>
    /// Editor-only 3D preview of a level in the open scene, built exactly from the saved visual design
    /// (the same resolution runtime will use). The preview is never saved with the scene.
    /// Runtime will build geometry differently (mesh combine, chunks); this is only for looking at the design.
    /// </summary>
    internal static class LevelPreviewBuilder
    {
        private const string RootName = "[Maze Level Preview]";

        /// <summary>Soft drift edges of the previewed level (same shader path as in the game); null without drifts.</summary>
        private static SnowdriftMask _snowdrifts;

        /// <summary>Returns the number of cells/objects that could not be shown (no visual or missing prefab).</summary>
        public static int Build(LevelData level)
        {
            Clear();
            var root = new GameObject(RootName);
            var geometryRoot = new GameObject("Geometry").transform;
            var objectsRoot = new GameObject("Objects").transform;
            geometryRoot.SetParent(root.transform, false);
            objectsRoot.SetParent(root.transform, false);

            var missing = 0;
            var geometry = level.Geometry;
            var theme = level.VisualTheme;

            // Every cell has a floor; wall cells get the wall on top of it.
            for (var i = 0; i < geometry.CellCount; i++)
            foreach (var layer in CellLayers.All)
            {
                var cell = geometry.ToPosition(i);
                if (!CellLayers.Exists(geometry.GetCell(cell), layer))
                    continue;

                var set = theme != null ? theme.GetSet(CellLayers.Kind(layer)) : null;
                if (!Spawn(set, VisualResolver.ResolveCell(level, cell, layer), cell, geometryRoot))
                    missing++;
            }

            // Optional decor on floor cells, named by cell: DecorSceneHandles moves them while their placement is dragged.
            var decorSet = theme != null ? theme.GetSet(VisualKind.Decor) : null;
            for (var i = 0; i < geometry.CellCount; i++)
            {
                var cell = geometry.ToPosition(i);
                var decor = VisualResolver.ResolveDecor(level, cell);
                if (decor.IsEmpty)
                    continue;

                if (!Spawn(decorSet, decor, cell, geometryRoot, DecorName(cell)))
                {
                    missing++;
                    continue;
                }

                PlaceDecor(level, cell, decor, geometryRoot.Find(DecorName(cell)));
            }

            foreach (var entity in level.AllEntities())
            {
                if (!VisualKinds.TryGetForEntity(entity, out var kind, out _))
                    continue;

                var choice = VisualResolver.ResolveObject(level, entity);
                // Zombie orientation is gameplay state (Facing), not part of the visual assignment.
                if (entity is ZombieSpawnData zombie)
                    choice = new VisualChoice(choice.VariantId, (int)zombie.Facing);

                var set = theme != null ? theme.GetSet(kind) : null;
                var instance = Spawn(set, choice, entity.Position, objectsRoot, entity.Id);
                if (!instance)
                    missing++;
                else if (VisualKinds.IsPlaceable(entity))
                    PlaceObject(level, entity, objectsRoot.Find(entity.Id));
            }

            // Light fixtures (torches) on the walls, named by light id: LightSceneHandles moves them with their light.
            var lightSet = theme != null ? theme.GetSet(VisualKind.Light) : null;
            foreach (var light in level.Lights)
            {
                var fixture = LightFixtures.Resolve(level, light);
                if (fixture.IsEmpty || !LightFixtures.TryGetPose(geometry, light, out _, out _))
                    continue;

                if (!Spawn(lightSet, fixture, light.Cell, objectsRoot, LightName(light.Id)))
                    missing++;
                else
                    PlaceLight(level, light, objectsRoot.Find(LightName(light.Id)));
            }

            // DontSave keeps the scene clean (not marked dirty, never saved). Such objects also survive a scene
            // unload, so the preview is removed before any scene closes (see PreviewSceneGuard).
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                transform.gameObject.hideFlags = HideFlags.DontSave;

            _snowdrifts = SnowdriftMask.Create(level);
            _snowdrifts?.Bind();

            Selection.activeGameObject = root;
            return missing;
        }

        public static string DecorName(GridPosition cell) => $"Decor {cell.X}_{cell.Y}";

        public static string LightName(string lightId) => $"Light {lightId}";

        /// <summary>Puts a preview light fixture on its wall (hidden when the light has no wall next to it).</summary>
        public static void PlaceLight(LevelData level, LightSourceData light, Transform instance)
        {
            if (instance == null)
                return;

            var mounted = LightFixtures.TryGetPose(level.Geometry, light, out var offset, out var yaw);
            instance.gameObject.SetActive(mounted);
            instance.localPosition = light.Cell.ToWorld() + offset;
            instance.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Parent of the preview's object instances (named by entity id), or null without a preview.</summary>
        public static Transform FindObjectsRoot()
        {
            var geometry = FindDecorRoot();
            return geometry != null ? geometry.parent.Find("Objects") : null;
        }

        /// <summary>Puts a preview pickup where the level says (centre or hand-set placement).</summary>
        public static void PlaceObject(LevelData level, LevelEntityData entity, Transform instance)
        {
            if (instance == null)
                return;

            VisualResolver.ResolveObjectPose(level, entity, VisualResolver.ResolveObject(level, entity), out var offset, out var yaw);
            instance.localPosition = entity.Position.ToWorld() + offset;
            instance.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Parent of the preview's cells and decor instances, or null without a preview.</summary>
        public static Transform FindDecorRoot()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                foreach (var go in scene.GetRootGameObjects())
                    if (go.name == RootName)
                        return go.transform.Find("Geometry");
            }

            return null;
        }

        /// <summary>Puts a preview decor instance where the level says (centre or hand-set placement).</summary>
        public static void PlaceDecor(LevelData level, GridPosition cell, VisualChoice decor, Transform instance)
        {
            if (instance == null)
                return;

            VisualResolver.ResolveDecorPose(level, cell, decor, out var offset, out var yaw);
            instance.localPosition = cell.ToWorld() + offset;
            instance.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public static void Clear()
        {
            ReleaseSnowdrifts();
            for (var i = 0; i < SceneManager.sceneCount; i++)
                Clear(SceneManager.GetSceneAt(i));
        }

        private static void Clear(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            foreach (var go in scene.GetRootGameObjects())
                if (go.name == RootName)
                    Object.DestroyImmediate(go);
        }

        /// <summary>Drops the preview's drift mask (keyword off); also before a domain reload, which would orphan it.</summary>
        public static void ReleaseSnowdrifts()
        {
            _snowdrifts?.Dispose();
            _snowdrifts = null;
        }

        /// <summary>
        /// Removes the preview before its scene closes (scene switch, new scene, Test Runner reload).
        /// Otherwise the DontSave preview would outlive the scene as an orphan: gone from the Hierarchy,
        /// still drawn in the Scene view, and unreachable by Clear Preview.
        /// </summary>
        [InitializeOnLoad]
        private static class PreviewSceneGuard
        {
            static PreviewSceneGuard()
            {
                EditorSceneManager.sceneClosing += (scene, removingScene) => Clear(scene);
                AssemblyReloadEvents.beforeAssemblyReload += ReleaseSnowdrifts;
            }
        }

        private static bool Spawn(VisualSet set, VisualChoice choice, GridPosition cell, Transform parent, string name = null)
        {
            if (set == null || choice.IsEmpty)
                return false;

            var variant = set.FindVariant(choice.VariantId);
            var prefab = variant?.Prefab?.editorAsset as GameObject;
            if (prefab == null)
                return false;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name ?? $"{choice.VariantId} {cell}";
            instance.transform.localPosition = cell.ToWorld();
            instance.transform.localRotation = Quaternion.Euler(0f, 90f * choice.Rotation, 0f);
            return true;
        }
    }
}
