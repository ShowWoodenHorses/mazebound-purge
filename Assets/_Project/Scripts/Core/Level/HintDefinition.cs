using System;
using UnityEngine;

namespace Purge.Core.Level
{
    public enum HintCondition
    {
        SilencedWeapon = 0,
        VisibleMonster = 1,
        ArmoredMonster = 2,
        Champion = 3,
        Chest = 4,
        ChestKey = 5,
        Refuge = 6,
        Barrel = 7,
        Vehicle = 8,
        Boss = 9,
        ClosedArena = 10,
    }

    /// <summary>One optional hint, with a stable save ID, a localized message and a reusable condition.</summary>
    [Serializable]
    public sealed class HintDefinition
    {
        [SerializeField] private string id;
        [SerializeField] private string textKey;
        [SerializeField] private HintCondition condition;
        [SerializeField] private string monsterIdPrefix;
        [SerializeField] private ChestKind chestKind;

        public string Id => id;
        public string TextKey => textKey;
        public HintCondition Condition => condition;
        public string MonsterIdPrefix => monsterIdPrefix;
        public ChestKind ChestKind => chestKind;
    }

    [Serializable]
    public sealed class HintCatalog
    {
        public const string ResourceName = "GameplayHints";
        [SerializeField] private HintDefinition[] hints;

        public static HintDefinition[] LoadDefinitions()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            return (asset != null ? JsonUtility.FromJson<HintCatalog>(asset.text)?.hints : null)
                ?? Array.Empty<HintDefinition>();
        }
    }
}
