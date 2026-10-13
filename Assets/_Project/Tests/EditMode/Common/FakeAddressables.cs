using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Object = UnityEngine.Object;

namespace Maze.Tests.EditMode.Common
{
    /// <summary>In-memory <see cref="IAddressablesService"/>: assets by address (or AssetReference GUID), handles counted per owner.</summary>
    internal sealed class FakeAddressables : IAddressablesService
    {
        public readonly Dictionary<string, Object> Assets = new Dictionary<string, Object>();
        private readonly List<FakeOwner> _owners = new List<FakeOwner>();

        public int OwnerCount => _owners.Count;

        public int ActiveHandleCount
        {
            get
            {
                var count = 0;
                foreach (var owner in _owners) count += owner.HandleCount;
                return count;
            }
        }

        public UniTask<bool> ExistsAsync(string address, CancellationToken cancellation) =>
            UniTask.FromResult(Assets.ContainsKey(address));

        public IAssetOwner CreateOwner(string name)
        {
            var owner = new FakeOwner(this, name);
            _owners.Add(owner);
            return owner;
        }

        private sealed class FakeOwner : IAssetOwner
        {
            private readonly FakeAddressables _service;

            public FakeOwner(FakeAddressables service, string name)
            {
                _service = service;
                Name = name;
            }

            public string Name { get; }
            public int HandleCount { get; private set; }
            public bool IsDisposed { get; private set; }

            public UniTask<T> LoadAsync<T>(string address, CancellationToken cancellation) where T : Object
            {
                HandleCount++;
                if (!_service.Assets.TryGetValue(address, out var asset) || !(asset is T typed))
                    throw new AssetLoadException(address, null);
                return UniTask.FromResult(typed);
            }

            public UniTask<T> LoadAsync<T>(UnityEngine.AddressableAssets.AssetReference reference,
                CancellationToken cancellation) where T : Object =>
                LoadAsync<T>(reference.AssetGUID, cancellation);

            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;
                HandleCount = 0;
                _service._owners.Remove(this);
            }
        }
    }
}
