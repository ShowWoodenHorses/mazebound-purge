using System.Collections.Generic;
using Purge.Application.Meta;
using Purge.Presentation.UI;
using Purge.Presentation.UI.Shapes;
using Purge.Presentation.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

            BuildCharacterTree(area, screen);

            var details = Text(area, "Details", "", 25, false, TopLeft, Center,
                Vector2.zero, Vector2.zero, TextAlignmentOptions.Left);
            SetReference(screen, "_details", AutoSize(details));
            var buy = TextButton(area, "Buy", "", true, null, 70, 26);
            PlaceCharacterSummary(details, buy);
            SetReference(screen, "_buy", buy);
            SetReference(screen, "_buyLabel", buy.GetComponentInChildren<TMP_Text>(true));
            var reset = TextButton(area, "Reset", Loc(TextKeys.CharacterReset), false, null, 58, 23);
            Place(reset, TopLeft, new Vector2(970, -78), new Vector2(280, 58));
            SetReference(screen, "_reset", reset);
            return screen;
        }

        private static void PlaceCharacterSummary(TMP_Text details, Button buy)
        {
            const float summaryY = -970f;
            Place(details, TopLeft, new Vector2(650, summaryY), new Vector2(1090, 100));
            Place(buy, TopLeft, new Vector2(1420, summaryY), new Vector2(390, 70));
        }

        private const float CharacterNodeHeight = 46f;

        private static void BuildCharacterTree(Transform area, CharacterScreen screen)
        {
            var anchor = new Vector2(0.5f, 1f);
            var links = Rect(area, "TreePaths", anchor, Vector2.zero, Vector2.zero);
            var branches = new[] { CharacterBranch.Shooter, CharacterBranch.Demolisher, CharacterBranch.Survivor };
            var branchX = new[] { -700f, 0f, 700f };
            var root = new Vector2(0f, -796f);
            var nodes = CharacterTree.Nodes;
            var positions = new Vector2[nodes.Count];
            var incoming = new Object[nodes.Count];

            for (var lane = 0; lane < branches.Length; lane++)
            {
                var branch = branches[lane];
                var tipX = branchX[lane];
                var first = new Vector2(tipX * 0.46f, -704f);
                DrawCharacterStem(links, root, first - Vector2.up * (CharacterNodeHeight * 0.5f), branch.ToString());
                var count = 0;
                foreach (var node in nodes)
                    if (node.Branch == branch) count++;

                var step = 0;
                var previous = first;
                for (var i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].Branch != branch) continue;
                    var t = step / (float)Mathf.Max(1, count - 1);
                    var x = Mathf.SmoothStep(first.x, tipX, t);
                    if (branch == CharacterBranch.Demolisher)
                        x += Mathf.Sin(t * 2f * Mathf.PI) * 92f;
                    var position = new Vector2(x, Mathf.Lerp(first.y, -219f, t));
                    positions[i] = position;
                    if (step > 0)
                    {
                        // Clip to the top and bottom edges so the line stays outside the button fill.
                        var inset = (position - previous) * (CharacterNodeHeight * 0.5f / (position.y - previous.y));
                        incoming[i] = CharacterConnection(links, nodes[i].Id, previous + inset, position - inset, 3f);
                    }
                    previous = position;
                    step++;
                }

                AutoSize(Text(area, branch.ToString(), Loc(TextKeys.CharacterBranchPrefix + branch), 32, true,
                    anchor, Center, new Vector2(tipX, -156f), new Vector2(280f, 48f), TextAlignmentOptions.Center));
            }

            var junction = Shape(links, "Junction", Center, root, new Vector2(16f, 16f));
            junction.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            junction.Fill = _style.Accent;
            junction.StrokeWidth = 0f;

            const float trunkY = -853f;
            const float nodeWidth = 254f;
            const float trunkSpacing = 286f;
            AutoSize(Text(area, "Trunk", Loc(TextKeys.CharacterBranchPrefix + CharacterBranch.Trunk), 26, true,
                anchor, Center, new Vector2(-570f, trunkY), new Vector2(230f, 46f), TextAlignmentOptions.Center));
            var trunkIndex = 0;
            var references = new List<Object>();
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node.Branch == CharacterBranch.Trunk)
                {
                    var position = new Vector2(-300f + trunkSpacing * trunkIndex, trunkY);
                    positions[i] = position;
                    if (trunkIndex++ > 0)
                        incoming[i] = CharacterConnection(links, node.Id,
                            position + Vector2.left * (trunkSpacing - nodeWidth * 0.5f),
                            position + Vector2.left * (nodeWidth * 0.5f), 3f);
                }

                var button = TextButton(area, node.Id, "", false, null, CharacterNodeHeight, 24, centerLabel: true);
                Place(button, anchor, positions[i], new Vector2(nodeWidth, CharacterNodeHeight));
                references.Add(button);
            }
            SetReferences(screen, "_nodes", references);
            SetReferences(screen, "_incoming", incoming);
            SetReference(screen, "_style", _style);
        }

        private static void DrawCharacterStem(Transform parent, Vector2 root, Vector2 end, string name)
        {
            var previous = root;
            const int segments = 12;
            for (var i = 1; i <= segments; i++)
            {
                var t = i / (float)segments;
                var next = new Vector2(Mathf.SmoothStep(root.x, end.x, t), Mathf.Lerp(root.y, end.y, t));
                CharacterConnection(parent, name + "Stem", previous, next, 5f).Fill = _style.Line;
                previous = next;
            }
        }

        private static UiShape CharacterConnection(Transform parent, string name, Vector2 from, Vector2 to, float width)
        {
            var delta = to - from;
            var line = Shape(parent, name, Center, (from + to) * 0.5f, new Vector2(delta.magnitude + 1f, width));
            line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            line.StrokeWidth = 0f;
            line.Fill = Fade(_style.Line, 0.5f);
            return line;
        }
    }
}
