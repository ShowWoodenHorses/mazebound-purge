using System;
using System.Collections.Generic;
using Purge.Core.Grid;
using UnityEngine;

namespace Purge.Presentation.Visual
{
    /// <summary>
    /// Built static geometry of the level: visibility chunks plus the per-cell mask. Visibility only switches
    /// already built geometry between visible and hidden (ТЗ §46–47); it never rebuilds or picks prefabs.
    /// </summary>
    public sealed class LevelGeometryView : IDisposable
    {
        private readonly List<VisibilityChunk> _chunks;

        public LevelGeometryView(Transform root, List<VisibilityChunk> chunks, CellVisibilityMask mask, int missingVisuals,
            SnowdriftMask snowdrifts = null, GroundMask ground = null)
        {
            Ground = snowdrifts == null ? ground : null; // one keyword set: drifts win
            if (snowdrifts != null) ground?.Dispose();
            Root = root;
            _chunks = chunks;
            Mask = mask;
            MissingVisuals = missingVisuals;
            Snowdrifts = snowdrifts;
        }

        public Transform Root { get; }
        public IReadOnlyList<VisibilityChunk> Chunks => _chunks;
        public CellVisibilityMask Mask { get; }

        /// <summary>Soft drift edges of the level; null when it shows no drift floors.</summary>
        public SnowdriftMask Snowdrifts { get; }

        /// <summary>Soft ground edges of the level; null when it has no ground cells or the theme no ground texture.</summary>
        public GroundMask Ground { get; }

        /// <summary>Makes the shader use this level's masks. Only one level is active at a time.</summary>
        public void Bind()
        {
            Mask.Bind();
            Snowdrifts?.Bind();
            Ground?.Bind();
        }

        /// <summary>Cell layers that should have geometry but whose visual or prefab is missing.</summary>
        public int MissingVisuals { get; }

        public void SetCellVisible(GridPosition cell, bool visible) => Mask.SetVisible(cell, visible);

        public void SetAllVisible(bool visible) => Mask.SetAll(visible);

        /// <summary>Uploads the mask and switches off chunks that have no visible cell. Call once per visibility update.</summary>
        public void ApplyVisibility()
        {
            Mask.Apply();
            foreach (var chunk in _chunks)
                chunk.SetVisible(Mask.AnyVisible(chunk.Cells));
        }

        public void Dispose()
        {
            Mask.Dispose();
            Snowdrifts?.Dispose();
            Ground?.Dispose();
            foreach (var chunk in _chunks)
                UnityObjects.Destroy(chunk.Mesh);
            _chunks.Clear();
            if (Root != null)
                UnityObjects.Destroy(Root.gameObject);
        }
    }
}
