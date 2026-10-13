using System.Linq;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Core.Validation;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>Shared editing state of the Level Designer window: current level, selection, last validation.</summary>
    internal sealed class LevelDesignerState
    {
        public LevelData Level { get; private set; }
        public GridPosition? SelectedCell { get; set; }
        public GridPosition? HoveredCell { get; set; }
        public ValidationReport Report { get; private set; }
        public ValidationIssue FocusedIssue { get; private set; }

        /// <summary>Id of the selected object (door, key, zombie...), or null.</summary>
        public string SelectedEntityId { get; set; }

        /// <summary>Rectangle being dragged (map fragment region), drawn as a preview on the grid.</summary>
        public GridRect? PreviewRect { get; set; }

        /// <summary>Cell an object is being dragged to with the Select tool.</summary>
        public GridPosition? DragTarget { get; set; }

        /// <summary>Set when the canvas should re-fit the level (new level, new size).</summary>
        public bool FitRequested { get; set; }

        public bool HasUsableLevel => Level != null && Level.Geometry != null && Level.Geometry.IsConsistent;

        public LevelEntityData SelectedEntity =>
            SelectedEntityId == null || Level == null ? null : Level.AllEntities().FirstOrDefault(e => e.Id == SelectedEntityId);

        public void SetLevel(LevelData level)
        {
            Level = level;
            ClearSelection();
            HoveredCell = null;
            FitRequested = true;
            Revalidate();
        }

        public void ClearSelection()
        {
            SelectedCell = null;
            SelectedEntityId = null;
            FocusedIssue = null;
            PreviewRect = null;
            DragTarget = null;
        }

        public void Select(LevelEntityData entity)
        {
            SelectedEntityId = entity?.Id;
            if (entity != null)
                SelectedCell = entity.Position;
        }

        /// <summary>Called after every command and undo/redo, so the report always matches the level.</summary>
        public void Revalidate()
        {
            Report = HasUsableLevel ? EditorLevelValidator.Validate(Level) : null;
            if (FocusedIssue != null && Report != null && !Report.Has(FocusedIssue.Code))
                FocusedIssue = null;
            if (SelectedEntityId != null && SelectedEntity == null)
                SelectedEntityId = null;
        }

        public void Focus(ValidationIssue issue)
        {
            FocusedIssue = issue;
            if (issue.Position.HasValue)
                SelectedCell = issue.Position.Value;
            if (issue.EntityId != null && Level.AllEntities().Any(e => e.Id == issue.EntityId))
                SelectedEntityId = issue.EntityId;
        }

        /// <summary>After a destructive change (Generate New) old selection and size are meaningless.</summary>
        public void OnLevelReplaced()
        {
            ClearSelection();
            FitRequested = true;
            Revalidate();
        }
    }
}
