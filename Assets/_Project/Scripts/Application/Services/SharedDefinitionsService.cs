using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Core.Definitions;
using Maze.Core.Visual;

namespace Maze.Application.Services
{
    /// <summary>
    /// Definitions shared by every level (player parameters and look, combat visuals, held weapon looks), loaded once and kept resident
    /// for the application lifetime (ТЗ §84: shared assets may stay resident).
    /// </summary>
    public sealed class SharedDefinitionsService : IApplicationService, IDisposable
    {
        private readonly IAddressablesService _addressables;
        private IAssetOwner _assets;

        public SharedDefinitionsService(IAddressablesService addressables)
        {
            _addressables = addressables;
        }

        public string Name => "Shared Definitions";

        public PlayerDefinition Player { get; private set; }
        public PlayerVisualDefinition PlayerVisual { get; private set; }
        public CombatVisualDefinition CombatVisual { get; private set; }
        public WeaponVisualCatalog WeaponVisuals { get; private set; }

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            _assets = _addressables.CreateOwner("Application: Shared Definitions");
            (Player, PlayerVisual, CombatVisual, WeaponVisuals) = await UniTask.WhenAll(
                _assets.LoadAsync<PlayerDefinition>(PlayerDefinition.Address, cancellation),
                _assets.LoadAsync<PlayerVisualDefinition>(PlayerVisualDefinition.Address, cancellation),
                _assets.LoadAsync<CombatVisualDefinition>(CombatVisualDefinition.Address, cancellation),
                _assets.LoadAsync<WeaponVisualCatalog>(WeaponVisualCatalog.Address, cancellation));
        }

        public void Dispose()
        {
            _assets?.Dispose();
            _assets = null;
            Player = null;
            PlayerVisual = null;
            CombatVisual = null;
            WeaponVisuals = null;
        }
    }
}
