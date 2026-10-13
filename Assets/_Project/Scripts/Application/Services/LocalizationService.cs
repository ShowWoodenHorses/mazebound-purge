using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Assets;
using Maze.Application.Platform;
using Maze.Core.Common;
using Maze.Core.Localization;
using UnityEngine;

namespace Maze.Application.Services
{
    /// <summary>
    /// Texts of the game by key in the chosen language. The languages come from <see cref="LanguageCatalog"/>; only
    /// the chosen language's <see cref="LocalizationTable"/> is loaded (its own asset owner, released on a switch).
    /// The first start picks the system language when it is supported, else the base language, and saves the choice
    /// (<see cref="SettingsService.Language"/>). <see cref="LanguageChanged"/> is raised once a table is loaded (at
    /// start-up and after a switch); shown texts take their strings then — nothing is polled per frame. Untranslated keys show the base
    /// language's text, in development builds the key itself (to see the gap); unknown keys show the key.
    /// Must be initialized after <see cref="SettingsService"/>.
    /// </summary>
    public sealed class LocalizationService : IApplicationService, IDisposable
    {
        private readonly IAddressablesService _addressables;
        private readonly SettingsService _settings;
        private readonly HashSet<string> _reportedKeys = new HashSet<string>();
        private IAssetOwner _catalogAssets;
        private IAssetOwner _tableAssets;
        private LanguageCatalog _catalog;
        private LocalizationTable _table;
        private CancellationTokenSource _switch;

        private readonly PlatformService _platform;

        public LocalizationService(IAddressablesService addressables, SettingsService settings, PlatformService platform = null)
        {
            _platform = platform;
            _addressables = addressables;
            _settings = settings;
        }

        public string Name => "Localization";

        /// <summary>Raised after the texts of a language were loaded: at start-up and on every switch.</summary>
        public event Action LanguageChanged;

        /// <summary>Languages in the order of the source table (the first is the base one).</summary>
        public IReadOnlyList<LanguageEntry> Languages =>
            _catalog != null ? _catalog.Languages : (IReadOnlyList<LanguageEntry>)Array.Empty<LanguageEntry>();

        /// <summary>Index of the shown language in <see cref="Languages"/>; -1 before initialization.</summary>
        public int LanguageIndex { get; private set; } = -1;

        /// <summary>Code of the shown language ("en"); null before initialization.</summary>
        public string Language => LanguageIndex >= 0 ? Languages[LanguageIndex].Code : null;

        /// <summary>Untranslated keys show the key instead of the base-language text (development builds; tests).</summary>
        public bool ShowMissingKeys { get; set; } = Debug.isDebugBuild;

        /// <summary>Overridable system language (tests).</summary>
        public SystemLanguage SystemLanguage { get; set; } = UnityEngine.Application.systemLanguage;

        public async UniTask InitializeAsync(CancellationToken cancellation)
        {
            _catalogAssets = _addressables.CreateOwner("Application: Languages");
            _catalog = await _catalogAssets.LoadAsync<LanguageCatalog>(LanguageCatalog.Address, cancellation);
            var index = _catalog.Choose(_settings.Language, _platform?.Language, SystemLanguage);
            if (index < 0) throw new InvalidOperationException("The language catalog is empty: run Maze → Dev → Build Localization.");

            await LoadAsync(index, cancellation);
            GameLog.Info(LogChannel.Localization, $"Language '{Language}' ({_table.Count} texts).");
            LanguageChanged?.Invoke();
        }

        /// <summary>Text of <paramref name="key"/> in the shown language (no allocation).</summary>
        public string Get(string key)
        {
            if (_table != null && _table.TryGet(key, out var text))
                return ShowMissingKeys && _table.IsMissing(key) ? key : text;

            if (_table != null && key != null && _reportedKeys.Add(key))
                GameLog.Warning(LogChannel.Localization, $"No text for key '{key}'.");
            return key ?? string.Empty;
        }

        /// <summary>True when the table has <paramref name="key"/> (for optional keys like per-colour messages).</summary>
        public bool Has(string key) => _table != null && key != null && _table.TryGet(key, out _);

        /// <summary><see cref="string.Format(string, object)"/> over the text of <paramref name="key"/>.</summary>
        public string Format(string key, object arg0) => SafeFormat(key, arg0);

        public string Format(string key, object arg0, object arg1) => SafeFormat(key, arg0, arg1);

        /// <summary>Shows the language at <paramref name="index"/>; a newer call cancels a pending one.</summary>
        public async UniTask SetLanguageAsync(int index, CancellationToken cancellation = default)
        {
            if (_catalog == null || index < 0 || index >= Languages.Count || index == LanguageIndex) return;

            _switch?.Cancel();
            _switch?.Dispose();
            _switch = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var token = _switch.Token;
            try
            {
                await LoadAsync(index, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            GameLog.Info(LogChannel.Localization, $"Language switched to '{Language}'.");
            LanguageChanged?.Invoke();
        }

        public void Dispose()
        {
            _switch?.Cancel();
            _switch?.Dispose();
            _switch = null;
            _tableAssets?.Dispose();
            _tableAssets = null;
            _catalogAssets?.Dispose();
            _catalogAssets = null;
            _table = null;
            _catalog = null;
            LanguageIndex = -1;
        }

        private async UniTask LoadAsync(int index, CancellationToken cancellation)
        {
            var language = Languages[index];
            var owner = _addressables.CreateOwner("Application: Texts " + language.Code);
            LocalizationTable table;
            try
            {
                table = await owner.LoadAsync<LocalizationTable>(language.Table, cancellation);
                cancellation.ThrowIfCancellationRequested();
            }
            catch
            {
                owner.Dispose();
                throw;
            }

            _tableAssets?.Dispose();
            _tableAssets = owner;
            _table = table;
            LanguageIndex = index;
            _reportedKeys.Clear();
            _settings.Language = language.Code;
        }

        private string SafeFormat(string key, params object[] args)
        {
            var format = Get(key);
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                GameLog.Warning(LogChannel.Localization, $"Bad format of key '{key}' in '{Language}': {format}");
                return format;
            }
        }
    }
}
