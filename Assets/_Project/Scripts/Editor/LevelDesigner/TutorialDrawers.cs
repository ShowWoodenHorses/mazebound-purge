using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Maze.Core.Level;
using Maze.Core.Localization;
using Maze.Editor.Dev;
using Maze.Presentation.Localization;
using UnityEditor;
using UnityEngine;

namespace Maze.Editor.LevelDesigner
{
    /// <summary>
    /// Dropdown for <see cref="TutorialTextAttribute"/>: the step keys of the translation table (tutorial.*, without
    /// their .touch / .hint variants) with the English text, so a key cannot be mistyped. A key missing from the table
    /// is kept and shown as missing.
    /// </summary>
    [CustomPropertyDrawer(typeof(TutorialTextAttribute))]
    internal sealed class TutorialTextDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var keys = StepKeys(out var texts);
            var current = property.stringValue ?? string.Empty;
            var options = new List<string> { "(none)" };
            options.AddRange(keys.Select((k, i) => Label(k, texts[i])));
            var index = current.Length == 0 ? 0 : keys.IndexOf(current) + 1;
            var missing = current.Length > 0 && index == 0;
            if (missing)
            {
                options.Add(current + " (missing in Strings.tsv)");
                index = options.Count - 1;
            }

            using (new EditorGUI.PropertyScope(position, label, property))
            {
                var color = GUI.color;
                if (missing) GUI.color = new Color(1f, 0.6f, 0.5f);
                EditorGUI.BeginChangeCheck();
                var chosen = EditorGUI.Popup(position, label, index, options.Select(o => new GUIContent(o)).ToArray());
                GUI.color = color;
                if (EditorGUI.EndChangeCheck() && chosen != index)
                    property.stringValue = chosen == 0 ? string.Empty : chosen <= keys.Count ? keys[chosen - 1] : current;
            }
        }

        /// <summary>"move — Move with the stick…" (slashes would make submenus).</summary>
        private static string Label(string key, string text)
        {
            var name = key.Substring(TextKeys.TutorialPrefix.Length);
            if (string.IsNullOrEmpty(text)) return name;
            text = text.Replace('/', '∕').Replace("\\n", " ");
            return name + " — " + (text.Length > 60 ? text.Substring(0, 60) + "…" : text);
        }

        private static List<string> StepKeys(out List<string> texts)
        {
            var keys = new List<string>();
            texts = new List<string>();
            var path = LocalizationBuilder.SourcePath;
            if (!File.Exists(path)) return keys;
            var source = LocalizationSource.Parse(File.ReadAllText(path, Encoding.UTF8));
            foreach (var row in source.Rows)
            {
                var key = row.Key;
                if (!key.StartsWith(TextKeys.TutorialPrefix) || key.EndsWith(TextKeys.TouchSuffix) ||
                    key.EndsWith(TextKeys.HintSuffix))
                    continue;
                keys.Add(key);
                texts.Add(row.Texts.Length > 0 ? row.Texts[0] : string.Empty);
            }

            return keys;
        }
    }

    /// <summary>
    /// Dropdown for <see cref="LevelEntityRefAttribute"/> in a <see cref="TutorialScript"/>: the objects of the level that
    /// uses the script ("key_1 (Key at 4,7)"). A target gone from the level is kept and shown as missing.
    /// </summary>
    [CustomPropertyDrawer(typeof(LevelEntityRefAttribute))]
    internal sealed class LevelEntityRefDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var level = property.propertyType == SerializedPropertyType.String ? LevelOf(property.serializedObject.targetObject) : null;
            if (level == null)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var entities = level.AllEntities().Where(e => !(e is PlayerStartData)).ToList();
            var current = property.stringValue ?? string.Empty;
            var options = new List<string> { "(none)" };
            options.AddRange(entities.Select(e => $"{e.Id} ({Kind(e)} at {e.Position.X},{e.Position.Y})"));
            var index = current.Length == 0 ? 0 : entities.FindIndex(e => e.Id == current) + 1;
            var missing = current.Length > 0 && index == 0;
            if (missing)
            {
                options.Add(current + " (missing in the level)");
                index = options.Count - 1;
            }

            using (new EditorGUI.PropertyScope(position, label, property))
            {
                var color = GUI.color;
                if (missing) GUI.color = new Color(1f, 0.6f, 0.5f);
                EditorGUI.BeginChangeCheck();
                var chosen = EditorGUI.Popup(position, label, index, options.Select(o => new GUIContent(o)).ToArray());
                GUI.color = color;
                if (EditorGUI.EndChangeCheck() && chosen != index)
                    property.stringValue = chosen == 0 ? string.Empty : chosen <= entities.Count ? entities[chosen - 1].Id : current;
            }
        }

        private static string Kind(LevelEntityData entity) => entity switch
        {
            DoorData door => door.RequiresKey ? "Locked door" : "Door",
            ExitData _ => "Exit",
            ZombieSpawnData _ => "Zombie",
            WeaponPickupData weapon => weapon.Definition != null ? weapon.Definition.name : "Weapon",
            KeyData _ => "Key",
            MedkitData _ => "Medkit",
            MapFragmentData _ => "Map fragment",
            _ => entity.GetType().Name,
        };

        private static TutorialScript _cachedScript;
        private static LevelData _cachedLevel;

        /// <summary>The level whose Tutorial is <paramref name="target"/> (cached: drawn every repaint).</summary>
        private static LevelData LevelOf(Object target)
        {
            if (!(target is TutorialScript script)) return null;
            if (_cachedScript == script && _cachedLevel != null && _cachedLevel.Tutorial == script) return _cachedLevel;

            _cachedScript = script;
            _cachedLevel = null;
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(LevelData)))
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid));
                if (level == null || level.Tutorial != script) continue;
                _cachedLevel = level;
                break;
            }

            return _cachedLevel;
        }
    }
}
