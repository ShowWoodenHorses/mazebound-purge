using System.Collections.Generic;
using System.Linq;
using Maze.Core.Audio;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Dropdown for <see cref="WeaponSoundGroupAttribute"/> fields: "Default" (empty: the folder named by the weapon's
    /// id, else the common sounds) and every group folder under Sounds/Weapon. A value whose folder is gone is kept
    /// and shown as missing, so it is noticed rather than silently lost.
    /// </summary>
    [CustomPropertyDrawer(typeof(WeaponSoundGroupAttribute))]
    internal sealed class WeaponSoundGroupDrawer : PropertyDrawer
    {
        private const string DefaultLabel = "Default (by id, else common)";

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var groups = Groups();
            var current = property.stringValue ?? string.Empty;
            var missing = current.Length > 0 && !groups.Contains(current);

            var options = new List<string> { DefaultLabel };
            options.AddRange(groups);
            if (missing) options.Add(current + " (missing folder)");

            var index = current.Length == 0 ? 0 : missing ? options.Count - 1 : groups.IndexOf(current) + 1;
            using (new EditorGUI.PropertyScope(position, label, property))
            {
                var color = GUI.color;
                if (missing) GUI.color = new Color(1f, 0.6f, 0.5f);
                EditorGUI.BeginChangeCheck();
                var chosen = EditorGUI.Popup(position, label, index, options.Select(o => new GUIContent(o)).ToArray());
                GUI.color = color;
                if (EditorGUI.EndChangeCheck() && chosen != index)
                    property.stringValue = chosen == 0 ? string.Empty
                        : chosen <= groups.Count ? groups[chosen - 1]
                        : current;
            }
        }

        /// <summary>Names of the folders right under Sounds/Weapon, sorted.</summary>
        private static List<string> Groups() =>
            AssetDatabase.IsValidFolder(AudioBuilder.WeaponGroupsFolder)
                ? AssetDatabase.GetSubFolders(AudioBuilder.WeaponGroupsFolder)
                    .Select(path => path.Substring(path.LastIndexOf('/') + 1))
                    .OrderBy(name => name, System.StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();
    }
}
