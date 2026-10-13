using System;
using System.Collections.Generic;
using Maze.Core.Grid;
using UnityEngine;

namespace Maze.Core.Level
{
    /// <summary>What the player has to do to finish a tutorial step.</summary>
    public enum TutorialGoal
    {
        /// <summary>Never finishes by itself (the last step: the level ends at the exit).</summary>
        None = 0,

        /// <summary>Enter the target's cell (or <see cref="TutorialStep.Cell"/> without a target).</summary>
        Reach = 1,

        /// <summary>The target pickup (key, medkit, weapon, map fragment) no longer lies in the level.</summary>
        Collect = 2,

        /// <summary>The target door is no longer locked.</summary>
        UnlockDoor = 3,

        /// <summary>The target door is open.</summary>
        OpenDoor = 4,

        /// <summary>The target zombie is dead.</summary>
        Kill = 5,

        /// <summary>The player opened the map.</summary>
        OpenMap = 6,
    }

    /// <summary>A control the step points at with a pulsing ring (the on-screen one on touch screens).</summary>
    public enum TutorialHighlight
    {
        None = 0,
        Stick = 1,
        Attack = 2,
        Use = 3,
        Melee = 4,
        Ranged = 5,
        Map = 6,
    }

    /// <summary>
    /// One step of the tutorial: a text shown in a window that pauses the game (<see cref="Dialog"/>), then a short
    /// reminder on the screen, an arrow and a line on the floor to <see cref="Target"/> until <see cref="Goal"/> is done.
    /// </summary>
    [Serializable]
    public sealed class TutorialStep
    {
        [Tooltip("Text key (tutorial.*): the window text. Optional variants: <key>.touch for touch screens, " +
                 "<key>.hint (and <key>.hint.touch) — the reminder line shown while the step is on.")]
        [SerializeField, TutorialText] private string _text;

        [Tooltip("Show the text in a window that pauses the game when the step starts.")]
        [SerializeField] private bool _dialog = true;

        [SerializeField] private TutorialGoal _goal;

        [Tooltip("Object of the level the arrow and the line lead to; the goal is checked on it.")]
        [SerializeField, LevelEntityRef] private string _target;

        [Tooltip("Cell to reach when there is no target (Reach only).")]
        [SerializeField] private Vector2Int _cell;

        [SerializeField] private TutorialHighlight _highlight;

        public TutorialStep()
        {
        }

        internal TutorialStep(string text, TutorialGoal goal, string target = null, Vector2Int cell = default,
            TutorialHighlight highlight = TutorialHighlight.None, bool dialog = true)
        {
            _text = text;
            _goal = goal;
            _target = target;
            _cell = cell;
            _highlight = highlight;
            _dialog = dialog;
        }

        public string Text => _text;
        public bool Dialog => _dialog;
        public TutorialGoal Goal => _goal;
        public string Target => _target;
        public bool HasTarget => !string.IsNullOrEmpty(_target);
        public GridPosition Cell => new GridPosition(_cell.x, _cell.y);
        public TutorialHighlight Highlight => _highlight;
    }

    /// <summary>
    /// Steps of a tutorial level, in order. Referenced by its <see cref="LevelData.Tutorial"/>; such a level is the game's
    /// tutorial (Build / Sync lists it apart from the numbered levels).
    /// </summary>
    [CreateAssetMenu(fileName = "TutorialScript", menuName = "Maze/Tutorial Script")]
    public sealed class TutorialScript : ScriptableObject
    {
        [SerializeField] private List<TutorialStep> _steps = new List<TutorialStep>();

        public IReadOnlyList<TutorialStep> Steps => _steps;

        internal List<TutorialStep> MutableSteps => _steps;
    }

    /// <summary>A string field holding a tutorial text key: the inspector shows the tutorial.* keys of the table.</summary>
    public sealed class TutorialTextAttribute : PropertyAttribute
    {
    }

    /// <summary>A string field holding an object id of the level: the inspector shows the level's ids.</summary>
    public sealed class LevelEntityRefAttribute : PropertyAttribute
    {
    }
}
