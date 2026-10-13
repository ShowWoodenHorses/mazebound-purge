using System;
using Maze.Application.Services;
using Maze.Presentation.UI;

namespace Maze.Presentation.Localization
{
    /// <summary>
    /// Puts the texts of the current language on the application UI: at start-up (once the table is loaded) and on
    /// every switch (<see cref="LocalizationService.LanguageChanged"/>) — static captions and screens through
    /// <see cref="UIRoot.ApplyLanguage"/>. Texts of a level (HUD messages, map caption) are set by their presenters.
    /// </summary>
    public sealed class LocalizationPresenter : IDisposable
    {
        private readonly LocalizationService _texts;
        private readonly UIRoot _ui;
        private bool _initialized;

        public LocalizationPresenter(LocalizationService texts, UIRoot ui)
        {
            _texts = texts;
            _ui = ui;
        }

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _texts.LanguageChanged += Apply;
            if (_texts.LanguageIndex >= 0) Apply();
        }

        public void Dispose()
        {
            if (!_initialized) return;
            _initialized = false;
            _texts.LanguageChanged -= Apply;
        }

        private void Apply()
        {
            if (_ui != null) _ui.ApplyLanguage(_texts);
        }
    }
}
