using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace Maze.Editor.Build
{
    /// <summary>
    /// Keeps the Android SDKs' scripts (VK Ad Network, MyTracker) out of every non-Android player: nothing uses
    /// them there (they and <c>Maze.Application.Android</c> are compiled only for Android and the editor; a filter in
    /// case that changes: the web download must not grow).
    /// </summary>
    internal sealed class AndroidSdkAssemblyFilter : IFilterBuildAssemblies
    {
        private static readonly string[] AndroidOnly = { "MyTarget", "MyTracker" };

        public int callbackOrder => 0;

        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return assemblies;
            return assemblies.Where(path => !AndroidOnly.Contains(Path.GetFileNameWithoutExtension(path))).ToArray();
        }
    }
}
