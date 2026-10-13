using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;

namespace Maze.Application.Assets
{
    /// <summary>
    /// Owner of Addressables handles (ТЗ §84). Every handle is registered in its owner the moment it is
    /// created, so cancellation or failure can never lose it. Dispose releases everything the owner loaded.
    /// </summary>
    public interface IAssetOwner : IDisposable
    {
        string Name { get; }
        int HandleCount { get; }
        bool IsDisposed { get; }

        UniTask<T> LoadAsync<T>(string address, CancellationToken cancellation) where T : UnityEngine.Object;

        UniTask<T> LoadAsync<T>(AssetReference reference, CancellationToken cancellation) where T : UnityEngine.Object;
    }

    public interface IAddressablesService
    {
        /// <summary>Handles held by all live owners. Used to detect leaks.</summary>
        int ActiveHandleCount { get; }

        int OwnerCount { get; }

        IAssetOwner CreateOwner(string name);

        /// <summary>True when the address exists in the loaded content catalogs (nothing is loaded).</summary>
        UniTask<bool> ExistsAsync(string address, CancellationToken cancellation);
    }

    public sealed class AssetLoadException : Exception
    {
        public AssetLoadException(string address, Exception inner)
            : base($"Failed to load Addressable '{address}'" + (inner != null ? ": " + inner.Message : "."), inner)
        {
            Address = address;
        }

        public string Address { get; }
    }
}
