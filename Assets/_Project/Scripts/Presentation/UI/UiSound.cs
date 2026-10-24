using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Purge.Presentation.UI
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

        /// <summary>A confirmation window opens (stage 6e2). Requested by code, not set on buttons.</summary>
        Window = 4,

        /// <summary>Cards of a level-up appear / a card is chosen / the cards are rerolled (stage 6e2). Requested by code.</summary>
        CardsShown = 5,
        CardChosen = 6,
        CardReroll = 7,
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
