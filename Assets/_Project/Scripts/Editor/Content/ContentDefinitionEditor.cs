using System;
using System.Collections.Generic;
using System.Reflection;
using Purge.Core.Content;
using Purge.Core.Modules;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Content
{
    /// <summary>
    /// Inspector of every content asset: the usual fields, and each <see cref="ModuleListAttribute"/> list drawn as
    /// module boxes (title, move up/down, remove) with an "Add module" menu of the concrete classes of the list's family
    /// (<see cref="ModuleTypes"/>; a class that allows one module is greyed out once added). Lost modules (class renamed
    /// without MovedFrom, deleted) are shown so they can be removed.
    /// </summary>
    [CustomEditor(typeof(ContentDefinition), true)]
    internal sealed class ContentDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var property = serializedObject.GetIterator();
            var enter = true;
            while (property.NextVisible(enter))
            {
                enter = false;
                if (property.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(property);
                    continue;
                }

                var family = ModuleFamily(target.GetType(), property);
                if (family != null)
                    DrawModuleList(property, family);
                else
                    EditorGUILayout.PropertyField(property, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>The element type of a <c>[ModuleList]</c> list field, null for any other property.</summary>
        internal static Type ModuleFamily(Type owner, SerializedProperty property)
        {
            if (!property.isArray || property.propertyType == SerializedPropertyType.String) return null;
            for (var type = owner; type != null && type != typeof(ScriptableObject); type = type.BaseType)
            {
                var field = type.GetField(property.name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                if (field.GetCustomAttribute<ModuleListAttribute>() == null) return null;
                var fieldType = field.FieldType;
                if (fieldType.IsArray) return fieldType.GetElementType();
                return fieldType.IsGenericType ? fieldType.GetGenericArguments()[0] : null;
            }

            return null;
        }

        private static void DrawModuleList(SerializedProperty list, Type family)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(list.displayName, EditorStyles.boldLabel);
            var present = new HashSet<Type>();
            for (var i = 0; i < list.arraySize; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                var value = element.managedReferenceValue;
                if (value != null) present.Add(value.GetType());

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var title = value != null ? ModuleTypes.DisplayName(value.GetType()) : "Missing module (class renamed or deleted)";
                        var color = GUI.color;
                        if (value == null) GUI.color = new Color(1f, 0.6f, 0.5f);
                        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                        GUI.color = color;

                        using (new EditorGUI.DisabledScope(i == 0))
                            if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(24f)))
                            {
                                list.MoveArrayElement(i, i - 1);
                                break;
                            }

                        using (new EditorGUI.DisabledScope(i == list.arraySize - 1))
                            if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(24f)))
                            {
                                list.MoveArrayElement(i, i + 1);
                                break;
                            }

                        if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(24f)))
                        {
                            element.managedReferenceValue = null;
                            list.DeleteArrayElementAtIndex(i);
                            break;
                        }
                    }

                    if (value == null) continue;
                    var child = element.Copy();
                    var end = element.GetEndProperty();
                    EditorGUI.indentLevel++;
                    if (child.NextVisible(true))
                        while (!SerializedProperty.EqualContents(child, end))
                        {
                            EditorGUILayout.PropertyField(child, true);
                            if (!child.NextVisible(false)) break;
                        }
                    EditorGUI.indentLevel--;
                }
            }

            var types = ModuleTypes.Concrete(family);
            if (GUILayout.Button(types.Count == 0 ? "No modules of this kind yet" : "Add module ▾"))
            {
                var menu = new GenericMenu();
                foreach (var type in types)
                {
                    var category = ModuleTypes.CategoryOf(type);
                    var label = new GUIContent((category.Length > 0 ? category + "/" : string.Empty) + ModuleTypes.DisplayName(type));
                    if (!ModuleTypes.AllowMultiple(type) && present.Contains(type))
                    {
                        menu.AddDisabledItem(label);
                        continue;
                    }

                    var serializedObject = list.serializedObject;
                    var path = list.propertyPath;
                    menu.AddItem(label, false, () =>
                    {
                        serializedObject.Update();
                        var target = serializedObject.FindProperty(path);
                        target.arraySize++;
                        target.GetArrayElementAtIndex(target.arraySize - 1).managedReferenceValue = Activator.CreateInstance(type);
                        serializedObject.ApplyModifiedProperties();
                    });
                }

                menu.ShowAsContext();
            }
        }
    }
}
