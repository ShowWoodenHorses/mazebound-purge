using Maze.Application.Services;
using UnityEngine.EventSystems;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// <see cref="IUiPointer"/> from the Bootstrap scene's EventSystem: the pointer is over a raycast target of any UI
    /// canvas (screens, HUD buttons; touch-only panels without raycast targets do not count).
    /// </summary>
    public sealed class UiPointer : IUiPointer
    {
        private readonly UIRoot _ui;

        public UiPointer(UIRoot ui)
        {
            _ui = ui;
        }

        public bool IsOverUi
        {
            get
            {
                var eventSystem = _ui != null ? _ui.EventSystem : null;
                return eventSystem != null && eventSystem.isActiveAndEnabled && eventSystem.IsPointerOverGameObject();
            }
        }
    }
}
