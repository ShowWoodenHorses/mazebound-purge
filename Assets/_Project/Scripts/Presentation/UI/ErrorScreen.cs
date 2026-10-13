using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    public sealed class ErrorScreen : UIScreen
    {
        [SerializeField] private TMP_Text _message;
        [SerializeField] private Button _backButton;

        public event Action BackClicked;

        private void Awake() => Bind(_backButton, () => BackClicked?.Invoke());

        public void SetMessage(string message) => _message.text = message;
    }
}
