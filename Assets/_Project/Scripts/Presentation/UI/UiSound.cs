using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>What a control sounds like when used.</summary>
    public enum UiSoundKind
    {
        Click = 0,

        /// <summary>Back, close, cancel.</summary>
        Back = 1,

        /// <summary>Silent: the screen it opens has its own sound (pause, map).</summary>
        None = 2,

        /// <summary>A click on an unavailable control (locked level). Played automatically, not chosen.</summary>
        Denied = 3,
    }

    /// <summary>
    /// Sound of a button or toggle: asks <see cref="UIRoot.RequestSound"/> on click; a click on a disabled control
    /// sounds <see cref="UiSoundKind.Denied"/>. Copied with the control (level buttons are clones of a template).
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public sealed class UiSound : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private UiSoundKind _kind = UiSoundKind.Click;

        private Selectable _selectable;
        private UIRoot _root;

        public UiSoundKind Kind { get => _kind; set => _kind = value; }

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            _root = GetComponentInParent<UIRoot>(true);
            if (_selectable is Button button) button.onClick.AddListener(OnUsed);
            else if (_selectable is Toggle toggle) toggle.onValueChanged.AddListener(_ => OnUsed());
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_selectable != null && !_selectable.IsInteractable() && _root != null)
                _root.RequestSound(UiSoundKind.Denied);
        }

        private void OnUsed()
        {
            if (_kind != UiSoundKind.None && _root != null)
                _root.RequestSound(_kind);
        }
    }
}
