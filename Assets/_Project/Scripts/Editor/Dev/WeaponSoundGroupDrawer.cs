using System.Collections.Generic;
using System.Linq;
using Purge.Core.Audio;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Dev
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

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) =>
            FolderPopup.Draw(position, property, label, DefaultLabel, AudioBuilder.WeaponGroups());
    }

    /// <summary>Dropdown for <see cref="WeaponSoundPartAttribute"/> fields: "None" and every folder under Sounds/Weapon/_Parts.</summary>
    [CustomPropertyDrawer(typeof(WeaponSoundPartAttribute))]
    internal sealed class WeaponSoundPartDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) =>
            FolderPopup.Draw(position, property, label, "None", AudioBuilder.WeaponParts());
    }

    internal static class FolderPopup
    {
        public static void Draw(Rect position, SerializedProperty property, GUIContent label, string emptyLabel, List<string> names)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var current = property.stringValue ?? string.Empty;
            var missing = current.Length > 0 && !names.Contains(current);

            var options = new List<string> { emptyLabel };
            options.AddRange(names);
            if (missing) options.Add(current + " (missing folder)");

            var index = current.Length == 0 ? 0 : missing ? options.Count - 1 : names.IndexOf(current) + 1;
            using (new EditorGUI.PropertyScope(position, label, property))
            {
                var color = GUI.color;
                if (missing) GUI.color = new Color(1f, 0.6f, 0.5f);
                EditorGUI.BeginChangeCheck();
                var chosen = EditorGUI.Popup(position, label, index, options.Select(o => new GUIContent(o)).ToArray());
                GUI.color = color;
                if (EditorGUI.EndChangeCheck() && chosen != index)
                    property.stringValue = chosen == 0 ? string.Empty
                        : chosen <= names.Count ? names[chosen - 1]
                        : current;
            }
        }
    }
}
