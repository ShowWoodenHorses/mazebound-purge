using System.Collections.Generic;

namespace Purge.Core.Modules
{
    /// <summary>
    /// Queries and checks over a content's module list (no allocations in the queries). Any family list fits
    /// (<c>List&lt;WeaponModule&gt;</c> is an <c>IReadOnlyList&lt;ContentModule&gt;</c>): <c>Modules.TryGet(list, out
    /// ShotModule shot)</c>.
    /// </summary>
    public static class Modules
    {
        /// <summary>The first module of type <typeparamref name="T"/>.</summary>
        public static bool TryGet<T>(IReadOnlyList<ContentModule> modules, out T module) where T : ContentModule
        {
            if (modules != null)
                for (var i = 0; i < modules.Count; i++)
                    if (modules[i] is T found)
                    {
                        module = found;
                        return true;
                    }

            module = null;
            return false;
        }

        public static bool Has<T>(IReadOnlyList<ContentModule> modules) where T : ContentModule => TryGet<T>(modules, out _);

        /// <summary>Every module of type <typeparamref name="T"/>, in list order.</summary>
        public static void GetAll<T>(IReadOnlyList<ContentModule> modules, List<T> result) where T : ContentModule
        {
            result.Clear();
            if (modules == null) return;
            for (var i = 0; i < modules.Count; i++)
                if (modules[i] is T found)
                    result.Add(found);
        }

        /// <summary>
        /// Problems of the list, in English, prefixed by <paramref name="owner"/>: lost modules (a class renamed without
        /// MovedFrom or deleted), two modules of a class that allows one, and each module's own
        /// <see cref="ContentModule.Validate"/>.
        /// </summary>
        public static void Validate(IReadOnlyList<ContentModule> modules, string owner, List<string> problems)
        {
            if (modules == null) return;
            var own = new List<string>();
            for (var i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                if (module == null)
                {
                    problems.Add($"{owner}: module #{i + 1} is missing (its class was renamed or deleted).");
                    continue;
                }

                var type = module.GetType();
                if (!ModuleTypes.AllowMultiple(type))
                    for (var j = 0; j < i; j++)
                        if (modules[j] != null && modules[j].GetType() == type)
                        {
                            problems.Add($"{owner}: '{ModuleTypes.DisplayName(type)}' is added twice.");
                            break;
                        }

                own.Clear();
                module.Validate(own);
                foreach (var problem in own)
                    problems.Add($"{owner}, {ModuleTypes.DisplayName(type)}: {problem}");
            }
        }
    }
}
