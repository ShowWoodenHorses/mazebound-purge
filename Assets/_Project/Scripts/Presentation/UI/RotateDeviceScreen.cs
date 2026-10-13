using System;
using UnityEngine;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// "Rotate your device" over everything while a phone or tablet browser is held in portrait (the game is
    /// landscape only; a browser page cannot lock the orientation, an Android app does). Watches the screen size every
    /// frame (two ints, no allocations); <see cref="ScreenRouter"/> pauses a running level when it shows.
    /// </summary>
    public sealed class RotateDeviceScreen : MonoBehaviour
    {
        [Tooltip("The overlay (blocks touches under it); this object stays active to keep watching.")]
        [SerializeField] private GameObject _content;

        public bool IsShown { get; private set; }

        public event Action<bool> Changed;

        private void Awake()
        {
            if (_content != null) _content.SetActive(false);
        }

        private void Update()
        {
            var show = HudScreen.IsTouchAvailable && Screen.height > Screen.width;
            if (show == IsShown) return;
            IsShown = show;
            if (_content != null) _content.SetActive(show);
            Changed?.Invoke(show);
        }
    }
}
