using System.Collections.Generic;
using Purge.Application.Meta;
using Purge.Presentation.UI;
using Purge.Presentation.Localization;
using TMPro;
using UnityEngine;

namespace Purge.Editor.Dev
{
    internal static partial class RuntimeScenesBuilder
    {
        private static CharacterScreen BuildCharacter(Transform parent)
        {
            var screen = Screen<CharacterScreen>(parent, "CharacterScreen", Background);
            var area = SafeArea(screen.transform);
            var back = RoundButton(area, "Back", UiIcons().ChevronLeft, TopLeft, new Vector2(100, -78), 84, 36);
            SetReference(screen, "_back", back);
            Text(area, "Title", Loc(TextKeys.MenuCharacter), 38, true, TopLeft, Center,
                new Vector2(460, -78), new Vector2(600, 68), TextAlignmentOptions.Left);
            SetReference(screen, "_money", Text(area, "Money", "", 38, true, new Vector2(1, 1), Center,
                new Vector2(-175, -78), new Vector2(220, 68), TextAlignmentOptions.Center));

            var references = new List<Object>();
            var rows = new int[4];
            for (var i = 0; i < rows.Length; i++)
            {
                var branch = (CharacterBranch)i;
                var title = Text(area, branch.ToString(), Loc(TextKeys.CharacterBranchPrefix + branch), 30, true,
                    TopLeft, Center, new Vector2(298 + i * 442, -157), new Vector2(410, 58), TextAlignmentOptions.Center);
                AutoSize(title);
            }
            foreach (var node in CharacterTree.Nodes)
            {
                var col = (int)node.Branch;
                var button = TextButton(area, node.Id, "", false, null, 52, 25);
                Place(button, TopLeft, new Vector2(298 + col * 442, -218 - 61 * rows[col]++), new Vector2(406, 52));
                AutoSize(button.GetComponentInChildren<TMP_Text>(true));
                references.Add(button);
            }
            SetReferences(screen, "_nodes", references);

            var details = Text(area, "Details", "", 25, false, TopLeft, Center,
                new Vector2(650, -927), new Vector2(1090, 100), TextAlignmentOptions.Left);
            SetReference(screen, "_details", AutoSize(details));
            var buy = TextButton(area, "Buy", "", true, null, 70, 26);
            Place(buy, TopLeft, new Vector2(1420, -916), new Vector2(390, 70));
            SetReference(screen, "_buy", buy);
            SetReference(screen, "_buyLabel", buy.GetComponentInChildren<TMP_Text>(true));
            var reset = TextButton(area, "Reset", Loc(TextKeys.CharacterReset), false, null, 58, 23);
            Place(reset, TopLeft, new Vector2(970, -78), new Vector2(280, 58));
            SetReference(screen, "_reset", reset);
            return screen;
        }
    }
}
