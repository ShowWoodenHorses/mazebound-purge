using System.Collections.Generic;
using System.Linq;
using Purge.Core.Content;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Content
{
    /// <summary>The stable id of a content asset: shown, never edited by hand.</summary>
    [CustomPropertyDrawer(typeof(StableIdAttribute))]
    internal sealed class StableIdDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.PropertyField(position, property, label);
        }
    }

    /// <summary>
    /// Dropdown for <see cref="ContentRefAttribute"/> string fields: "None" and the stable ids of every asset of the
    /// type (shown with their file names). A value whose asset is gone is kept and shown as missing.
    /// </summary>
    [CustomPropertyDrawer(typeof(ContentRefAttribute))]
    internal sealed class ContentRefDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var type = ((ContentRefAttribute)attribute).Type;
            var items = ContentIdAssigner.Cached()
                .Where(item => item != null && (type == null || type.IsInstanceOfType(item)))
                .Where(item => !string.IsNullOrEmpty(item.Id))
                .OrderBy(item => item.Id, System.StringComparer.Ordinal)
                .ToList();
            var current = property.stringValue ?? string.Empty;
            var index = items.FindIndex(item => item.Id == current);
            var missing = current.Length > 0 && index < 0;

            var options = new List<GUIContent> { new GUIContent("None") };
            options.AddRange(items.Select(item => new GUIContent($"{item.Id}  ({item.name})")));
            if (missing) options.Add(new GUIContent(current + " (missing)"));
            var selected = current.Length == 0 ? 0 : missing ? options.Count - 1 : index + 1;

            using (new EditorGUI.PropertyScope(position, label, property))
            {
                var color = GUI.color;
                if (missing) GUI.color = new Color(1f, 0.6f, 0.5f);
                EditorGUI.BeginChangeCheck();
                var chosen = EditorGUI.Popup(position, label, selected, options.ToArray());
                GUI.color = color;
                if (EditorGUI.EndChangeCheck() && chosen != selected)
                    property.stringValue = chosen == 0 ? string.Empty : chosen <= items.Count ? items[chosen - 1].Id : current;
            }
        }
    }
}
