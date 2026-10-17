using System;
using Purge.Core.Definitions;

namespace Purge.Gameplay.Throwing
{
    /// <summary>
    /// The player's throwable slot (ТЗ: стопка — тип и количество): one kind at a time, at most its
    /// <see cref="ThrowableDefinition.MaxStack"/>. The same kind picked up adds to the stack; another kind replaces it
    /// (<see cref="Replace"/>: the old stack is dropped).
    /// </summary>
    public sealed class ThrowableSlot
    {
        public ThrowableDefinition Definition { get; private set; }
        public int Count { get; private set; }
        public bool IsEmpty => Definition == null || Count <= 0;

        /// <summary>After the kind or the count changed.</summary>
        public event Action Changed;

        public bool IsSameKind(ThrowableDefinition definition) =>
            definition != null && Definition != null &&
            (Definition == definition || !string.IsNullOrEmpty(definition.Id) && Definition.Id == definition.Id);

        /// <summary>How many of <paramref name="definition"/> the slot would take now (another kind: none).</summary>
        public int Room(ThrowableDefinition definition)
        {
            if (definition == null) return 0;
            if (IsEmpty) return definition.MaxStack;
            return IsSameKind(definition) ? Math.Max(0, Definition.MaxStack - Count) : 0;
        }

        /// <summary>Adds up to the room left (empty slot: becomes this kind); returns how many were taken.</summary>
        public int Add(ThrowableDefinition definition, int count)
        {
            var taken = Math.Min(Room(definition), Math.Max(0, count));
            if (taken <= 0) return 0;
            if (IsEmpty) Definition = definition;
            Count += taken;
            Changed?.Invoke();
            return taken;
        }

        /// <summary>Puts another stack in; returns the old one (kind, count; null kind = nothing).</summary>
        public (ThrowableDefinition Definition, int Count) Replace(ThrowableDefinition definition, int count)
        {
            var old = IsEmpty ? (null, 0) : (Definition, Count);
            Definition = definition;
            Count = definition != null ? Math.Min(Math.Max(0, count), definition.MaxStack) : 0;
            Changed?.Invoke();
            return old;
        }

        /// <summary>Takes one for a throw; false when empty.</summary>
        public bool TakeOne()
        {
            if (IsEmpty) return false;
            Count--;
            if (Count <= 0)
            {
                Count = 0;
                Definition = null;
            }

            Changed?.Invoke();
            return true;
        }
    }
}
