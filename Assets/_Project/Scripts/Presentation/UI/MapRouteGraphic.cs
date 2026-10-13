using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// The route hint on the map screen: a thick polyline with an outline over the map image (stretched over it).
    /// Points are in map units (cells; the map is <see cref="SetRoute"/>'s size in cells). Opaque colours: the outline is
    /// drawn first and the line over it, square joints fill the 90° turns of a grid path.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MapRouteGraphic : MaskableGraphic
    {
        private readonly List<Vector2> _points = new List<Vector2>();
        private Vector2 _mapSize;
        private float _width;
        private float _outline;
        private float _minWidth;
        private Color _lineColor;
        private Color _outlineColor;

        public int PointCount => _points.Count;

        /// <param name="width">Line width, cells (outline excluded).</param>
        /// <param name="outline">Outline on each side, cells.</param>
        /// <param name="minWidth">Smallest line width on screen, canvas units (big levels have tiny cells).</param>
        public void SetRoute(IReadOnlyList<Vector2> points, Vector2 mapSize, float width, float outline, float minWidth,
            Color lineColor, Color outlineColor)
        {
            _points.Clear();
            if (points != null)
                for (var i = 0; i < points.Count; i++) _points.Add(points[i]);
            _mapSize = mapSize;
            _width = width;
            _outline = outline;
            _minWidth = minWidth;
            _lineColor = lineColor;
            _outlineColor = outlineColor;
            raycastTarget = false;
            SetVerticesDirty();
        }

        public void ClearRoute()
        {
            if (_points.Count == 0) return;
            _points.Clear();
            SetVerticesDirty();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_points.Count < 2 || _mapSize.x <= 0f || _mapSize.y <= 0f) return;

            var rect = rectTransform.rect;
            var cell = rect.width / _mapSize.x;
            var width = Mathf.Max(_width * cell, _minWidth);
            var outline = _outline * cell * (width / Mathf.Max(_width * cell, 1e-4f));
            Strip(vh, rect, width * 0.5f + outline, _outlineColor * color);
            Strip(vh, rect, width * 0.5f, _lineColor * color);
        }

        private void Strip(VertexHelper vh, Rect rect, float half, Color32 tint)
        {
            for (var i = 1; i < _points.Count; i++)
            {
                var a = ToLocal(rect, _points[i - 1]);
                var b = ToLocal(rect, _points[i]);
                var along = b - a;
                if (along.sqrMagnitude < 1e-6f) continue;
                along.Normalize();
                var side = new Vector2(-along.y, along.x) * half;
                // Extended by half the width at both ends: square joints and caps.
                Quad(vh, a - along * half + side, a - along * half - side, b + along * half - side, b + along * half + side, tint);
            }
        }

        private Vector2 ToLocal(Rect rect, Vector2 point) =>
            new Vector2(rect.xMin + point.x / _mapSize.x * rect.width, rect.yMin + point.y / _mapSize.y * rect.height);

        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
        {
            var start = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero);
            vh.AddVert(b, tint, Vector2.zero);
            vh.AddVert(c, tint, Vector2.zero);
            vh.AddVert(d, tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
