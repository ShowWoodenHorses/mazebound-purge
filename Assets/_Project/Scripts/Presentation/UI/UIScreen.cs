using System;
using Maze.Application.Services;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// Base of application screens. Screens are passive views: they show data and raise events;
    /// <see cref="ScreenRouter"/> decides which screen is visible and what a click does.
    /// </summary>
    public abstract class UIScreen : MonoBehaviour
    {
        public bool IsVisible => gameObject.activeSelf;

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }

        /// <summary>
        /// The language changed (also once at start-up): screens that compose texts themselves keep
        /// <paramref name="texts"/> and refresh what they show. Static captions are <see cref="Localization.LocalizedText"/>.
        /// </summary>
        public virtual void ApplyLanguage(LocalizationService texts)
        {
        }

        protected static void Bind(Button button, Action action)
        {
            if (button != null)
                button.onClick.AddListener(() => action?.Invoke());
        }
    }
}
