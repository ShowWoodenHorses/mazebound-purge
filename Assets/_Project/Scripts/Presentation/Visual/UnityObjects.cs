namespace Maze.Presentation.Visual
{
    internal static class UnityObjects
    {
        /// <summary>Destroy in play mode, DestroyImmediate in edit mode (EditMode tests, tools).</summary>
        public static void Destroy(UnityEngine.Object target)
        {
            if (target == null) return;
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        /// <summary>
        /// Destroys the object a component is on. Safe when the component is already gone (leaving Play Mode destroys
        /// the scenes before the level scope is disposed; <c>.gameObject</c> of a destroyed component throws).
        /// </summary>
        public static void DestroyObjectOf(UnityEngine.Component component)
        {
            if (component != null) Destroy(component.gameObject);
        }
    }
}
