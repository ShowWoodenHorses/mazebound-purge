using Purge.Core.Content;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Purge.Core.Objects
{
    /// <summary>
    /// The safe dome at the start (ТЗ 7в, «колпак»): monsters do not enter it; bullets, projectiles, blasts and zones do
    /// not cross its edge either way; it heals the player inside. During a wave the player may stay
    /// <see cref="WaveTime"/> s in it, then it pushes them out and goes out until the pause; in the pause — no limit.
    /// Where and how big — the level (<c>LevelSettings.Refuge</c>); one asset (<c>Data/Objects</c>).
    /// </summary>
    [CreateAssetMenu(fileName = "Refuge", menuName = "Purge/Definitions/Refuge")]
    public sealed class RefugeDefinition : ContentDefinition
    {
        [Tooltip("Seconds per wave the player may stay inside.")]
        [SerializeField, Min(0.5f)] private float _waveTime = 10f;

        [Tooltip("Share of the max HP healed per second inside.")]
        [SerializeField, Range(0f, 1f)] private float _healPerSecond = 0.05f;

        [Tooltip("Seconds of pushing the player out when the time is over.")]
        [SerializeField, Min(0.05f)] private float _pushTime = 0.5f;

        [Tooltip("The dome's look (a looping effect, scaled to the radius: radius 1 at scale 1).")]
        [SerializeField] private AssetReferenceGameObject _look;

        public float WaveTime => _waveTime;
        public float HealPerSecond => _healPerSecond;
        public float PushTime => _pushTime;
        public AssetReferenceGameObject Look => _look;

        /// <summary>For tests and tools.</summary>
        internal void Configure(string id, float waveTime, float healPerSecond, float pushTime = 0.5f)
        {
            SetId(id);
            _waveTime = waveTime;
            _healPerSecond = healPerSecond;
            _pushTime = pushTime;
        }

        internal void SetLook(AssetReferenceGameObject look) => _look = look;
    }
}
