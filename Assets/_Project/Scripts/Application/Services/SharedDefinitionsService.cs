using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Core.Cards;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Visual;

namespace Purge.Application.Services
{
    /// <summary>
    /// Definitions shared by every level (player parameters and look, combat visuals, held weapon looks, the content
    /// catalog that resolves stable ids of saves), loaded once and kept resident
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

        /// <summary>Looks of dropped and thrown items; null until Build Items has made it (they are then not shown).</summary>
        public ItemVisualCatalog ItemVisuals { get; private set; }

        /// <summary>Cards of the run and the experience curve; null until Build Cards has made it (no cards then).</summary>
        public CardDeck Cards { get; private set; }

        /// <summary>Every content asset by stable id (purchases, upgrades and runs in the save refer to them).</summary>
        public ContentCatalog Content { get; private set; }

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            _assets = _addressables.CreateOwner("Application: Shared Definitions");
            (Player, PlayerVisual, CombatVisual, WeaponVisuals, Content) = await UniTask.WhenAll(
                _assets.LoadAsync<PlayerDefinition>(PlayerDefinition.Address, cancellation),
                _assets.LoadAsync<PlayerVisualDefinition>(PlayerVisualDefinition.Address, cancellation),
                _assets.LoadAsync<CombatVisualDefinition>(CombatVisualDefinition.Address, cancellation),
                _assets.LoadAsync<WeaponVisualCatalog>(WeaponVisualCatalog.Address, cancellation),
                _assets.LoadAsync<ContentCatalog>(ContentCatalog.Address, cancellation));
            if (await _addressables.ExistsAsync(ItemVisualCatalog.Address, cancellation))
                ItemVisuals = await _assets.LoadAsync<ItemVisualCatalog>(ItemVisualCatalog.Address, cancellation);
            if (await _addressables.ExistsAsync(CardDeck.Address, cancellation))
                Cards = await _assets.LoadAsync<CardDeck>(CardDeck.Address, cancellation);
        }

        public void Dispose()
        {
            _assets?.Dispose();
            _assets = null;
            Player = null;
            PlayerVisual = null;
            CombatVisual = null;
            WeaponVisuals = null;
            ItemVisuals = null;
            Cards = null;
            Content = null;
        }
    }
}
