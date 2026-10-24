using Purge.Core.Content;
using Purge.Core.Drops;
using Purge.Core.Level;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Core.Objects
{
    /// <summary>
    /// A kind of chest (ТЗ 7в): opened once per run by Use next to it (<see cref="OpenTime"/>). A common one gives one
    /// item of <see cref="Reward"/> (never nothing); a locked one needs the chest key and gives a weapon of
    /// <see cref="Reward"/>'s rarities; a challenge one first calls <see cref="Champions"/> champions around it and gives
    /// the weapon when they are dead. One asset per <see cref="ChestKind"/> (<c>Data/Objects</c>).
    /// </summary>
    [CreateAssetMenu(fileName = "Chest", menuName = "Purge/Definitions/Chest")]
    public sealed class ChestDefinition : ContentDefinition
    {
        [SerializeField] private ChestKind _kind;

        [Tooltip("Seconds of opening after Use (the player stays next to it).")]
        [SerializeField, Min(0.1f)] private float _openTime = 1f;

        [Tooltip("Drop table of the reward: a common chest rolls an item of it (never nothing), the others a weapon by its rarities.")]
        [SerializeField] private DropTable _reward;

        [Tooltip("Challenge: champions that climb out around the chest.")]
        [SerializeField, Min(0)] private int _champions = 6;

        [Tooltip("Challenge: cells around the chest they climb out in.")]
        [SerializeField, Min(1f)] private float _championRadius = 6f;

        [Tooltip("The model in the cell (display only); a child named *Lid* opens.")]
        [SerializeField] private AssetReferenceGameObject _prefab;

        public ChestKind Kind => _kind;
        public float OpenTime => _openTime;
        public DropTable Reward => _reward;
        public int Champions => _champions;
        public float ChampionRadius => _championRadius;
        public AssetReferenceGameObject Prefab => _prefab;

        /// <summary>The reward is a weapon (locked and challenge chests), not any item of the table.</summary>
        public bool GivesWeapon => _kind != ChestKind.Common;

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, ChestKind kind, DropTable reward, float openTime = 1f, int champions = 6,
            float championRadius = 6f)
        {
            SetId(id);
            _kind = kind;
            _reward = reward;
            _openTime = openTime;
            _champions = champions;
            _championRadius = championRadius;
        }

        internal void SetPrefab(AssetReferenceGameObject prefab) => _prefab = prefab;
        internal void SetReward(DropTable reward) => _reward = reward;
    }
}
