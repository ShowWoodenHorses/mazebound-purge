using System;
using System.Collections.Generic;
using Maze.Core.Grid;
using UnityEngine;

namespace Maze.Core.Level
{
    /// <summary>Closed patrol loop: A -> B -> C -> D -> A.</summary>
    [Serializable]
    public sealed class PatrolData
    {
        public const string IdPrefix = "patrol";

        [SerializeField] private string _id;
        [SerializeField] private List<GridPosition> _points = new List<GridPosition>();

        public PatrolData(string id, IEnumerable<GridPosition> points = null)
        {
            _id = id;
            if (points != null)
                _points.AddRange(points);
        }

        public string Id { get => _id; internal set => _id = value; }
        public IReadOnlyList<GridPosition> Points => _points;

        internal List<GridPosition> MutablePoints => _points;
    }
}
