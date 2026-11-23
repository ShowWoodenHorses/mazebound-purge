using System;
using System.Collections.Generic;

namespace Purge.Application.Save
{
    [Serializable]
    public sealed class DailySave
    {
        public int TaskDay;
        public List<DailyTaskSave> Tasks = new List<DailyTaskSave>();
        public int LastRewardDay;
        public int NextReward;

        internal void Normalize()
        {
            TaskDay = Math.Max(0, TaskDay);
            LastRewardDay = Math.Max(0, LastRewardDay);
            NextReward = Math.Clamp(NextReward, 0, 6);
            Tasks ??= new List<DailyTaskSave>();
            var ids = new HashSet<string>();
            Tasks.RemoveAll(task => task == null || string.IsNullOrEmpty(task.Id) || !ids.Add(task.Id));
            foreach (var task in Tasks) task.Progress = Math.Max(0, task.Progress);
        }

        internal void MergeFrom(DailySave other)
        {
            if (other.LastRewardDay > LastRewardDay)
            {
                LastRewardDay = other.LastRewardDay;
                NextReward = other.NextReward;
            }

            if (other.TaskDay > TaskDay || other.TaskDay == TaskDay && Tasks.Count == 0)
            {
                TaskDay = other.TaskDay;
                Tasks = new List<DailyTaskSave>(other.Tasks.Count);
                foreach (var task in other.Tasks)
                    Tasks.Add(new DailyTaskSave { Id = task.Id, Progress = task.Progress, Claimed = task.Claimed });
                return;
            }

            if (other.TaskDay != TaskDay) return;
            foreach (var mine in Tasks)
            {
                var theirs = other.Tasks.Find(task => task.Id == mine.Id);
                if (theirs == null) continue;
                mine.Progress = Math.Max(mine.Progress, theirs.Progress);
                mine.Claimed |= theirs.Claimed;
            }
        }
    }

    [Serializable]
    public sealed class DailyTaskSave
    {
        public string Id;
        public int Progress;
        public bool Claimed;
    }
}
