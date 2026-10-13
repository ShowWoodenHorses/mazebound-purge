using System;
using System.Collections.Generic;
using Maze.Core.Grid;
using UnityEngine;

namespace Maze.Core.Visual
{
    /// <summary>A concrete visual decision: which variant and how it is rotated.</summary>
    [Serializable]
    public struct VisualChoice : IEquatable<VisualChoice>
    {
        [SerializeField] private string _variantId;
        [SerializeField] private byte _rotation;

        public VisualChoice(string variantId, int rotation = 0)
        {
            _variantId = variantId;
            _rotation = (byte)(((rotation % 4) + 4) % 4);
        }

        public static readonly VisualChoice None = default;

        public string VariantId => _variantId;

        /// <summary>Clockwise quarter turns around Y (seen from above): 0 = authored orientation facing North.</summary>
        public int Rotation => _rotation;

        public bool IsEmpty => string.IsNullOrEmpty(_variantId);

        public bool Equals(VisualChoice other) => _variantId == other._variantId && _rotation == other._rotation;
        public override bool Equals(object obj) => obj is VisualChoice other && Equals(other);
        public override int GetHashCode() => ((_variantId?.Length ?? 0) * 397) ^ _rotation;
        public override string ToString() => IsEmpty ? "<none>" : $"{_variantId} r{_rotation}";
    }

    [Serializable]
    public sealed class CellVisualOverride
    {
        [SerializeField] private GridPosition _position;
        [SerializeField] private CellLayer _layer;
        [SerializeField] private VisualChoice _choice;

        public CellVisualOverride(GridPosition position, CellLayer layer, VisualChoice choice)
        {
            _position = position;
            _layer = layer;
            _choice = choice;
        }

        public GridPosition Position => _position;
        public CellLayer Layer => _layer;
        public VisualChoice Choice { get => _choice; internal set => _choice = value; }
    }

    /// <summary>
    /// Hand-set placement of the decor in a cell (only with a manual decor override): shift inside the cell,
    /// height above the floor and turn in degrees (clockwise from above, replaces the quarter turn of the choice).
    /// </summary>
    [Serializable]
    public sealed class DecorPlacement
    {
        public const float MaxOffset = 0.5f;
        public const float MinHeight = -0.5f;
        public const float MaxHeight = 2f;

        [SerializeField] private GridPosition _cell;
        [SerializeField] private Vector2 _offset;
        [SerializeField] private float _height;
        [SerializeField] private float _yaw;

        public DecorPlacement(GridPosition cell, Vector2 offset, float height, float yaw)
        {
            _cell = cell;
            Set(offset, height, yaw);
        }

        public GridPosition Cell => _cell;

        /// <summary>Shift from the cell centre: x = East, y = North, cells.</summary>
        public Vector2 Offset => _offset;

        public float Height => _height;

        /// <summary>Degrees, clockwise seen from above, [0, 360).</summary>
        public float Yaw => _yaw;

        public bool IsValid =>
            !float.IsNaN(_offset.x) && !float.IsNaN(_offset.y) && !float.IsNaN(_height) && !float.IsNaN(_yaw) &&
            Mathf.Abs(_offset.x) <= MaxOffset + 1e-4f && Mathf.Abs(_offset.y) <= MaxOffset + 1e-4f &&
            _height >= MinHeight - 1e-4f && _height <= MaxHeight + 1e-4f;

        /// <summary>Clamped to the limits; the yaw is wrapped.</summary>
        internal void Set(Vector2 offset, float height, float yaw)
        {
            _offset = new Vector2(Mathf.Clamp(offset.x, -MaxOffset, MaxOffset), Mathf.Clamp(offset.y, -MaxOffset, MaxOffset));
            _height = Mathf.Clamp(height, MinHeight, MaxHeight);
            _yaw = Mathf.Repeat(yaw, 360f);
        }
    }

