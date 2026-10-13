using Maze.Application.Services;
using TMPro;
using UnityEngine;

namespace Maze.Presentation.Localization
{
    /// <summary>
    /// A static caption by key (a <see cref="TextKeys"/> constant, set by <c>RuntimeScenesBuilder</c>). Listed in
    /// <see cref="UI.UIRoot"/>; <see cref="LocalizationPresenter"/> sets the text at start and on a language switch —
    /// the component itself does nothing per frame or on enable.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class LocalizedText : MonoBehaviour
    {
        [SerializeField] private string _key;
        [SerializeField] private TMP_Text _text;

        public string Key => _key;

        public void Apply(LocalizationService texts)
        {
            if (_text == null) _text = GetComponent<TMP_Text>();
            if (_text != null && !string.IsNullOrEmpty(_key)) _text.text = texts.Get(_key);
        }
    }
}
