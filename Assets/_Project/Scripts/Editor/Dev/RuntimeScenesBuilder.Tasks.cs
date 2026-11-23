using System.Collections.Generic;
using Purge.Application.Meta;
using Purge.Presentation.Localization;
using Purge.Presentation.UI;
using TMPro;
using UnityEngine;

namespace Purge.Editor.Dev
{
    internal static partial class RuntimeScenesBuilder
    {
        private static TasksScreen BuildTasks(Transform parent)
        {
            var screen = Screen<TasksScreen>(parent, "TasksScreen", Background);
            var area = SafeArea(screen.transform);
            var icons = UiIcons();
            SetReference(screen, "_back", RoundButton(area, "Back", icons.ChevronLeft, TopLeft, new Vector2(100, -78), 84, 36));
            Text(area, "Title", Loc(TextKeys.MenuTasks), 38, true, TopLeft, Center,
                new Vector2(460, -78), new Vector2(600, 68), TextAlignmentOptions.Left);
            SetReference(screen, "_money", Text(area, "Money", "", 38, true, new Vector2(1, 1), Center,
                new Vector2(-175, -78), new Vector2(220, 68), TextAlignmentOptions.Center));

            Text(area, "TaskHeading", Loc(TextKeys.TasksDaily), 38, true, Center, Center,
                new Vector2(-420, 290), new Vector2(790, 66), TextAlignmentOptions.Left);
            AutoSize(Text(area, "TaskHint", Loc(TextKeys.TasksHint), 25, false, Center, Center,
                new Vector2(-420, 230), new Vector2(790, 64), TextAlignmentOptions.Left, _style.MutedText));

            var objectives = new List<Object>();
            var progress = new List<Object>();
            var claims = new List<Object>();
            var claimLabels = new List<Object>();
            for (var i = 0; i < DailyTasks.OfferCount; i++)
            {
                var y = 110f - i * 160f;
                objectives.Add(AutoSize(Text(area, $"Objective{i}", "", 33, true, Center, Center,
                    new Vector2(-560, y), new Vector2(510, 82), TextAlignmentOptions.Left)));
                progress.Add(Text(area, $"Progress{i}", "", 27, false, Center, Center,
                    new Vector2(-690, y - 60), new Vector2(250, 38), TextAlignmentOptions.Left, _style.MutedText));
                var claim = TextButton(area, $"Claim{i}", "", true, icons.Coin, 78, 28);
                Place(claim, Center, new Vector2(-165, y - 20), new Vector2(255, 78));
                claims.Add(claim);
                claimLabels.Add(AutoSize(claim.GetComponentInChildren<TMP_Text>(true)));
            }
            SetReferences(screen, "_objectives", objectives);
            SetReferences(screen, "_progress", progress);
            SetReferences(screen, "_claims", claims);
            SetReferences(screen, "_claimLabels", claimLabels);

            Text(area, "RewardHeading", Loc(TextKeys.TasksRewards), 38, true, Center, Center,
                new Vector2(435, 290), new Vector2(740, 66), TextAlignmentOptions.Left);
            AutoSize(Text(area, "RewardHint", Loc(TextKeys.TasksCycleHint), 25, false, Center, Center,
                new Vector2(435, 230), new Vector2(740, 64), TextAlignmentOptions.Left, _style.MutedText));
            var days = new List<Object>();
            var amounts = new List<Object>();
            for (var i = 0; i < DailyTasks.RewardCount; i++)
            {
                var x = 154 + (i % 4) * 187;
                var y = i < 4 ? 105 : -43;
                days.Add(AutoSize(Text(area, $"Day{i}", "", 26, false, Center, Center,
                    new Vector2(x, y), new Vector2(168, 43), TextAlignmentOptions.Center)));
                amounts.Add(Text(area, $"Amount{i}", "", 38, true, Center, Center,
                    new Vector2(x, y - 53), new Vector2(168, 54), TextAlignmentOptions.Center));
            }
            SetReferences(screen, "_rewardDays", days);
            SetReferences(screen, "_rewardAmounts", amounts);
            var rewardClaim = TextButton(area, "ClaimReward", "", true, icons.Coin, 86, 31);
            Place(rewardClaim, Center, new Vector2(435, -225), new Vector2(565, 86));
            SetReference(screen, "_rewardClaim", rewardClaim);
            SetReference(screen, "_rewardClaimLabel", AutoSize(rewardClaim.GetComponentInChildren<TMP_Text>(true)));
            SetReference(screen, "_status", AutoSize(Text(area, "Status", "", 26, false, Center, Center,
                new Vector2(0, -355), new Vector2(1520, 56), TextAlignmentOptions.Center, _style.MutedText)));
            return screen;
        }
    }
}
