using System;
using Purge.Presentation.UI.Style;
using TMPro;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// A yes/no window over the menu or the pause (stage 6e: «Начать новый забег? Текущий будет потерян»). Not a flow state:
    /// <see cref="ScreenRouter"/> opens it with the action of «Да» and closes it on any answer or state change.
    /// </summary>
    public sealed class ConfirmScreen : UIScreen
    {
        [SerializeField] private TMP_Text _message;
        [SerializeField] private UiButton _yesButton;
        [SerializeField] private UiButton _noButton;

        private Action _onYes;

        private void Awake()
        {
            Bind(_yesButton, OnYes);
            Bind(_noButton, Close);
        }

        public bool IsOpen => _onYes != null;

        public void Open(string message, Action onYes)
        {
            _onYes = onYes;
            if (_message != null) _message.text = message;
            SetVisible(true);
        }

        public void Close()
        {
            _onYes = null;
            SetVisible(false);
        }

        private void OnYes()
        {
            var action = _onYes;
            Close();
            action?.Invoke();
        }
    }
}
