using System.Collections.Generic;
using Purge.Core.Content;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Content
{
    /// <summary>
    /// Purge → Dev → Build Content Catalog: fixes missing or shared stable ids, then lists every
    /// <see cref="ContentDefinition"/> of the project in <see cref="ContentCatalog"/> (sorted by path). Also run by
    /// Build / Sync, so a new asset in a content folder is in the game without any manual step.
    /// </summary>
    internal static class ContentCatalogBuilder
    {
        public const string CatalogPath = "Assets/_Project/Data/Content/ContentCatalog.asset";

        [MenuItem("Purge/Dev/Build Content Catalog")]
        private static void BuildFromMenu() => Build(log: true);

        public static ContentCatalog Build(bool log)
        {
            ContentIdAssigner.Assign(ContentIdAssigner.FindAll());

            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(CatalogPath);
            if (catalog == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/Content"))
                    AssetDatabase.CreateFolder("Assets/_Project/Data", "Content");
                catalog = ScriptableObject.CreateInstance<ContentCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var items = ContentIdAssigner.FindAll();
            var list = catalog.MutableItems;
            var same = list.Count == items.Count;
            for (var i = 0; same && i < items.Count; i++)
                same = list[i] == items[i];
            if (!same)
            {
                list.Clear();
                list.AddRange(items);
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
            }

            var problems = new List<string>();
            catalog.Validate(problems);
            foreach (var problem in problems)
                Debug.LogError("[Purge] Content catalog: " + problem);
            if (log)
                Debug.Log($"[Purge] Content catalog: {items.Count} item(s), {problems.Count} problem(s).");
            return catalog;
        }
    }
}
