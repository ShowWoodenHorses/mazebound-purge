using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Level;
using Purge.Gameplay.Level;
using Purge.Gameplay.Throwing;
using Purge.Gameplay.Weapons;

namespace Purge.Gameplay.Horde
{
    /// <summary>
    /// The combat sandbox of stage 4 (<c>StressTestDefinition.IsSandbox</c>): the player starts with
    /// <c>SandboxAmmo</c> rounds of every ammo type of the content catalog and a full stack of the first sandbox
    /// throwable; <see cref="Refill"/> tops them up (overlay), <see cref="NextThrowable"/> switches the throwable kind.
    /// Inactive on other levels.
    /// </summary>
    public sealed class CombatSandbox : ILevelLoadStep
    {
        private readonly LevelData _level;
        private readonly AmmoStock _ammo;
        private readonly ThrowableSlot _throwables;
        private readonly ContentCatalog _content;
        private readonly List<AmmoDefinition> _types = new List<AmmoDefinition>();

        public CombatSandbox(LevelData level, AmmoStock ammo, ThrowableSlot throwables = null, ContentCatalog content = null)
        {
            _level = level;
            _ammo = ammo;
            _throwables = throwables;
            _content = content;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeSystems;

        public bool IsActive { get; private set; }

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            IsActive = _level.StressTest != null && _level.StressTest.IsSandbox;
            if (IsActive)
            {
                _content?.GetAll(_types);
                Refill();
            }

            return UniTask.CompletedTask;
        }

        /// <summary>Every ammo type back up to the sandbox amount; the throwable stack full (the first kind if empty).</summary>
        public void Refill()
        {
            if (!IsActive) return;
            var amount = _level.StressTest.SandboxAmmo;
            foreach (var type in _types)
                _ammo.Add(type, amount - _ammo.Get(type));

            if (_throwables == null) return;
            var kind = _throwables.IsEmpty ? First() : _throwables.Definition;
            if (kind != null) _throwables.Replace(kind, kind.MaxStack);
        }

        /// <summary>A full stack of the next sandbox throwable kind.</summary>
        public void NextThrowable()
        {
            if (!IsActive || _throwables == null) return;
            var kinds = _level.StressTest.SandboxThrowables;
            if (kinds.Length == 0) return;
            var index = -1;
            for (var i = 0; i < kinds.Length; i++)
                if (_throwables.IsSameKind(kinds[i])) index = i;
            for (var n = 1; n <= kinds.Length; n++)
            {
                var next = kinds[(index + n) % kinds.Length];
                if (next == null) continue;
                _throwables.Replace(next, next.MaxStack);
                return;
            }
        }

        private ThrowableDefinition First()
        {
            foreach (var kind in _level.StressTest.SandboxThrowables)
                if (kind != null) return kind;
            return null;
        }
    }
}
