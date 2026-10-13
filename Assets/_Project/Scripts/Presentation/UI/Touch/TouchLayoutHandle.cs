using Maze.Application.Save;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Maze.Presentation.UI.Touch
{
    /// <summary>
    /// Makes an on-screen control draggable in <see cref="TouchLayoutEditor"/>. Enabled only while the layout is edited
    /// (<see cref="TouchControls.SetEditing"/>; disabled in the scene); the control's own input component is disabled then.
    /// </summary>
    public sealed class TouchLayoutHandle : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        [SerializeField] private TouchElement _element;
        [SerializeField] private TouchLayoutEditor _editor;

        public void OnPointerDown(PointerEventData eventData)
        {
            _editor.OnHandleDown(_element, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            _editor.OnHandleDrag(_element, eventData);
        }
    }
}
