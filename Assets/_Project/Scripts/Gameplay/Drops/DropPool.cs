using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Drops;
using Purge.Core.Level;
using Purge.Gameplay.Level;

namespace Purge.Gameplay.Drops
{
    /// <summary>
    /// What monsters of this level may drop (ТЗ: в дроп попадает купленное — stage 10): the table, the weapons and
    /// throwables of the pool, the ammo types and medkits. Now — the combat sandbox's lists (its stress test settings);
    /// a level without them drops nothing. Known at load (stage InitializeSystems) so the views load only these items'
    /// looks.
    /// </summary>
    public sealed class DropPool : ILevelLoadStep
    {
        private readonly LevelData _level;
        private readonly ContentCatalog _content;
        private readonly List<WeaponDefinition> _weapons = new List<WeaponDefinition>();
        private readonly List<ThrowableDefinition> _throwables = new List<ThrowableDefinition>();
        private readonly List<AmmoDefinition> _ammo = new List<AmmoDefinition>();
        private readonly List<MedkitDefinition> _medkits = new List<MedkitDefinition>();
        private readonly List<AmmoDefinition> _allAmmo = new List<AmmoDefinition>();

        public DropPool(LevelData level, ContentCatalog content = null)
        {
            _level = level;
            _content = content;
        }

        public LevelLoadStage Stage => LevelLoadStage.InitializeSystems;

        /// <summary>Null: monsters drop nothing.</summary>
        public DropTable Table { get; private set; }

        public IReadOnlyList<WeaponDefinition> Weapons => _weapons;
        public IReadOnlyList<ThrowableDefinition> Throwables => _throwables;

        /// <summary>Every ammo type the pool's weapons use (and the base pistol's, if any).</summary>
        public IReadOnlyList<AmmoDefinition> AmmoTypes => _ammo;

        public IReadOnlyList<MedkitDefinition> Medkits => _medkits;

        public UniTask ExecuteAsync(CancellationToken cancellation)
        {
            var settings = _level.StressTest;
            Configure(settings != null && settings.IsSandbox ? settings.Drop : null,
                settings != null ? settings.SandboxWeapons : null, settings != null ? settings.SandboxThrowables : null);
            return UniTask.CompletedTask;
        }

        /// <summary>Tests and tools: the pool from these lists.</summary>
        public void Configure(DropTable table, IEnumerable<WeaponDefinition> weapons, IEnumerable<ThrowableDefinition> throwables)
        {
            Table = table;
            _weapons.Clear();
            _throwables.Clear();
            _ammo.Clear();
            _medkits.Clear();
            if (table == null) return;

            if (weapons != null)
                foreach (var weapon in weapons)
                    // The base pistol (infinite reserve) never lies on the ground.
                    if (weapon != null && !_weapons.Contains(weapon) && !(weapon.UsesAmmo && weapon.Ammo.InfiniteReserve))
                    {
                        _weapons.Add(weapon);
                        var ammo = weapon.UsesAmmo && !weapon.Ammo.InfiniteReserve ? weapon.Ammo.Ammo : null;
                        if (ammo != null && !_ammo.Contains(ammo)) _ammo.Add(ammo);
                    }

            if (throwables != null)
                foreach (var throwable in throwables)
                    if (throwable != null && !_throwables.Contains(throwable))
                        _throwables.Add(throwable);

            foreach (var medkit in table.Medkits)
                if (medkit?.Medkit != null && !_medkits.Contains(medkit.Medkit))
                    _medkits.Add(medkit.Medkit);
        }

        /// <summary>Every item kind the pool can drop (their looks are loaded with the level).</summary>
        public void GetItems(List<ContentDefinition> result)
        {
            result.Clear();
            if (Table == null) return;
            result.AddRange(_weapons);
            result.AddRange(_throwables);
            result.AddRange(_ammo);
            // Any type may drop for a weapon in the slots (one from the level, not from the pool).
            if (_content != null)
            {
                _allAmmo.Clear();
                _content.GetAll(_allAmmo);
                foreach (var ammo in _allAmmo)
                    if (!_ammo.Contains(ammo)) result.Add(ammo);
            }

            result.AddRange(_medkits);
        }
    }
}