    /// <summary>
    /// Hand-set placement of a pickup's view inside its cell (keys, medkits, weapons, map fragments): shift, height
    /// (e.g. on a table) and turn in degrees. Purely visual: the pickup still belongs to its cell. Same limits as
    /// <see cref="DecorPlacement"/>.
    /// </summary>
    [Serializable]
    public sealed class ObjectPlacement
    {
        [SerializeField] private string _entityId;
        [SerializeField] private Vector2 _offset;
        [SerializeField] private float _height;
        [SerializeField] private float _yaw;
        [SerializeField] private bool _onDecor;

        public ObjectPlacement(string entityId, Vector2 offset, float height, float yaw)
        {
            _entityId = entityId;
            Set(offset, height, yaw);
        }

        public string EntityId { get => _entityId; internal set => _entityId = value; }
        public Vector2 Offset => _offset;
        public float Height => _height;

        /// <summary>Degrees, clockwise seen from above, [0, 360).</summary>
        public float Yaw => _yaw;

        /// <summary>
        /// Put on the decor of its cell ("Put on decor"): moved and turned together with it, both ways
        /// (see <c>LevelEditing.SetObjectPlacement</c> / <c>SetDecorPlacement</c>).
        /// </summary>
        public bool OnDecor { get => _onDecor; internal set => _onDecor = value; }

        public bool IsValid =>
            !float.IsNaN(_offset.x) && !float.IsNaN(_offset.y) && !float.IsNaN(_height) && !float.IsNaN(_yaw) &&
            Mathf.Abs(_offset.x) <= DecorPlacement.MaxOffset + 1e-4f && Mathf.Abs(_offset.y) <= DecorPlacement.MaxOffset + 1e-4f &&
            _height >= DecorPlacement.MinHeight - 1e-4f && _height <= DecorPlacement.MaxHeight + 1e-4f;

        internal void Set(Vector2 offset, float height, float yaw)
        {
            var max = DecorPlacement.MaxOffset;
            _offset = new Vector2(Mathf.Clamp(offset.x, -max, max), Mathf.Clamp(offset.y, -max, max));
            _height = Mathf.Clamp(height, DecorPlacement.MinHeight, DecorPlacement.MaxHeight);
            _yaw = Mathf.Repeat(yaw, 360f);
        }
    }

    [Serializable]
    public sealed class ObjectVisualAssignment
    {
        [SerializeField] private string _entityId;
        [SerializeField] private VisualChoice _choice;

        public ObjectVisualAssignment(string entityId, VisualChoice choice)
        {
            _entityId = entityId;
            _choice = choice;
        }

        public string EntityId { get => _entityId; internal set => _entityId = value; }
        public VisualChoice Choice { get => _choice; internal set => _choice = value; }
    }

    /// <summary>
    /// Saved visual design of a level (ТЗ §25). Geometry assignments are stored per cell and layer (row-major,
    /// same indexing as LevelGeometry): every cell has a Floor-layer assignment, wall cells also a Wall-layer one.
    /// Floor cells may have a Decor assignment (optional). Doors and exits themselves are object assignments.
    /// Overrides are the designer's manual choices (per cell and layer) and win over assignments; an empty Decor
    /// override means "no decor here".
    /// </summary>
    [Serializable]
    public sealed class VisualData
    {
        [SerializeField] private VisualChoice[] _floorAssignments = Array.Empty<VisualChoice>();
        [SerializeField] private VisualChoice[] _wallAssignments = Array.Empty<VisualChoice>();

        /// <summary>Auto placed decor per cell; empty entries (and levels made before decor) = no decor.</summary>
        [SerializeField] private VisualChoice[] _decorAssignments = Array.Empty<VisualChoice>();
        [SerializeField] private List<CellVisualOverride> _cellOverrides = new List<CellVisualOverride>();
        [SerializeField] private List<DecorPlacement> _decorPlacements = new List<DecorPlacement>();
        [SerializeField] private List<ObjectPlacement> _objectPlacements = new List<ObjectPlacement>();
        [SerializeField] private List<ObjectVisualAssignment> _objectAssignments = new List<ObjectVisualAssignment>();
        [SerializeField] private List<ObjectVisualAssignment> _objectOverrides = new List<ObjectVisualAssignment>();

