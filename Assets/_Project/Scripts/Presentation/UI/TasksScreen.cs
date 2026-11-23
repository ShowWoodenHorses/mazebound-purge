using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    public sealed class TasksScreen : UIScreen
    {
        [SerializeField] private Button _back;
        [SerializeField] private TMP_Text _money;
        [SerializeField] private TMP_Text[] _objectives;
        [SerializeField] private TMP_Text[] _progress;
        [SerializeField] private Button[] _claims;
        [SerializeField] private TMP_Text[] _claimLabels;
        [SerializeField] private TMP_Text[] _rewardDays;
        [SerializeField] private TMP_Text[] _rewardAmounts;
        [SerializeField] private Button _rewardClaim;
        [SerializeField] private TMP_Text _rewardClaimLabel;
        [SerializeField] private TMP_Text _status;

        public event Action BackClicked;
        public event Action<int> TaskClicked;
        public event Action RewardClicked;

        private void Awake()
        {
            Bind(_back, () => BackClicked?.Invoke());
            Bind(_rewardClaim, () => RewardClicked?.Invoke());
            for (var i = 0; i < _claims.Length; i++)
            {
                var slot = i;
                Bind(_claims[i], () => TaskClicked?.Invoke(slot));
            }
        }

        public void SetMoney(int money) => _money.text = money.ToString();
        public void SetStatus(string status) => _status.text = status;

        public void SetTask(int slot, string objective, string progress, string claim, bool available)
        {
            _objectives[slot].text = objective;
            _progress[slot].text = progress;
            _claims[slot].interactable = available;
            _claimLabels[slot].text = claim;
        }

        public void SetRewardDay(int slot, string day, int coins, bool current)
        {
            _rewardDays[slot].text = day;
            _rewardDays[slot].fontStyle = current ? FontStyles.Bold | FontStyles.Underline : FontStyles.Normal;
            _rewardAmounts[slot].text = coins.ToString();
        }

        public void SetRewardClaim(string label, bool available)
        {
            _rewardClaimLabel.text = label;
            _rewardClaim.interactable = available;
        }
    }
}
