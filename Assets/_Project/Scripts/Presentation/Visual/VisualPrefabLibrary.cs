using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Core.Common;
using Maze.Core.Level;
using Maze.Core.Visual;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>Prefab of an exact visual variant. Null when the variant or its prefab is missing.</summary>
    public interface IVisualPrefabs
    {
        GameObject Get(VisualKind kind, string variantId);
    }

    /// <summary>
    /// Prefabs of the variants a level uses, loaded through the level's <see cref="IAssetOwner"/>: they live
    /// exactly as long as the level (ТЗ §44, §84). Runtime never picks a variant, it only looks up saved ids.
    /// </summary>
    public sealed class VisualPrefabLibrary : IVisualPrefabs
    {
        private readonly Dictionary<VisualKey, GameObject> _prefabs = new Dictionary<VisualKey, GameObject>();

        public int Count => _prefabs.Count;

        public GameObject Get(VisualKind kind, string variantId) =>
            _prefabs.TryGetValue(new VisualKey(kind, variantId), out var prefab) ? prefab : null;

        /// <summary>Loads every used variant in parallel. Missing variants are logged and left out.</summary>
        public async UniTask LoadAsync(LevelData level, IAssetOwner assets, CancellationToken cancellation)
        {
            var theme = level.VisualTheme;
            if (theme == null)
            {
                GameLog.Warning(LogChannel.Visual, $"Level '{level.name}' has no visual theme: nothing to show.");
                return;
            }

            var keys = new List<VisualKey>();
            var loads = new List<UniTask<GameObject>>();
            foreach (var key in LevelVisualUsage.Collect(level))
            {
                var set = theme.GetSet(key.Kind);
                var variant = set != null ? set.FindVariant(key.VariantId) : null;
                if (variant?.Prefab == null || !variant.Prefab.RuntimeKeyIsValid())
                {
                    GameLog.Warning(LogChannel.Visual, $"Visual {key} has no prefab in theme '{theme.name}'; it will not be shown.");
                    continue;
                }

                keys.Add(key);
                loads.Add(assets.LoadAsync<GameObject>(variant.Prefab, cancellation));
            }

            var prefabs = await UniTask.WhenAll(loads);
            for (var i = 0; i < keys.Count; i++)
                _prefabs[keys[i]] = prefabs[i];
        }

        /// <summary>For tests and tools: registers a prefab directly.</summary>
        public void Add(VisualKind kind, string variantId, GameObject prefab) => _prefabs[new VisualKey(kind, variantId)] = prefab;
    }
}
