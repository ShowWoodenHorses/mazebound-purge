using System.Linq;
using Maze.Core.Authoring;
using Maze.Core.Level;
using Maze.Core.Visual;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>
    /// Level Designer Tool (ТЗ §15–16): left side = command panels and inspector, right side = top-down 2D grid.
    /// Every change goes through <see cref="LevelEditorCommands"/> / <see cref="LevelEditing"/> (Undo + SetDirty);
    /// the level is revalidated after each command.
    /// </summary>
    public sealed class LevelDesignerWindow : EditorWindow
    {
        private static readonly string[] TabNames = { "Generation", "Edit", "Visuals", "Validate" };
        private const int EditTab = 1;
        private const float PanelWidth = 340f;

        [SerializeField] private LevelData _level;
        [SerializeField] private int _tab;
        [SerializeField] private Vector2 _panelScroll;

        private readonly LevelDesignerState _state = new LevelDesignerState();
        private readonly GridCanvas _canvas = new GridCanvas();
        private readonly GenerationPanel _generation = new GenerationPanel();
        private readonly EditPanel _edit = new EditPanel();
        private readonly VisualsPanel _visuals = new VisualsPanel();
        private readonly ValidationPanel _validation = new ValidationPanel();
        private EditToolController _tools;
        private SelectionInspector _inspector;

        [MenuItem("Maze/Level Designer")]
        public static void Open() => GetWindow<LevelDesignerWindow>("Level Designer").minSize = new Vector2(800f, 520f);

        private void OnEnable()
        {
            wantsMouseMove = true;
            _tools = new EditToolController(_state, Notify);
            _inspector = new SelectionInspector(_state, _tools, Notify);
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.duringSceneGui += OnSceneGUI;
            _state.SetLevel(_level);
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        /// <summary>Light sources and decor are dragged in the Scene view (over the preview).</summary>
        private void OnSceneGUI(SceneView view)
        {
            if (LevelSceneHandles.Draw(_state))
                Repaint();
        }

        private void Notify(string message) => ShowNotification(new GUIContent(message), 2.5);

        private void OnUndoRedo()
        {
            if (!_state.HasUsableLevel)
                return;

            // Undo of Generate New can change the size; keep the view and selection meaningful.
            if (_state.SelectedCell.HasValue && !_state.Level.Geometry.IsInside(_state.SelectedCell.Value))
                _state.ClearSelection();

            _state.Revalidate();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_state.Level == null)
            {
                EditorGUILayout.HelpBox("Select a LevelData asset or create a new one.", MessageType.Info);
                return;
            }

            if (!_state.HasUsableLevel)
            {
                EditorGUILayout.HelpBox("Level geometry is corrupted. Use Generate New to rebuild it.", MessageType.Error);
                _generation.OnGUI(_state);
                return;
            }

            HandleShortcuts();
            _tools.EditingEnabled = _tab == EditTab;

            EditorGUILayout.BeginHorizontal();
            DrawPanel();
            DrawCanvas();
            EditorGUILayout.EndHorizontal();

            DrawStatusBar();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUI.BeginChangeCheck();
            var level = (LevelData)EditorGUILayout.ObjectField(_level, typeof(LevelData), false, GUILayout.Width(240f));
            if (EditorGUI.EndChangeCheck())
                SelectLevel(level);

            if (GUILayout.Button("New Level", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                var created = LevelEditorCommands.CreateLevelAsset();
                if (created != null)
                    SelectLevel(created);
            }

            using (new EditorGUI.DisabledScope(!_state.HasUsableLevel))
            {
                if (GUILayout.Button("Ping", EditorStyles.toolbarButton, GUILayout.Width(36f)))
                    EditorGUIUtility.PingObject(_level);
                if (GUILayout.Button("Fit", EditorStyles.toolbarButton, GUILayout.Width(30f)))
                    _state.FitRequested = true;
                _canvas.HandMode = GUILayout.Toggle(_canvas.HandMode,
                    new GUIContent("Hand (H)", "Drag the scheme with LMB. Hold Space for a temporary hand."),
                    EditorStyles.toolbarButton, GUILayout.Width(62f));

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Build Preview", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                {
                    var missing = LevelPreviewBuilder.Build(_state.Level);
                    Notify(missing == 0 ? "Preview built in the scene." : $"Preview built; {missing} item(s) have no visual.");
                }

                if (GUILayout.Button("Clear Preview", EditorStyles.toolbarButton, GUILayout.Width(85f)))
                    LevelPreviewBuilder.Clear();

                var dev = LevelSync.IsDevLevel(_state.Level);
                if (GUILayout.Toggle(dev, new GUIContent("Dev", "Development level: listed in the DEV section of the level select " +
                        "(editor and development builds only), not in release builds and not in the progression."),
                        EditorStyles.toolbarButton, GUILayout.Width(34f)) != dev)
                {
                    LevelSync.SetDevLevel(_state.Level, !dev);
                    Notify(!dev ? "Moved to development levels." : "Moved to game levels.");
                }

                if (GUILayout.Button("Build / Sync", EditorStyles.toolbarButton, GUILayout.Width(80f)) && LevelSync.Sync(_state.Level))
                    Notify("Level synced.");

                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button(new GUIContent("Play", "Validate, Build / Sync and play the level from a random start, as in the game."),
                            EditorStyles.toolbarButton, GUILayout.Width(40f)))
                        Play(null);

                    if (GUILayout.Button(new GUIContent("Test from Start ▾", "Play the level from a chosen player start (ТЗ §61)."),
                            EditorStyles.toolbarDropDown, GUILayout.Width(110f)))
                        ShowStartMenu();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ShowStartMenu()
        {
            var menu = new GenericMenu();
            var starts = _state.Level.PlayerStarts;
            if (starts.Count == 0)
                menu.AddDisabledItem(new GUIContent("The level has no player start"));
            for (var i = 0; i < starts.Count; i++)
            {
                var index = i;
                menu.AddItem(new GUIContent($"Start #{i}  {starts[i].Id} {starts[i].Position}"), false, () => Play(index));
            }

            menu.ShowAsContext();
        }

        /// <param name="startIndex">Null = random start.</param>
        private void Play(int? startIndex)
        {
            if (!LevelPlayLauncher.Launch(_state.Level, startIndex, out var problem))
            {
                Notify(problem);
                Debug.LogWarning("[Maze] Test level: " + problem);
            }
        }

        private void HandleShortcuts()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField)
                return;

            if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && _state.SelectedEntity != null)
            {
                var entity = _state.SelectedEntity;
                LevelEditorCommands.Modify(_state.Level, "Delete " + entity.Id, () => LevelEditing.Remove(_state.Level, entity));
                _state.SelectedEntityId = null;
                _state.Revalidate();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                _tools.Tool = EditTool.Select;
                _tools.PendingKeyDoorId = null;
                _canvas.HandMode = false;
                e.Use();
            }
            else if (e.keyCode == KeyCode.H && !e.control && !e.command)
            {
                _canvas.HandMode = !_canvas.HandMode;
                e.Use();
            }
        }

        private void OnLostFocus() => _canvas.ResetTemporaryModes();

        private void SelectLevel(LevelData level)
        {
            _level = level;
            _state.SetLevel(level);
        }

        private void DrawPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(PanelWidth));
            _tab = GUILayout.Toolbar(_tab, TabNames);
            _panelScroll = EditorGUILayout.BeginScrollView(_panelScroll);

            switch (_tab)
            {
                case 0: _generation.OnGUI(_state); break;
                case EditTab: _edit.OnGUI(_state, _tools); break;
                case 2: _visuals.OnGUI(_state); break;
                default: _validation.OnGUI(_state); break;
            }

            EditorGUILayout.Space();
            _inspector.OnGUI();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawCanvas()
        {
            var rect = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            _canvas.Draw(rect, _state, _tools);
            if (_canvas.NeedsRepaint || GUI.changed || Event.current.type == EventType.MouseDrag || Event.current.type == EventType.MouseUp)
                Repaint();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var level = _state.Level;
            if (_state.HoveredCell.HasValue && level.Geometry.IsInside(_state.HoveredCell.Value))
            {
                var cell = _state.HoveredCell.Value;
                var visuals = string.Join("  +  ", CellLayers.All
                    .Where(layer => CellLayers.Exists(level.Geometry.GetCell(cell), layer))
                    .Select(layer =>
                    {
                        var visual = VisualResolver.ResolveCell(level, cell, layer, out var source);
                        return Describe(visual, source);
                    }));
                var decor = VisualResolver.ResolveDecor(level, cell, out var decorSource);
                if (!decor.IsEmpty)
                    visuals += "  +  " + Describe(decor, decorSource);
                var entities = string.Join(", ", level.AllEntities().Where(e => e.Position == cell).Select(e => e.Id));
                GUILayout.Label($"{cell}  {level.Geometry.GetCell(cell)}  ·  {visuals}" +
                                (entities.Length > 0 ? $"  ·  {entities}" : string.Empty), EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();
            if (_canvas.IsPanActive)
                GUILayout.Label("Hand: drag to move the scheme", EditorStyles.miniBoldLabel);
            else if (_tab == EditTab)
                GUILayout.Label("Tool: " + _tools.Tool, EditorStyles.miniLabel);

            var report = _state.Report;
            if (report != null)
                GUILayout.Label(report.IsValid
                    ? $"Valid · {report.WarningCount} warning(s)"
                    : $"{report.ErrorCount} error(s) · {report.WarningCount} warning(s)", EditorStyles.miniLabel);

            GUILayout.Label($"{level.Geometry.Width}x{level.Geometry.Height}", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private static string Describe(VisualChoice choice, VisualSource source) =>
            choice.IsEmpty ? "no visual" : $"{choice.VariantId} r{choice.Rotation} [{source}]";
    }
}
