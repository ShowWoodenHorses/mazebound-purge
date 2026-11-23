using System;
using System.Collections.Generic;
using Purge.Application.Levels;
using Purge.Application.Platform;
using Purge.Application.Save;
using Purge.Core.Common;
using Purge.Core.Level;
using UnityEngine;

namespace Purge.Application.Meta
{
    public enum DailyCondition { Kills, CampaignWave, ClearedWaves, RamKills }

    [Serializable]
    public sealed class DailyOffer
    {
        public string Id;
        public DailyCondition Condition;
        public int Target;
        public int Difficulty;
        public int Money;
    }

    public sealed class DailyTasks
    {
        public const int OfferCount = 3;
        public const int RewardCount = 7;

        [Serializable]
        private sealed class Content
        {
            public DailyOffer[] Offers;
            public int[] Rewards;
        }

        private readonly SaveService _save;
        private readonly IProgressService _progress;
        private readonly ILevelCatalog _catalog;
        private readonly AnalyticsService _analytics;
        private readonly Content _content;
        private readonly Dictionary<string, DailyOffer> _offers = new Dictionary<string, DailyOffer>();
        private DailySave _dirty;
        private List<DailyTaskSave> _validated;

        public DailyTasks(SaveService save, IProgressService progress, ILevelCatalog catalog, AnalyticsService analytics)
        {
            _save = save;
            _progress = progress;
            _catalog = catalog;
            _analytics = analytics;
            var asset = Resources.Load<TextAsset>("DailyTasks");
            _content = JsonUtility.FromJson<Content>(asset.text);
            foreach (var offer in _content.Offers) _offers.Add(offer.Id, offer);
        }

        public static int LocalDay => (int)(DateTime.Today.Ticks / TimeSpan.TicksPerDay);
        private DailySave State => _save.Data.Daily;
        public IReadOnlyList<DailyTaskSave> Tasks => State.Tasks;
        public IReadOnlyList<int> Rewards => _content.Rewards;
        public int NextReward => State.NextReward;
        public bool CanClaimReward(int day) => day == State.TaskDay && day > State.LastRewardDay;
        public DailyOffer Offer(string id) => _offers.TryGetValue(id, out var offer) ? offer : null;

        public bool Refresh(int day)
        {
            var state = State;
            if (day < state.TaskDay || _catalog.Levels.Count == 0) return false;
            if (day == state.TaskDay && (ReferenceEquals(_validated, state.Tasks) ||
                state.Tasks.Count == OfferCount && state.Tasks.TrueForAll(task => _offers.ContainsKey(task.Id))))
            {
                _validated = state.Tasks;
                return false;
            }

            var candidates = new List<DailyOffer>(_content.Offers.Length);
            foreach (var offer in _content.Offers)
                if (IsAvailable(offer)) candidates.Add(offer);

            // A stable daily draw without replacement. Each slot uses a different condition.
            var random = new DeterministicRandom(day);
            var selected = new List<DailyTaskSave>(OfferCount);
            while (selected.Count < OfferCount && candidates.Count > 0)
            {
                var offer = candidates[random.NextInt(candidates.Count)];
                selected.Add(state.TaskDay == day ? state.Tasks.Find(task => task.Id == offer.Id) ??
                    new DailyTaskSave { Id = offer.Id } : new DailyTaskSave { Id = offer.Id });
                candidates.RemoveAll(candidate => candidate.Condition == offer.Condition);
            }

            state.TaskDay = day;
            state.Tasks = _validated = selected;
            _dirty = state;
            return true;
        }

        private bool IsAvailable(DailyOffer offer)
        {
            if (offer.Condition == DailyCondition.CampaignWave)
            {
                foreach (var level in _catalog.Levels)
                    if (_progress.IsDifficultyOpen(level.LevelId, offer.Difficulty)) return true;
                return false;
            }
            if (offer.Condition == DailyCondition.RamKills)
            {
                // Forest has road vehicles and is available in survival from the start.
                foreach (var level in _catalog.Levels)
                    if (level.Location == LevelLocation.Forest) return true;
                return false;
            }
            return true;
        }

        public void Record(int day, DailyCondition condition, int amount, int difficulty = 0)
        {
            if (amount <= 0) return;
            Refresh(day);
            if (day != State.TaskDay) return;

            var campaignWave = condition == DailyCondition.CampaignWave;
            foreach (var task in State.Tasks)
            {
                var offer = Offer(task.Id);
                if (offer == null || task.Claimed || offer.Condition != condition ||
                    task.Progress >= offer.Target || difficulty < offer.Difficulty) continue;
                if (campaignWave && amount > offer.Target) continue;
                var progress = campaignWave
                    ? Math.Max(task.Progress, amount)
                    : task.Progress + Math.Min(offer.Target - task.Progress, amount);
                if (progress == task.Progress) continue;
                task.Progress = progress;
                _dirty = State;
            }
        }

        public bool CanClaimTask(DailyTaskSave task, int day)
        {
            var offer = Offer(task.Id);
            return day == State.TaskDay && !task.Claimed && offer != null && task.Progress >= offer.Target;
        }

        public bool ClaimTask(string id, int day)
        {
            Refresh(day);
            var task = State.Tasks.Find(entry => entry.Id == id);
            if (task == null || !CanClaimTask(task, day)) return false;

            var offer = Offer(id);
            task.Claimed = true;
            if (!Pay(offer.Money))
            {
                task.Claimed = false;
                return false;
            }
            if (!_save.IsDevProgress)
            {
                var fields = _analytics.Begin();
                fields["task_id"] = id;
                fields["money"] = offer.Money;
                _analytics.Send(AnalyticsEvents.DailyTaskClaimed);
            }
            return true;
        }

        public bool ClaimReward(int day)
        {
            Refresh(day);
            if (!CanClaimReward(day)) return false;

            var state = State;
            var previousDay = state.LastRewardDay;
            var index = state.NextReward;
            var money = Rewards[index];
            state.LastRewardDay = day;
            state.NextReward = (index + 1) % RewardCount;
            if (!Pay(money))
            {
                state.LastRewardDay = previousDay;
                state.NextReward = index;
                return false;
            }
            if (!_save.IsDevProgress)
            {
                var fields = _analytics.Begin();
                fields["cycle_day"] = index + 1;
                fields["money"] = money;
                _analytics.Send(AnalyticsEvents.DailyRewardClaimed);
            }
            return true;
        }

        private bool Pay(int money)
        {
            var data = _save.Data;
            var previous = data.Money;
            data.Money += Math.Min(int.MaxValue - previous, money);
            if (_save.Save())
            {
                _dirty = null;
                return true;
            }
            data.Money = previous;
            return false;
        }

        public void Flush()
        {
            if (_dirty == null) return;
            if (!ReferenceEquals(_dirty, State) || _save.Save()) _dirty = null;
        }
    }
}
