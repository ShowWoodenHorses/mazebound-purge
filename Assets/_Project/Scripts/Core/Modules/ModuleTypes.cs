using System;
using System.Collections.Generic;
using System.Reflection;

namespace Purge.Core.Modules
{
    /// <summary>Module classes found by reflection (editor menus and checks); results are cached per family.</summary>
    public static class ModuleTypes
    {
        private static readonly Dictionary<Type, IReadOnlyList<Type>> Cache = new Dictionary<Type, IReadOnlyList<Type>>();

        /// <summary>
        /// Concrete module classes assignable to <paramref name="family"/> (itself included when concrete): not abstract,
        /// not generic, serializable, with a parameterless constructor. Sorted by category, then display name.
        /// </summary>
        public static IReadOnlyList<Type> Concrete(Type family)
        {
            if (family == null) throw new ArgumentNullException(nameof(family));
            lock (Cache)
            {
                if (Cache.TryGetValue(family, out var cached)) return cached;

                var result = new List<Type>();
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        types = e.Types;
                    }

                    foreach (var type in types)
                        if (type != null && IsUsable(type, family))
                            result.Add(type);
                }

                result.Sort((a, b) =>
                {
                    var byCategory = string.CompareOrdinal(CategoryOf(a), CategoryOf(b));
                    return byCategory != 0 ? byCategory : string.CompareOrdinal(DisplayName(a), DisplayName(b));
                });
                Cache[family] = result;
                return result;
            }
        }

        public static string DisplayName(Type type) =>
            type.GetCustomAttribute<ModuleInfoAttribute>(false)?.DisplayName ?? type.Name;

        public static string CategoryOf(Type type) =>
            type.GetCustomAttribute<ModuleInfoAttribute>(false)?.Category ?? string.Empty;

        public static bool AllowMultiple(Type type) =>
            type.GetCustomAttribute<ModuleInfoAttribute>(false)?.AllowMultiple ?? false;

        private static bool IsUsable(Type type, Type family) =>
            family.IsAssignableFrom(type) && typeof(ContentModule).IsAssignableFrom(type) &&
            !type.IsAbstract && !type.IsGenericTypeDefinition && !type.ContainsGenericParameters &&
            type.IsSerializable && type.GetConstructor(Type.EmptyTypes) != null;
    }
}
