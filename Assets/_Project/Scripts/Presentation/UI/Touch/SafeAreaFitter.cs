using Maze.Application.Platform;
using UnityEngine;

namespace Maze.Presentation.UI.Touch
{
    /// <summary>
    /// Fits a RectTransform into <see cref="Screen.safeArea"/> (notches, rounded corners) narrowed by the page's insets
    /// (<see cref="WebPage.Insets"/>). Its parent must cover the whole screen. Follows resolution changes.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private Rect _appliedArea;
        private Vector2Int _appliedScreen;

        private void OnEnable() => Apply();

        private void Update() => Apply();

        private void Apply()
        {
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (screen.x <= 0 || screen.y <= 0) return;
            // Plus the page's insets: system bars a site draws over the game (the VK app), CSS safe areas.
            var area = WebPage.SafeArea(Screen.safeArea, screen.x, screen.y);
            if (area == _appliedArea && screen == _appliedScreen) return;
            _appliedArea = area;
            _appliedScreen = screen;

            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(area.xMin / screen.x, area.yMin / screen.y);
            rect.anchorMax = new Vector2(area.xMax / screen.x, area.yMax / screen.y);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
