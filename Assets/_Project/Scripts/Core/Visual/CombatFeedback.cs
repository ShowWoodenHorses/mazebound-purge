using System;
using UnityEngine;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Health bars over damaged zombies and flying numbers (damage to zombies and the player, medkit healing) — display
    /// only, part of <see cref="CombatVisualDefinition"/>. Every colour is here, so it is changed without code. Sizes are
    /// in cells; "up" is the top of the screen (+Z, the camera never turns). The digit glyphs and the material are made by
    /// Maze → Dev → Build Damage Numbers; no material = nothing is shown.
    /// </summary>
    [Serializable]
    public sealed class CombatFeedback
    {
        [Tooltip("Maze/Overhead material with the digit atlas. Made by Build Damage Numbers.")]
        [SerializeField] private Material _material;

        [Header("Health bar (zombies)")]
        [Tooltip("Seconds the bar stays after the last hit, then fades.")]
        [SerializeField, Min(0.1f)] private float _barShowTime = 3f;

        [SerializeField, Min(0.01f)] private float _barFadeTime = 0.25f;
        [SerializeField, Min(0.05f)] private float _barWidth = 0.7f;
        [SerializeField, Min(0.01f)] private float _barHeight = 0.09f;

        [Tooltip("How far above the zombie's centre the bar is, cells toward the top of the screen.")]
        [SerializeField] private float _barOffset = 0.75f;

        [Tooltip("Border of the bar around the background, cells.")]
        [SerializeField, Min(0f)] private float _barBorder = 0.015f;

        [SerializeField] private Color _barBorderColor = new Color(0.925f, 0.902f, 0.839f, 0.75f);
        [SerializeField] private Color _barBackgroundColor = new Color(0.05f, 0.05f, 0.045f, 0.7f);
        [SerializeField] private Color _barFillColor = new Color(0.788f, 0.314f, 0.247f, 1f);

        [Tooltip("The lost part of the bar right after a hit; it then shrinks to the fill.")]
        [SerializeField] private Color _barTrailColor = new Color(0.95f, 0.92f, 0.85f, 0.9f);

        [Tooltip("Seconds the lost part stays before shrinking.")]
        [SerializeField, Min(0f)] private float _barTrailDelay = 0.2f;

        [Tooltip("Seconds the lost part takes to shrink.")]
        [SerializeField, Min(0.01f)] private float _barTrailTime = 0.3f;

        [Header("Numbers")]
        [SerializeField] private Color _zombieDamageColor = new Color(1f, 0.27f, 0.2f, 1f);
        [SerializeField] private Color _playerDamageColor = new Color(1f, 0.2f, 0.15f, 1f);
        [SerializeField] private Color _healColor = new Color(0.42f, 0.86f, 0.36f, 1f);

        [Tooltip("Dark edge around the digits.")]
        [SerializeField] private Color _outlineColor = new Color(0.08f, 0.02f, 0.02f, 0.9f);

        [Tooltip("Edge width, share of the glyph's distance field (0 = none).")]
        [SerializeField, Range(0f, 0.45f)] private float _outlineWidth = 0.22f;

        [Tooltip("Seconds a number lives: appears small, grows and fades.")]
        [SerializeField, Min(0.1f)] private float _numberLifetime = 0.6f;

        [Tooltip("Digit height when it appears, cells.")]
        [SerializeField, Min(0.01f)] private float _numberStartSize = 0.18f;

        [Tooltip("Digit height at its largest, cells.")]
        [SerializeField, Min(0.01f)] private float _numberEndSize = 0.38f;

        [Tooltip("Share of the lifetime spent growing; the rest it fades.")]
        [SerializeField, Range(0.05f, 1f)] private float _numberGrowShare = 0.35f;

        [Tooltip("How far a number rises during its life, cells toward the top of the screen.")]
        [SerializeField] private float _numberRise = 0.3f;

        [Tooltip("Where a number appears above the target's centre, cells toward the top of the screen.")]
        [SerializeField] private float _numberOffset = 0.2f;

        [Tooltip("How far numbers that follow each other quickly at one target are moved aside (and a bit lower), cells.")]
        [SerializeField, Min(0f)] private float _numberSpread = 0.5f;

        [Header("Digit glyphs (Build Damage Numbers)")]
        [SerializeField] private NumberGlyph[] _glyphs = Array.Empty<NumberGlyph>();

        public Material Material { get => _material; internal set => _material = value; }
        public float BarShowTime => _barShowTime;
        public float BarFadeTime => _barFadeTime;
        public float BarWidth => _barWidth;
        public float BarHeight => _barHeight;
        public float BarOffset => _barOffset;
        public float BarBorder => _barBorder;
        public Color BarBorderColor => _barBorderColor;
        public Color BarBackgroundColor => _barBackgroundColor;
        public Color BarFillColor => _barFillColor;
        public Color BarTrailColor => _barTrailColor;
        public float BarTrailDelay => _barTrailDelay;
        public float BarTrailTime => _barTrailTime;
        public Color ZombieDamageColor => _zombieDamageColor;
        public Color PlayerDamageColor => _playerDamageColor;
        public Color HealColor => _healColor;
        public Color OutlineColor => _outlineColor;
        public float OutlineWidth => _outlineWidth;
        public float NumberLifetime => _numberLifetime;
        public float NumberStartSize => _numberStartSize;
        public float NumberEndSize => _numberEndSize;
        public float NumberGrowShare => _numberGrowShare;
        public float NumberRise => _numberRise;
        public float NumberOffset => _numberOffset;
        public float NumberSpread => _numberSpread;
        public NumberGlyph[] Glyphs { get => _glyphs; internal set => _glyphs = value ?? Array.Empty<NumberGlyph>(); }
    }

    /// <summary>
    /// One character of the number atlas. Units: the digit height = 1, origin on the baseline at the pen position.
    /// The quad includes the distance-field padding.
    /// </summary>
    [Serializable]
    public struct NumberGlyph
    {
        public char Character;

        /// <summary>Atlas uv rect of the quad.</summary>
        public Rect Uv;

        /// <summary>Quad corners relative to the pen on the baseline: x min, y min, x max, y max.</summary>
        public Vector4 Quad;

        /// <summary>Pen advance after this character.</summary>
        public float Advance;
    }
}
