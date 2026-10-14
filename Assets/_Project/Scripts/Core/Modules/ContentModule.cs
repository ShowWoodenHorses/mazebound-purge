using System;
using System.Collections.Generic;
using UnityEngine;

namespace Purge.Core.Modules
{
    /// <summary>
    /// A piece of mechanics that content is assembled from in the inspector (ТЗ: «Контент — это данные»): weapons,
    /// throwables, monster abilities, cards, states and zones are lists of modules; code is written once per module,
    /// a new item is an asset with modules and numbers. Every family has its own abstract base (e.g. a weapon module)
    /// and content keeps a <c>[SerializeReference, ModuleList] List&lt;FamilyBase&gt;</c> field; the inspector offers
    /// every concrete module of the family (<see cref="ModuleTypes"/>).
    /// <para>Renaming or moving a module class loses it in assets unless the class gets
    /// <c>[UnityEngine.Scripting.APIUpdating.MovedFrom]</c> with the old name.</para>
    /// </summary>
    [Serializable]
    public abstract class ContentModule
    {
        /// <summary>Problems of the module's settings, in English (editor checks; nothing by default).</summary>
        public virtual void Validate(List<string> problems)
        {
        }
    }

    /// <summary>How a module class appears in the "Add module" menu.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ModuleInfoAttribute : Attribute
    {
        public ModuleInfoAttribute(string displayName) => DisplayName = displayName;

        public string DisplayName { get; }

        /// <summary>Submenu of the "Add module" menu (e.g. "Attack", "Effect"); empty = top level.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Whether one list may hold several modules of this class (e.g. several effects, but one shape).</summary>
        public bool AllowMultiple { get; set; }
    }

    /// <summary>
    /// Marks a <c>[SerializeReference]</c> list of modules: the inspector draws it with an "Add module" menu of the
    /// concrete classes of the list's element type, and remove / move buttons.
    /// </summary>
    public sealed class ModuleListAttribute : PropertyAttribute
    {
    }
}
