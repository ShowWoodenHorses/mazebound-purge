using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Maze.Presentation.UI.Touch
{
    /// <summary>
    /// Tells whether an on-screen button is held, for its look (the pressed fill); sits next to the
    /// <c>OnScreenButton</c>, which does the input. Lets go when disabled (layout editing, hidden HUD).
    /// </summary>
    public sealed class TouchPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public bool IsPressed { get; private set; }

        public event Action Changed;

        public void OnPointerDown(PointerEventData eventData) => Set(true);

        public void OnPointerUp(PointerEventData eventData) => Set(false);

        private void OnDisable() => Set(false);

        private void Set(bool pressed)
        {
            if (IsPressed == pressed) return;
            IsPressed = pressed;
            Changed?.Invoke();
        }
    }
}
