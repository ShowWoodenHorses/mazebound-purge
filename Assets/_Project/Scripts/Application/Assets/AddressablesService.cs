using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Core.Common;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Maze.Application.Assets
{
    /// <summary>
    /// Application-wide entry point to Addressables. Assets are never loaded directly: callers create an
    /// <see cref="IAssetOwner"/> with a lifetime (application, level) and dispose it with that lifetime.
    /// </summary>
    public sealed class AddressablesService : IAddressablesService, IDisposable
    {
        private readonly List<AssetOwner> _owners = new List<AssetOwner>();

        public int OwnerCount => _owners.Count;

        public int ActiveHandleCount
        {
            get
            {
                var count = 0;
                foreach (var owner in _owners)
                    count += owner.HandleCount;
                return count;
            }
        }

        public async UniTask<bool> ExistsAsync(string address, CancellationToken cancellation)
        {
            var handle = Addressables.LoadResourceLocationsAsync(address);
            try
            {
                await handle.ToUniTask(cancellationToken: cancellation);
                return handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null && handle.Result.Count > 0;
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        public IAssetOwner CreateOwner(string name)
        {
            var owner = new AssetOwner(this, name);
            _owners.Add(owner);
            return owner;
        }

        public void Dispose()
        {
            // Owners must be disposed by their lifetimes; anything left here is a leak worth reporting.
            foreach (var owner in _owners.ToArray())
            {
                if (owner.HandleCount > 0)
                    GameLog.Warning(LogChannel.Addressables,
                        $"Owner '{owner.Name}' still held {owner.HandleCount} handle(s) at shutdown; releasing.");
                owner.Dispose();
            }
        }

        private void OnOwnerDisposed(AssetOwner owner) => _owners.Remove(owner);

        private sealed class AssetOwner : IAssetOwner
        {
            private readonly AddressablesService _service;
            private readonly List<AsyncOperationHandle> _handles = new List<AsyncOperationHandle>();

            public AssetOwner(AddressablesService service, string name)
            {
                _service = service;
                Name = name;
            }

            public string Name { get; }
            public int HandleCount => _handles.Count;
            public bool IsDisposed { get; private set; }

            public UniTask<T> LoadAsync<T>(string address, CancellationToken cancellation) where T : UnityEngine.Object =>
                LoadAsync<T>(address, address, cancellation);

            public UniTask<T> LoadAsync<T>(AssetReference reference, CancellationToken cancellation) where T : UnityEngine.Object
            {
                if (reference == null || !reference.RuntimeKeyIsValid())
                    throw new AssetLoadException(reference?.AssetGUID ?? "<null>", null);
                return LoadAsync<T>(reference.RuntimeKey, reference.AssetGUID, cancellation);
            }

            private async UniTask<T> LoadAsync<T>(object key, string address, CancellationToken cancellation) where T : UnityEngine.Object
            {
                if (IsDisposed) throw new ObjectDisposedException($"Asset owner '{Name}'");
                cancellation.ThrowIfCancellationRequested();

                var handle = Addressables.LoadAssetAsync<T>(key);
                _handles.Add(handle);

                try
                {
                    await handle.ToUniTask(cancellationToken: cancellation);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    throw new AssetLoadException(address, e);
                }

                // The owner may have been disposed (and the handle released) while loading.
                if (IsDisposed) throw new OperationCanceledException($"Asset owner '{Name}' was disposed while loading '{address}'.");
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                    throw new AssetLoadException(address, handle.OperationException);

                return handle.Result;
            }

            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;

                var released = _handles.Count;
                foreach (var handle in _handles)
                    if (handle.IsValid())
                        Addressables.Release(handle);
                _handles.Clear();

                _service.OnOwnerDisposed(this);
                if (released > 0)
                    GameLog.Info(LogChannel.Addressables, $"Owner '{Name}' released {released} handle(s).");
            }
        }
    }
}
