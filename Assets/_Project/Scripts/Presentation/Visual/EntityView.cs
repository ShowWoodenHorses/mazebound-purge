using System;
using System.Collections.Generic;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Visual;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Visual of one level object (door, exit, pickup, zombie). Display only: gameplay systems own the state and
    /// tell the view what to show. Hidden views keep existing (ТЗ §48, §57).
    /// </summary>
    public sealed class EntityView
    {
        public EntityView(string entityId, VisualKind kind, GridPosition cell, GameObject gameObject)
        {
            EntityId = entityId;
            Kind = kind;
            Cell = cell;
            GameObject = gameObject;
        }

        public string EntityId { get; }
        public VisualKind Kind { get; }

        /// <summary>Cell used by visibility. Moving entities change it via <see cref="EntityViewRegistry.Move"/>.</summary>
        public GridPosition Cell { get; internal set; }

        public GameObject GameObject { get; }
        public bool IsVisible => GameObject != null && GameObject.activeSelf;

        public void SetVisible(bool visible)
        {
            if (GameObject != null && GameObject.activeSelf != visible)
                GameObject.SetActive(visible);
        }
    }

    /// <summary>All entity views of the active level, by entity id.</summary>
    public sealed class EntityViewRegistry : IDisposable
    {
        private readonly Dictionary<string, EntityView> _views = new Dictionary<string, EntityView>();
        private readonly List<EntityView> _ordered = new List<EntityView>();

        public int Count => _ordered.Count;
        public IReadOnlyList<EntityView> All => _ordered;

        /// <summary>A view was added (e.g. a zombie spawned after the level loaded).</summary>
        public event Action<EntityView> Added;

        /// <summary>A view moved to another cell.</summary>
        public event Action<EntityView> Moved;

        public void Add(EntityView view)
        {
            if (_views.ContainsKey(view.EntityId))
                throw new ArgumentException($"Entity view '{view.EntityId}' already exists.", nameof(view));

            _views.Add(view.EntityId, view);
            _ordered.Add(view);
            Added?.Invoke(view);
        }

        public bool TryGet(string entityId, out EntityView view) => _views.TryGetValue(entityId, out view);

        /// <summary>Updates the cell of a moving entity's view, so visibility follows it.</summary>
        public void Move(EntityView view, GridPosition cell)
        {
            if (view.Cell == cell) return;
            view.Cell = cell;
            Moved?.Invoke(view);
        }

        /// <summary>Removes and destroys the view (e.g. a collected pickup).</summary>
        public bool Remove(string entityId)
        {
            if (!_views.TryGetValue(entityId, out var view))
                return false;

            _views.Remove(entityId);
            _ordered.Remove(view);
            UnityObjects.Destroy(view.GameObject);
            return true;
        }

        public void Dispose()
        {
            foreach (var view in _ordered)
                UnityObjects.Destroy(view.GameObject);
            _ordered.Clear();
            _views.Clear();
        }
    }

    /// <summary>Instantiates the exact saved visual of an object (ТЗ §43). Never chooses a variant itself.</summary>
    public sealed class EntityViewFactory
    {
        private readonly IVisualPrefabs _prefabs;

        public EntityViewFactory(IVisualPrefabs prefabs)
        {
            _prefabs = prefabs;
        }

        /// <summary>Null when the object has no visual or its prefab is missing.</summary>
        public EntityView Create(LevelData level, LevelEntityData entity, Transform parent)
        {
            if (!VisualKinds.TryGetForEntity(entity, out var kind, out _))
                return null;

            // A placeable pickup may be shifted / lifted (e.g. on a table) / turned by the designer.
            var choice = VisualResolver.ResolveObject(level, entity);
            VisualResolver.ResolveObjectPose(level, entity, choice, out var offset, out var yaw);
            return Create(entity.Id, kind, choice, entity.Position, parent, offset, yaw);
        }

        /// <summary>At the cell centre with the choice's quarter turn (dropped items, zombies).</summary>
        public EntityView Create(string entityId, VisualKind kind, VisualChoice choice, GridPosition cell, Transform parent) =>
            Create(entityId, kind, choice, cell, parent, Vector3.zero, 90f * choice.Rotation);

        public EntityView Create(string entityId, VisualKind kind, VisualChoice choice, GridPosition cell, Transform parent,
            Vector3 offset, float yaw)
        {
            var prefab = choice.IsEmpty ? null : _prefabs.Get(kind, choice.VariantId);
            if (prefab == null)
                return null;

            var instance = UnityEngine.Object.Instantiate(prefab, parent);
            instance.name = entityId;
            instance.transform.localPosition = cell.ToWorld() + offset;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return new EntityView(entityId, kind, cell, instance);
        }
    }
}
