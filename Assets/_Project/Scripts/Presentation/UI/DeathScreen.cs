using System;
using Purge.Presentation.UI.Style;
using UnityEngine;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// The death window over the HUD (stage 6d): "YOU DIED", revive for a rewarded ad, revive by the character's skill
    /// (each shown only while it is left), give up. A passive view: <see cref="DeathPresenter"/> fills it and acts.
    /// </summary>
    public sealed class DeathScreen : UIScreen
    {
        [SerializeField] private UiButton _adButton;
        [SerializeField] private UiButton _skillButton;
        [SerializeField] private UiButton _giveUpButton;

        public event Action AdClicked;
        public event Action SkillClicked;
        public event Action GiveUpClicked;

        private void Awake()
        {
            Bind(_adButton, () => AdClicked?.Invoke());
            Bind(_skillButton, () => SkillClicked?.Invoke());
            Bind(_giveUpButton, () => GiveUpClicked?.Invoke());
        }

        /// <summary>Which revives are offered; the first one offered is the main action.</summary>
        public void SetOptions(bool ad, bool skill)
        {
            if (_adButton != null)
            {
                _adButton.gameObject.SetActive(ad);
                _adButton.Accent = ad;
            }

            if (_skillButton != null)
            {
                _skillButton.gameObject.SetActive(skill);
                _skillButton.Accent = skill && !ad;
            }

            SetBusy(false);
        }

        /// <summary>A rewarded ad is shown: nothing can be pressed.</summary>
        public void SetBusy(bool busy)
        {
            if (_adButton != null) _adButton.interactable = !busy;
            if (_skillButton != null) _skillButton.interactable = !busy;
            if (_giveUpButton != null) _giveUpButton.interactable = !busy;
        }
    }
}
