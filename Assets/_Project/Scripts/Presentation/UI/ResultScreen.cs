using System;
using Purge.Application.Services;
using Purge.Gameplay.Level;
using Purge.Presentation.Localization;
using Purge.Presentation.UI.Style;
using TMPro;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// Result of a run (stage 6d, ТЗ «итог» instead of the stars of Maze): VICTORY / DEFEAT and the level name; rows of
    /// the run's numbers — waves (only on a level with waves), kills, time, score, money earned — that appear one by one
    /// with their sounds (<see cref="RevealNextRow"/> from the audio presenter; without sound they all appear after a
    /// while). Under them ×2 for a rewarded ad (<see cref="DoubleClicked"/>; after it the money row shows the doubled sum
    /// in the accent). Buttons: Menu, Retry and — after a win with another level after it — Next (the main action).
    /// The completed tutorial shows only its title (<see cref="SetTutorialComplete"/>).
    /// </summary>
    public sealed class ResultScreen : UIScreen
    {
        public const int Waves = 0;
        public const int Kills = 1;
        public const int Time = 2;
        public const int Score = 3;
        public const int Money = 4;
        public const int RowCount = 5;

        private const float PopTime = 0.28f;
        private const float PopScale = 1.25f;

        /// <summary>Rows still hidden this long after the screen opened appear by themselves.</summary>
        private const float RevealFallback = 4f;

        [Serializable]
        private struct Row
        {
            [Tooltip("Takes the row's place in the layout: hidden only for a row this result has not (no reflow on reveal).")]
            public GameObject Slot;

            [Tooltip("What appears with the row's sound.")]
            public GameObject Root;

            public TMP_Text Value;
        }

        [SerializeField] private UiStyle _style;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _levelName;
        [SerializeField] private GameObject _rowsRoot;
        [SerializeField] private Row[] _rows = Array.Empty<Row>();
        [SerializeField] private UiButton _doubleButton;
        [SerializeField] private UiButton _retryButton;
        [SerializeField] private UiButton _menuButton;
        [SerializeField] private UiButton _nextButton;

        private readonly bool[] _shown = new bool[RowCount];
        private readonly float[] _pop = new float[RowCount];
        private int _revealed;
        private float _shownTime;
        private int _money;
        private bool _canDouble;
        private LocalizationService _texts;

        public event Action RetryClicked;
        public event Action MenuClicked;
        public event Action NextClicked;
        public event Action DoubleClicked;

        private void Awake()
        {
            Bind(_retryButton, () => RetryClicked?.Invoke());
            Bind(_menuButton, () => MenuClicked?.Invoke());
            Bind(_nextButton, () => NextClicked?.Invoke());
            Bind(_doubleButton, () => DoubleClicked?.Invoke());
        }

        public override void ApplyLanguage(LocalizationService texts) => _texts = texts;

        /// <summary>
        /// Fills the screen; the rows start hidden and are revealed one by one. <paramref name="levelName"/> is shown as
        /// given (already in the language's letter case).
        /// </summary>
        /// <param name="canDouble">The ×2 offer is shown (the rows' reveal ends before it appears).</param>
        public void SetResult(bool completed, LevelResult? result, string levelName, bool hasNext, bool canDouble)
        {
            _title.text = Text(completed ? TextKeys.ResultComplete : TextKeys.ResultFailed);
            if (_levelName != null) _levelName.text = levelName ?? string.Empty;
            ShowDetails(true);

            var r = result ?? default;
            _money = r.Money;
            _shown[Waves] = r.WaveCount > 0;
            _shown[Kills] = true;
            _shown[Time] = r.Time > 0f;
            _shown[Score] = r.Score > 0 || r.WaveCount > 0;
            _shown[Money] = true;
            SetValue(Waves, r.Waves + " / " + r.WaveCount);
            SetValue(Kills, r.Kills.ToString());
            SetValue(Time, TimeFormat.ToText(r.Time));
            SetValue(Score, r.Score.ToString());
            SetMoney(false);

            for (var i = 0; i < _rows.Length && i < RowCount; i++)
            {
                _pop[i] = -1f;
                if (_rows[i].Slot != null) _rows[i].Slot.SetActive(_shown[i]);
                if (_rows[i].Root != null) _rows[i].Root.SetActive(false);
            }

            _revealed = 0;
            _shownTime = UnityEngine.Time.unscaledTime;
            _canDouble = canDouble;
            if (_doubleButton != null) _doubleButton.gameObject.SetActive(false);

            var next = completed && hasNext;
            if (_nextButton != null) _nextButton.gameObject.SetActive(next);
            // The main action: Next after a win, Retry otherwise.
            if (_retryButton != null) _retryButton.Accent = !next;
        }

        /// <summary>The money was doubled (or the ad failed: the offer comes back with <paramref name="doubled"/> false).</summary>
        public void SetDoubled(bool doubled)
        {
            _canDouble = !doubled;
            SetMoney(doubled);
            if (_doubleButton != null) _doubleButton.gameObject.SetActive(_canDouble && _revealed >= RowCount);
        }

        /// <summary>The ×2 button while its ad is shown.</summary>
        public void SetDoubleBusy(bool busy)
        {
            if (_doubleButton != null) _doubleButton.interactable = !busy;
        }

        /// <summary>"Tutorial complete": the title, Menu and — when there is one — Next (the first level, the main action).</summary>
        public void SetTutorialComplete(bool hasNext)
        {
            _title.text = Text(TextKeys.ResultTutorialComplete);
            ShowDetails(false);
            _revealed = RowCount;
            _canDouble = false;
            if (_doubleButton != null) _doubleButton.gameObject.SetActive(false);
            if (_nextButton != null) _nextButton.gameObject.SetActive(hasNext);
            if (_retryButton != null)
            {
                _retryButton.gameObject.SetActive(false);
                _retryButton.Accent = false;
            }
        }

        /// <summary>Rows still to be revealed (one sound each).</summary>
        public int PendingRows
        {
            get
            {
                var count = 0;
                for (var i = _revealed; i < RowCount; i++)
                    if (_shown[i]) count++;
                return count;
            }
        }

        /// <summary>Shows the next row with a little pop (one per sound); after the last one — the ×2 offer.</summary>
        public void RevealNextRow()
        {
            for (var i = _revealed; i < RowCount; i++)
            {
                _revealed = i + 1;
                if (!_shown[i] || i >= _rows.Length || _rows[i].Root == null) continue;
                _rows[i].Root.SetActive(true);
                _rows[i].Root.transform.localScale = Vector3.one * PopScale;
                _pop[i] = 0f;
                break;
            }

            if (_revealed >= RowCount && PendingRows == 0 && _doubleButton != null)
                _doubleButton.gameObject.SetActive(_canDouble);
        }

        private void Update()
        {
            if (UnityEngine.Time.unscaledTime - _shownTime > RevealFallback && _revealed < RowCount)
                while (_revealed < RowCount) RevealNextRow();

            for (var i = 0; i < _rows.Length && i < RowCount; i++)
            {
                if (_pop[i] < 0f) continue;
                _pop[i] += UnityEngine.Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(_pop[i] / PopTime);
                var scale = Mathf.Lerp(PopScale, 1f, 1f - (1f - t) * (1f - t));
                _rows[i].Root.transform.localScale = new Vector3(scale, scale, 1f);
                if (t >= 1f) _pop[i] = -1f;
            }
        }

        /// <summary>Level name and rows (hidden for the tutorial); Retry is back for any other result.</summary>
        private void ShowDetails(bool shown)
        {
            if (_levelName != null) _levelName.gameObject.SetActive(shown);
            if (_rowsRoot != null) _rowsRoot.SetActive(shown);
            if (_retryButton != null && shown) _retryButton.gameObject.SetActive(true);
        }

        private void SetMoney(bool doubled)
        {
            if (Money >= _rows.Length || _rows[Money].Value == null) return;
            var value = _rows[Money].Value;
            value.text = "+" + (doubled ? _money * 2 : _money);
            value.color = doubled ? _style.Accent : _style.Text;
        }

        private void SetValue(int row, string text)
        {
            if (row < _rows.Length && _rows[row].Value != null) _rows[row].Value.text = text;
        }

        private string Text(string key) => _texts != null ? _texts.Get(key) : key;
    }
}
