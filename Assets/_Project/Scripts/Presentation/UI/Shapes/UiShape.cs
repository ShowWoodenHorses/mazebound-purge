using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI.Shapes
{
    /// <summary>
    /// Thin-line UI shape (rect, capsule/circle, annular sector) drawn by the <c>Maze/UIShape</c> shader as a signed
    /// distance field — crisp at any DPI and scale, one quad per shape. Fill and stroke colours travel in vertex
    /// channels, so all shapes share one material and batch together; <see cref="Graphic.color"/> stays a tint over
    /// the whole shape (button transitions, CanvasGroup fades). Clicks count only inside the shape (plus
    /// <see cref="HitPadding"/>, which also widens the rect the raycaster tests first), not anywhere in its rect.
    /// The shape is clipped to its rect (+ a pixel for anti-aliasing): size a sector's rect with
    /// <see cref="UiShapeMath.SectorBounds"/>.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiShape : MaskableGraphic, ICanvasRaycastFilter
    {
        private const AdditionalCanvasShaderChannels Channels =
            AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3;

        /// <summary>Extra geometry around the rect so the anti-aliased edge is not cut (canvas units).</summary>
        private const float Padding = 2f;

        [SerializeField] private UiShapeKind _kind = UiShapeKind.Rect;

        [Tooltip("Inside colour (sRGB). Alpha 0 = outline only.")]
        [SerializeField] private Color _fill = new Color(0.047f, 0.047f, 0.043f, 0.45f);

        [Tooltip("Outline colour (sRGB). Drawn inside the shape's edge.")]
        [SerializeField] private Color _stroke = new Color(0.925f, 0.902f, 0.839f, 0.72f);

        [Tooltip("Outline width, canvas units. 0 = no outline.")]
        [SerializeField, Min(0f)] private float _strokeWidth = 1.5f;

        [Tooltip("Rect: corner radius; Arrow: length of the tip; canvas units.")]
        [SerializeField, Min(0f)] private float _cornerRadius;

        [Tooltip("Sector: centre in the rect, 0..1 (1, 0 = bottom-right corner).")]
        [SerializeField] private Vector2 _center = new Vector2(0.5f, 0.5f);

        [Tooltip("Sector: inner radius, canvas units. 0 = pie.")]
        [SerializeField, Min(0f)] private float _innerRadius;

        [Tooltip("Sector: outer radius, canvas units.")]
        [SerializeField, Min(0f)] private float _outerRadius = 50f;

        [Tooltip("Sector: start angle, degrees; 0 = right, counter-clockwise.")]
        [SerializeField] private float _startAngle;

        [Tooltip("Sector: sweep, degrees; 360 = full ring.")]
        [SerializeField, Range(0f, 360f)] private float _sweep = 360f;

        [Tooltip("Clicks count this far outside the shape too, canvas units.")]
        [SerializeField] private float _hitPadding;

        public UiShapeKind Kind
        {
            get => _kind;
            set => Set(ref _kind, value);
        }

        public Color Fill
        {
            get => _fill;
            set => Set(ref _fill, value);
        }

        public Color Stroke
        {
            get => _stroke;
            set => Set(ref _stroke, value);
        }

        public float StrokeWidth
        {
            get => _strokeWidth;
            set => Set(ref _strokeWidth, Mathf.Max(0f, value));
        }

        public float CornerRadius
        {
            get => _cornerRadius;
            set => Set(ref _cornerRadius, Mathf.Max(0f, value));
        }

        public Vector2 Center
        {
            get => _center;
            set => Set(ref _center, value);
        }

        public float InnerRadius
        {
            get => _innerRadius;
            set => Set(ref _innerRadius, Mathf.Max(0f, value));
        }

        public float OuterRadius
        {
            get => _outerRadius;
            set => Set(ref _outerRadius, Mathf.Max(0f, value));
        }

        public float StartAngle
        {
            get => _startAngle;
            set => Set(ref _startAngle, value);
        }

        public float Sweep
        {
            get => _sweep;
            set => Set(ref _sweep, Mathf.Clamp(value, 0f, 360f));
        }

        public float HitPadding
        {
            get => _hitPadding;
            set
            {
                _hitPadding = value;
                SyncRaycastPadding();
            }
        }

        /// <summary>
        /// The raycaster tests the rect (minus <see cref="Graphic.raycastPadding"/>) before the shape: a negative
        /// padding lets touches outside the rect reach <see cref="IsRaycastLocationValid"/>.
        /// </summary>
        private void SyncRaycastPadding()
        {
            var outside = -Mathf.Max(0f, _hitPadding);
            raycastPadding = new Vector4(outside, outside, outside, outside);
        }

        /// <summary>Sets a sector at once (one mesh rebuild).</summary>
        public void SetSector(Vector2 center, float inner, float outer, float startAngle, float sweep)
        {
            _kind = UiShapeKind.Sector;
            _center = center;
            _innerRadius = Mathf.Max(0f, inner);
            _outerRadius = Mathf.Max(0f, outer);
            _startAngle = startAngle;
            _sweep = Mathf.Clamp(sweep, 0f, 360f);
            SetVerticesDirty();
        }

        /// <summary>Signed distance from a point in this rect's local space to the shape's edge (negative inside).</summary>
        public float Distance(Vector2 local)
        {
            var rect = rectTransform.rect;
            switch (_kind)
            {
                case UiShapeKind.Sector:
                    var center = rect.min + Vector2.Scale(_center, rect.size);
                    return UiShapeMath.Sector(local - center, _innerRadius, _outerRadius, _startAngle, _sweep);
                case UiShapeKind.Capsule:
                    return UiShapeMath.RoundedRect(local - rect.center, rect.size * 0.5f, float.MaxValue);
                case UiShapeKind.Arrow:
                    return UiShapeMath.Arrow(local - rect.center, rect.size * 0.5f, _cornerRadius);
                default:
                    return UiShapeMath.RoundedRect(local - rect.center, rect.size * 0.5f, _cornerRadius);
            }
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera,
                       out var local) &&
                   Distance(local) <= _hitPadding;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnableChannels();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnableChannels();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            var drawn = Rect.MinMaxRect(rect.xMin - Padding, rect.yMin - Padding, rect.xMax + Padding,
                rect.yMax + Padding);

            Vector4 shape;
            Vector2 origin;
            var turn = false;
            switch (_kind)
            {
                case UiShapeKind.Sector:
                    origin = rect.min + Vector2.Scale(_center, rect.size);
                    var half = UiShapeMath.HalfSweep(_sweep);
                    shape = new Vector4(_innerRadius, _outerRadius, half.x, half.y);
                    turn = true;
                    break;
                case UiShapeKind.Capsule:
                    origin = rect.center;
                    shape = new Vector4(rect.width * 0.5f, rect.height * 0.5f,
                        Mathf.Min(rect.width, rect.height) * 0.5f, 0f);
                    break;
                case UiShapeKind.Arrow:
                    origin = rect.center;
                    shape = new Vector4(rect.width * 0.5f, rect.height * 0.5f, Mathf.Clamp(_cornerRadius, 0f, rect.width), 0f);
                    break;
                default:
                    origin = rect.center;
                    shape = new Vector4(rect.width * 0.5f, rect.height * 0.5f,
                        Mathf.Min(_cornerRadius, Mathf.Min(rect.width, rect.height) * 0.5f), 0f);
                    break;
            }

            var kind = _kind == UiShapeKind.Sector ? 1f : _kind == UiShapeKind.Arrow ? 2f : 0f;
            Color32 tint = color;
            AddCorner(vh, new Vector2(drawn.xMin, drawn.yMin));
            AddCorner(vh, new Vector2(drawn.xMin, drawn.yMax));
            AddCorner(vh, new Vector2(drawn.xMax, drawn.yMax));
            AddCorner(vh, new Vector2(drawn.xMax, drawn.yMin));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);

            void AddCorner(VertexHelper helper, Vector2 position)
            {
                var p = position - origin;
                if (turn) p = UiShapeMath.ToSectorFrame(p, _startAngle, _sweep);
                var vertex = UIVertex.simpleVert;
                vertex.position = position;
                vertex.color = tint;
                vertex.uv0 = new Vector4(p.x, p.y, kind, _strokeWidth);
                vertex.uv1 = _fill;
                vertex.uv2 = _stroke;
                vertex.uv3 = shape;
                helper.AddVert(vertex);
            }
        }

        private void EnableChannels()
        {
            var target = canvas;
            if (target != null && (target.additionalShaderChannels & Channels) != Channels)
            {
                target.additionalShaderChannels |= Channels;
            }
        }

        private void Set<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            SetVerticesDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            EnableChannels();
            SyncRaycastPadding();
        }
#endif
    }
}
