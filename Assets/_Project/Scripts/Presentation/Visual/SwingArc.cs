using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Melee swing stroke over the attack sector (display only): a flat quad with the <c>Maze/SwingArc</c> shader,
    /// sized to the weapon's reach and cut to its arc. The head of the stroke sweeps across the arc in the first
    /// <see cref="_sweepShare"/> of the effect, then the stroke fades. Swings alternate their direction.
    /// The quad is the child <see cref="_quad"/>: lying flat, 1×1 units, +v = forward (+Z of this object).
    /// </summary>
    public sealed class SwingArc : MonoBehaviour
    {
        private static readonly int HalfArcId = Shader.PropertyToID("_HalfArc");
        private static readonly int HeadId = Shader.PropertyToID("_Head");
        private static readonly int TrailId = Shader.PropertyToID("_Trail");
        private static readonly int DirId = Shader.PropertyToID("_Dir");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        [SerializeField] private Renderer _quad;

        [Tooltip("Share of the effect during which the head sweeps across the arc; the rest is the fade.")]
        [SerializeField, Range(0.1f, 1f)] private float _sweepShare = 0.55f;

        [Tooltip("Trail behind the head, as a share of the arc.")]
        [SerializeField, Range(0.1f, 1f)] private float _trailShare = 0.7f;

        private MaterialPropertyBlock _block;
        private float _halfArc = 50f;
        private float _direction = 1f;

        public Renderer Quad { get => _quad; set => _quad = value; }

        /// <summary>Sizes the stroke: <paramref name="arc"/> degrees wide, <paramref name="reach"/> units long.</summary>
        public void Setup(float arc, float reach, bool leftToRight)
        {
            _halfArc = Mathf.Clamp(arc * 0.5f, 5f, 180f);
            _direction = leftToRight ? 1f : -1f;
            if (_quad != null)
                _quad.transform.localScale = new Vector3(reach * 2f, reach * 2f, 1f);
        }

        public void Animate(float progress)
        {
            if (_quad == null) return;
            _block ??= new MaterialPropertyBlock();

            var sweep = Mathf.Clamp01(progress / _sweepShare);
            sweep = 1f - (1f - sweep) * (1f - sweep); // fast start, slows down at the end
            var fade = 1f - Mathf.Clamp01((progress - _sweepShare) / Mathf.Max(1f - _sweepShare, 0.01f));
            var trail = _halfArc * 2f * _trailShare;

            // The head starts a little before the arc, so the stroke enters it, and ends past it.
            _block.SetFloat(HalfArcId, _halfArc);
            _block.SetFloat(HeadId, _direction * Mathf.Lerp(-_halfArc, _halfArc + trail * 0.3f, sweep));
            _block.SetFloat(TrailId, trail);
            _block.SetFloat(DirId, _direction);
            _block.SetFloat(AlphaId, fade);
            _quad.SetPropertyBlock(_block);
        }
    }
}
