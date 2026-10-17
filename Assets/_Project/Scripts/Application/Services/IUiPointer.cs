using UnityEngine;

namespace Purge.Application.Services
{
    /// <summary>
    /// Whether the mouse / pointer is over an element of the UI (a button, a panel) — so a click there is not also a
    /// gameplay action. Implemented by the presentation layer.
    /// </summary>
    public interface IUiPointer
    {
        /// <summary>Call from regular frame code, not from input callbacks (the UI state there is a frame old).</summary>
        bool IsOverUi { get; }

        /// <summary>A raycast target of the UI (on-screen controls too) is at the screen point.</summary>
        bool IsOverUiAt(Vector2 screenPosition);
    }
}