        public IReadOnlyList<CellVisualOverride> CellOverrides => _cellOverrides;
        public IReadOnlyList<DecorPlacement> DecorPlacements => _decorPlacements;

        public bool TryGetDecorPlacement(GridPosition cell, out DecorPlacement placement)
        {
            foreach (var entry in _decorPlacements)
                if (entry.Cell == cell)
                {
                    placement = entry;
                    return true;
                }

            placement = null;
            return false;
        }

        internal void SetDecorPlacement(GridPosition cell, Vector2 offset, float height, float yaw)
        {
            if (TryGetDecorPlacement(cell, out var placement))
                placement.Set(offset, height, yaw);
            else
                _decorPlacements.Add(new DecorPlacement(cell, offset, height, yaw));
        }

        internal bool ClearDecorPlacement(GridPosition cell) => _decorPlacements.RemoveAll(p => p.Cell == cell) > 0;

        public IReadOnlyList<ObjectPlacement> ObjectPlacements => _objectPlacements;

        public bool TryGetObjectPlacement(string entityId, out ObjectPlacement placement)
        {
            foreach (var entry in _objectPlacements)
                if (entry.EntityId == entityId)
                {
                    placement = entry;
                    return true;
                }

            placement = null;
            return false;
        }

        internal void SetObjectPlacement(string entityId, Vector2 offset, float height, float yaw)
        {
            if (TryGetObjectPlacement(entityId, out var placement))
                placement.Set(offset, height, yaw);
            else
                _objectPlacements.Add(new ObjectPlacement(entityId, offset, height, yaw));
        }

        internal bool ClearObjectPlacement(string entityId) => _objectPlacements.RemoveAll(p => p.EntityId == entityId) > 0;
        public IReadOnlyList<ObjectVisualAssignment> ObjectAssignments => _objectAssignments;
        public IReadOnlyList<ObjectVisualAssignment> ObjectOverrides => _objectOverrides;

        /// <summary>False if assignments were made for a different level size (or never made).</summary>
        public bool HasCellAssignments(int cellCount) =>
            _floorAssignments.Length == cellCount && _wallAssignments.Length == cellCount;

        public VisualChoice GetCellAssignment(CellLayer layer, int cellIndex)
        {
            var assignments = Layer(layer);
            return cellIndex >= 0 && cellIndex < assignments.Length ? assignments[cellIndex] : VisualChoice.None;
        }

        public bool TryGetCellOverride(GridPosition position, CellLayer layer, out VisualChoice choice)
        {
            foreach (var entry in _cellOverrides)
                if (entry.Position == position && entry.Layer == layer)
                {
                    choice = entry.Choice;
                    return true;
                }

            choice = VisualChoice.None;
            return false;
        }

        public VisualChoice GetObjectAssignment(string entityId) => Find(_objectAssignments, entityId)?.Choice ?? VisualChoice.None;

        public bool TryGetObjectOverride(string entityId, out VisualChoice choice)
        {
            var entry = Find(_objectOverrides, entityId);
            choice = entry?.Choice ?? VisualChoice.None;
            return entry != null;
        }

        internal void ResetCellAssignments(int cellCount)
        {
            _floorAssignments = new VisualChoice[cellCount];
            _wallAssignments = new VisualChoice[cellCount];
            _decorAssignments = new VisualChoice[cellCount];
        }

        /// <summary>Decor assignments are created on demand (levels made before decor have none).</summary>
        internal void SetCellAssignment(CellLayer layer, int cellIndex, VisualChoice choice)
        {
            if (layer == CellLayer.Decor && _decorAssignments.Length != _floorAssignments.Length)
            {
                if (choice.IsEmpty) return;
                var resized = new VisualChoice[_floorAssignments.Length];
                Array.Copy(_decorAssignments, resized, Math.Min(_decorAssignments.Length, resized.Length));
                _decorAssignments = resized;
            }

            Layer(layer)[cellIndex] = choice;
        }

