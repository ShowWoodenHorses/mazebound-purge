using System.Collections.Generic;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Short flash of a character model when hit (display only, instead of a hit clip): <c>_HitFlash</c> of the
    /// <c>Maze/Lit</c> materials is set through a <see cref="MaterialPropertyBlock"/> and fades out. Only renderers
    /// whose materials have the property take part (not blob shadows, effects). Renderers are found once, on creation.
    /// The same property block carries the model's frost (<c>_Frost</c>, theme weather): a second block would be
    /// overwritten by the flash.
    /// </summary>
    public sealed class HitFlash
    {
        private static readonly int FlashId = Shader.PropertyToID("_HitFlash");
        private static readonly int FrostId = Shader.PropertyToID("_Frost");
        private static readonly List<Renderer> Found = new List<Renderer>();
        private static readonly List<Material> Materials = new List<Material>();

        private readonly Renderer[] _renderers;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private Color _color;
        private float _duration;
        private float _left;
        private float _frost;

        private HitFlash(Renderer[] renderers)
        {
            _renderers = renderers;
        }

        public bool IsFlashing => _left > 0f;

        /// <summary>A flash for the model under <paramref name="root"/>, or null if none of its materials can flash.</summary>
        public static HitFlash Create(GameObject root)
        {
            if (root == null) return null;

            var flashing = new List<Renderer>();
            root.GetComponentsInChildren(true, Found);
            foreach (var renderer in Found)
            {
                renderer.GetSharedMaterials(Materials);
                foreach (var material in Materials)
                    if (material != null && material.HasProperty(FlashId))
                    {
                        flashing.Add(renderer);
                        break;
                    }
            }

            Found.Clear();
            Materials.Clear();
            return flashing.Count > 0 ? new HitFlash(flashing.ToArray()) : null;
        }

        /// <summary>Starts the flash again from full strength.</summary>
        public void Trigger(Color color, float duration)
        {
            _color = color;
            _duration = Mathf.Max(duration, 0.01f);
            _left = _duration;
            Apply(_color.a);
        }

        /// <summary>Frost of the model, 0..1 (stays until changed).</summary>
        public void SetFrost(float amount)
        {
            _frost = Mathf.Clamp01(amount);
            Apply(_left > 0f ? _color.a * (_left / _duration) * (_left / _duration) : 0f);
        }

        /// <summary>Ends the flash at once (the model gets its own colours back).</summary>
        public void Stop()
        {
            if (_left <= 0f) return;
            _left = 0f;
            Apply(0f);
        }

        public void Tick(float deltaTime)
        {
            if (_left <= 0f) return;
            _left = Mathf.Max(0f, _left - deltaTime);
            var t = _left / _duration;
            Apply(_color.a * t * t);
        }

        private void Apply(float strength)
        {
            _block.SetColor(FlashId, new Color(_color.r, _color.g, _color.b, strength));
            _block.SetFloat(FrostId, _frost);
            foreach (var renderer in _renderers)
                if (renderer != null)
                    renderer.SetPropertyBlock(_block);
        }
    }
}
