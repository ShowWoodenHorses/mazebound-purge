using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Application.Services;
using Maze.Core.Level;

namespace Maze.Application.Levels
{
    public interface ILevelCatalog
    {
        /// <summary>The game's levels in order: the menu, progress and "next level" use only these.</summary>
        IReadOnlyList<LevelCatalogEntry> Levels { get; }

        /// <summary>Development levels (editor and development builds only; empty in release builds).</summary>
        IReadOnlyList<LevelCatalogEntry> DevLevels { get; }

        /// <summary>The tutorial level (its own menu button, outside the numbered levels); null when there is none.</summary>
        LevelCatalogEntry Tutorial { get; }

        /// <summary>A game, tutorial or development level; null when it is in neither catalog.</summary>
        LevelCatalogEntry Find(string levelId);
    }

    /// <summary>
    /// Loads the Addressable <see cref="LevelCatalog"/> once and keeps it resident for the application lifetime. In the
    /// editor and development builds also the development catalog (<see cref="LevelCatalog.DevAddress"/>), if built.
    /// </summary>
    public sealed class LevelCatalogService : ILevelCatalog, IApplicationService, IDisposable
    {
        private readonly IAddressablesService _addressables;
        private readonly bool _loadDevLevels;
        private IAssetOwner _assets;
        private LevelCatalog _catalog;
        private LevelCatalog _devCatalog;

        public LevelCatalogService(IAddressablesService addressables)
        {
            _addressables = addressables;
            _loadDevLevels = UnityEngine.Debug.isDebugBuild;
        }

        public string Name => "Level Catalog";

        public IReadOnlyList<LevelCatalogEntry> Levels =>
            _catalog != null ? _catalog.Levels : (IReadOnlyList<LevelCatalogEntry>)Array.Empty<LevelCatalogEntry>();

        public IReadOnlyList<LevelCatalogEntry> DevLevels =>
            _devCatalog != null ? _devCatalog.Levels : (IReadOnlyList<LevelCatalogEntry>)Array.Empty<LevelCatalogEntry>();

        public LevelCatalogEntry Tutorial => _catalog != null ? _catalog.Tutorial : null;

        public LevelCatalogEntry Find(string levelId) =>
            (_catalog != null ? _catalog.Find(levelId) : null) ?? (_devCatalog != null ? _devCatalog.Find(levelId) : null);

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            _assets = _addressables.CreateOwner("Application: Level Catalog");
            _catalog = await _assets.LoadAsync<LevelCatalog>(LevelCatalog.Address, cancellation);
            if (_loadDevLevels && await _addressables.ExistsAsync(LevelCatalog.DevAddress, cancellation))
                _devCatalog = await _assets.LoadAsync<LevelCatalog>(LevelCatalog.DevAddress, cancellation);
        }

        public void Dispose()
        {
            _assets?.Dispose();
            _assets = null;
            _catalog = null;
            _devCatalog = null;
        }
    }
}
