using Maze.Core.Validation;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>Validation results (ТЗ §90–92). Clicking an issue selects its cell on the grid.</summary>
    internal sealed class ValidationPanel
    {
        private bool _showErrors = true;
        private bool _showWarnings = true;
        private bool _showInfo;
        private GUIStyle _messageStyle;

        public void OnGUI(LevelDesignerState state)
        {
            if (GUILayout.Button("Validate", GUILayout.Height(26f)))
                state.Revalidate();

            var report = state.Report;
            if (report == null)
            {
                EditorGUILayout.HelpBox("No report yet.", MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox(
                report.IsValid
                    ? $"Level is valid. {report.WarningCount} warning(s)."
                    : $"{report.ErrorCount} error(s), {report.WarningCount} warning(s). The level is not ready.",
                report.IsValid ? MessageType.Info : MessageType.Error);

            EditorGUILayout.BeginHorizontal();
            _showErrors = GUILayout.Toggle(_showErrors, $"Errors {report.ErrorCount}", EditorStyles.miniButtonLeft);
            _showWarnings = GUILayout.Toggle(_showWarnings, $"Warnings {report.WarningCount}", EditorStyles.miniButtonMid);
            _showInfo = GUILayout.Toggle(_showInfo, "Info", EditorStyles.miniButtonRight);
            EditorGUILayout.EndHorizontal();

            if (_messageStyle == null)
                _messageStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { richText = false };

            foreach (var issue in report.Issues)
            {
                if (!IsVisible(issue.Severity))
                    continue;

                var focused = issue == state.FocusedIssue;
                EditorGUILayout.BeginHorizontal(focused ? EditorStyles.helpBox : GUIStyle.none);
                GUILayout.Label(Icon(issue.Severity), GUILayout.Width(18f), GUILayout.Height(18f));
                GUILayout.Label($"{issue.Code}: {issue.Message}", _messageStyle);
                if (issue.Position.HasValue && GUILayout.Button("Show", EditorStyles.miniButton, GUILayout.Width(42f)))
                    state.Focus(issue);
                EditorGUILayout.EndHorizontal();
            }
        }

        private bool IsVisible(ValidationSeverity severity)
        {
            switch (severity)
            {
                case ValidationSeverity.Error: return _showErrors;
                case ValidationSeverity.Warning: return _showWarnings;
                default: return _showInfo;
            }
        }

        private static GUIContent Icon(ValidationSeverity severity)
        {
            switch (severity)
            {
                case ValidationSeverity.Error: return EditorGUIUtility.IconContent("console.erroricon.sml");
                case ValidationSeverity.Warning: return EditorGUIUtility.IconContent("console.warnicon.sml");
                default: return EditorGUIUtility.IconContent("console.infoicon.sml");
            }
        }
    }
}