        /// <summary>An empty decor override ("no decor") drops the cell's decor placement.</summary>
        internal void SetCellOverride(GridPosition position, CellLayer layer, VisualChoice choice)
        {
            if (layer == CellLayer.Decor && choice.IsEmpty)
                ClearDecorPlacement(position);

            foreach (var entry in _cellOverrides)
                if (entry.Position == position && entry.Layer == layer)
                {
                    entry.Choice = choice;
                    return;
                }

            _cellOverrides.Add(new CellVisualOverride(position, layer, choice));
        }

        /// <summary>Clearing the decor override also drops its placement (placements belong to manual decor).</summary>
        internal bool ClearCellOverride(GridPosition position, CellLayer layer)
        {
            if (layer == CellLayer.Decor)
                ClearDecorPlacement(position);
            return _cellOverrides.RemoveAll(o => o.Position == position && o.Layer == layer) > 0;
        }

        private VisualChoice[] Layer(CellLayer layer) => layer switch
        {
            CellLayer.Floor => _floorAssignments,
            CellLayer.Wall => _wallAssignments,
            _ => _decorAssignments,
        };

        internal void SetObjectAssignment(string entityId, VisualChoice choice) => Set(_objectAssignments, entityId, choice);

        internal void SetObjectOverride(string entityId, VisualChoice choice) => Set(_objectOverrides, entityId, choice);

        internal bool ClearObjectOverride(string entityId) => _objectOverrides.RemoveAll(o => o.EntityId == entityId) > 0;

        internal void RemoveObject(string entityId)
        {
            _objectAssignments.RemoveAll(o => o.EntityId == entityId);
            _objectOverrides.RemoveAll(o => o.EntityId == entityId);
            ClearObjectPlacement(entityId);
        }

        /// <summary>Keeps the visual design of an object when its id changes.</summary>
        internal void RenameObject(string oldId, string newId)
        {
            foreach (var entry in _objectAssignments)
                if (entry.EntityId == oldId)
                    entry.EntityId = newId;

            foreach (var entry in _objectOverrides)
                if (entry.EntityId == oldId)
                    entry.EntityId = newId;

            foreach (var entry in _objectPlacements)
                if (entry.EntityId == oldId)
                    entry.EntityId = newId;
        }

        /// <summary>Drops assignments and overrides of entities that no longer exist.</summary>
        internal void RetainObjects(ICollection<string> existingEntityIds)
        {
            _objectAssignments.RemoveAll(o => !existingEntityIds.Contains(o.EntityId));
            _objectOverrides.RemoveAll(o => !existingEntityIds.Contains(o.EntityId));
            _objectPlacements.RemoveAll(o => !existingEntityIds.Contains(o.EntityId));
        }

        internal void ClearOverrides()
        {
            _cellOverrides.Clear();
            _decorPlacements.Clear();
            _objectPlacements.Clear();
            _objectOverrides.Clear();
        }

        internal void Clear()
        {
            _floorAssignments = Array.Empty<VisualChoice>();
            _wallAssignments = Array.Empty<VisualChoice>();
            _decorAssignments = Array.Empty<VisualChoice>();
            _objectAssignments.Clear();
            ClearOverrides();
        }

        private static ObjectVisualAssignment Find(List<ObjectVisualAssignment> list, string entityId)
        {
            foreach (var entry in list)
                if (entry.EntityId == entityId)
                    return entry;

            return null;
        }

        private static void Set(List<ObjectVisualAssignment> list, string entityId, VisualChoice choice)
        {
            var entry = Find(list, entityId);
            if (entry != null)
                entry.Choice = choice;
            else
                list.Add(new ObjectVisualAssignment(entityId, choice));
        }
    }
}
