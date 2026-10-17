using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Level;
using Purge.Gameplay.Level;
using Purge.Gameplay.Weapons;

namespace Purge.Gameplay.Horde
{
    /// <summary>
    /// The combat sandbox of stage 4 (<c>StressTestDefinition.IsSandbox</c>): the player starts with
    /// <c>SandboxAmmo</c> rounds of every ammo type of the content catalog; <see cref="Refill"/> tops them up (overlay).
    /// Inactive on other levels.
    /// </summary>
    public sealed class CombatSandbox : ILevelLoadStep
    {
        private readonly LevelData _level;
        private readonly AmmoStock _ammo;
        private readonly ContentCatalog _content;
        private readonly List<AmmoDefinition> _types = new List<AmmoDefinition>();

        public CombatSandbox(LevelData level, AmmoStock ammo, ContentCatalog content = null)
        {
            _level = level;
            _ammo = ammo;
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

        /// <summary>Every ammo type back up to the sandbox amount.</summary>
        public void Refill()
        {
            if (!IsActive) return;
            var amount = _level.StressTest.SandboxAmmo;
            foreach (var type in _types)
                _ammo.Add(type, amount - _ammo.Get(type));
        }
    }
}
