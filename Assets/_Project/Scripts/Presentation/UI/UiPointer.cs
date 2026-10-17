using System.Collections.Generic;
using Purge.Application.Services;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// <see cref="IUiPointer"/> from the Bootstrap scene's EventSystem: the pointer is over a raycast target of any UI
    /// canvas (screens, HUD buttons; touch-only panels without raycast targets do not count).
    /// </summary>
    public sealed class UiPointer : IUiPointer
    {
        private readonly UIRoot _ui;
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private PointerEventData _pointer;

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

        public bool IsOverUiAt(Vector2 screenPosition)
        {
            var eventSystem = _ui != null ? _ui.EventSystem : null;
            if (eventSystem == null || !eventSystem.isActiveAndEnabled) return false;
            _pointer ??= new PointerEventData(eventSystem);
            _pointer.position = screenPosition;
            eventSystem.RaycastAll(_pointer, _hits);
            var over = _hits.Count > 0;
            _hits.Clear();
            return over;
        }
    }
}
