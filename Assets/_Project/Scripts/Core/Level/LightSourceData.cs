using System;
using Maze.Core.Grid;
using Maze.Core.Visual;
using UnityEngine;

namespace Maze.Core.Level
{
    /// <summary>
    /// A light source of the level (visual only: gameplay, LOS and AI ignore it). Not a Unity light: its light is
    /// computed over the grid at load (<see cref="Lighting.LightField"/>), walls and closed doors block it.
    /// A wall-mounted light sits in a floor cell, shifted towards the wall (<see cref="Offset"/>).
    /// </summary>
    [Serializable]
    public sealed class LightSourceData
    {
        public const string IdPrefix = "light";

        [SerializeField] private string _id;
        [SerializeField] private GridPosition _cell;
        [SerializeField] private Vector2 _offset;
        [SerializeField] private Color _color = new Color(1f, 0.72f, 0.42f);
        [SerializeField] private float _radius = 4f;
        [SerializeField] private float _intensity = 1f;
        [SerializeField] private float _flicker;
        [SerializeField] private bool _isGenerated;
        [SerializeField] private VisualChoice _visual;

        public LightSourceData(string id, GridPosition cell, Vector2 offset, Color color, float radius, float intensity,
            float flicker, bool isGenerated)
        {
            _id = id;
            _cell = cell;
            _offset = offset;
            _color = color;
            _radius = radius;
            _intensity = intensity;
            _flicker = flicker;
            _isGenerated = isGenerated;
        }

        public string Id { get => _id; internal set => _id = value; }

        /// <summary>Floor cell the light belongs to.</summary>
        public GridPosition Cell { get => _cell; internal set => _cell = value; }

        /// <summary>Shift from the cell centre, cells (each axis within ±0.5).</summary>
        public Vector2 Offset { get => _offset; internal set => _offset = value; }

        /// <summary>Point in grid units (x = East, y = North).</summary>
        public Vector2 Point => new Vector2(_cell.X + _offset.x, _cell.Y + _offset.y);

        public Color Color { get => _color; internal set => _color = value; }

        /// <summary>Cells: the light fades to nothing at this distance.</summary>
        public float Radius { get => _radius; internal set => _radius = value; }

        public float Intensity { get => _intensity; internal set => _intensity = value; }

        /// <summary>0 = steady, 1 = strong flicker (in the shader).</summary>
        public float Flicker { get => _flicker; internal set => _flicker = value; }

        /// <summary>Placed by auto placement (replaced when it reruns); false = placed or edited by hand.</summary>
        public bool IsGenerated { get => _isGenerated; internal set => _isGenerated = value; }

        /// <summary>Saved fixture variant (theme's Light set), e.g. a torch; empty = the set default or none.</summary>
        public VisualChoice Visual { get => _visual; internal set => _visual = value; }
    }
}
