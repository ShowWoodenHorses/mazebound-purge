using System.Collections.Generic;
using System.Linq;
using Purge.Core.Content;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Content
{
    /// <summary>
    /// Gives every <see cref="ContentDefinition"/> its stable id once: a new asset (created, copied, imported) whose id is
    /// empty, invalid or already taken by another asset gets one made from its file name
    /// (<see cref="ContentIds.FromName"/>, "_2"… when taken). An id that is fine is never changed — renaming or moving
    /// the file keeps it.
    /// </summary>
    internal sealed class ContentIdAssigner : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            List<ContentDefinition> fresh = null;
            foreach (var path in imported)
            {
                if (!path.EndsWith(".asset")) continue;
                if (AssetDatabase.GetMainAssetTypeAtPath(path) is { } type && typeof(ContentDefinition).IsAssignableFrom(type))
                    (fresh ??= new List<ContentDefinition>()).Add(AssetDatabase.LoadAssetAtPath<ContentDefinition>(path));
            }

            if (fresh != null)
                Assign(fresh);
        }

        private static List<ContentDefinition> _cached;

        /// <summary>
        /// <see cref="FindAll"/> remembered until the project changes (inspector dropdowns redraw often).
        /// </summary>
        public static IReadOnlyList<ContentDefinition> Cached()
        {
            if (_cached == null)
            {
                _cached = FindAll();
                EditorApplication.projectChanged -= Forget;
                EditorApplication.projectChanged += Forget;
            }

            return _cached;
        }

        private static void Forget() => _cached = null;

        /// <summary>Every content asset of the project, sorted by path (a stable order for resolving duplicates).</summary>
        public static List<ContentDefinition> FindAll() =>
            AssetDatabase.FindAssets("t:" + nameof(ContentDefinition))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<ContentDefinition>)
                .Where(item => item != null)
                .ToList();

        /// <summary>
        /// Ids for <paramref name="candidates"/> that need one (empty, invalid or shared with another asset); the other
        /// assets keep theirs. Returns how many changed.
        /// </summary>
        public static int Assign(IReadOnlyCollection<ContentDefinition> candidates)
        {
            var all = FindAll();
            var owners = new Dictionary<string, ContentDefinition>(System.StringComparer.Ordinal);
            var candidateSet = new HashSet<ContentDefinition>(candidates.Where(c => c != null));

            // Ids of the other assets are taken first; among candidates, the first by path keeps a shared id.
            foreach (var item in all.Where(i => !candidateSet.Contains(i)).Concat(all.Where(candidateSet.Contains)))
            {
                if (!ContentIds.IsValid(item.Id) || owners.ContainsKey(item.Id))
                    continue;
                owners[item.Id] = item;
            }

            _cached = null;
            var changed = 0;
            foreach (var item in all.Where(candidateSet.Contains))
            {
                if (ContentIds.IsValid(item.Id) && owners.TryGetValue(item.Id, out var owner) && owner == item)
                    continue;

                var id = ContentIds.MakeUnique(ContentIds.FromName(item.name), owners.ContainsKey);
                var old = item.Id;
                item.SetId(id);
                owners[id] = item;
                EditorUtility.SetDirty(item);
                AssetDatabase.SaveAssetIfDirty(item);
                changed++;
                Debug.Log(string.IsNullOrEmpty(old)
                    ? $"[Purge] Content '{AssetDatabase.GetAssetPath(item)}' got the stable id '{id}'."
                    : $"[Purge] Content '{AssetDatabase.GetAssetPath(item)}': id '{old}' was taken or invalid, now '{id}'.");
            }

            return changed;
        }
    }
}
