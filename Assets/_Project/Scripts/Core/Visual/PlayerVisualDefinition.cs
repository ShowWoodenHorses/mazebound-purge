using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Maze.Core.Visual
{
    /// <summary>
    /// Look of the player, Addressable at <see cref="Address"/>. The prefab is display only (ТЗ §79): pivot at the
    /// feet, facing +Z. An optional Animator receives the float parameter "Speed" (0..1) from gameplay state.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerVisual", menuName = "Maze/Visual/Player Visual")]
    public sealed class PlayerVisualDefinition : ScriptableObject
    {
        public const string Address = "Player/Visual";

        [SerializeField] private AssetReferenceGameObject _prefab;

        public AssetReferenceGameObject Prefab { get => _prefab; internal set => _prefab = value; }
    }
}
