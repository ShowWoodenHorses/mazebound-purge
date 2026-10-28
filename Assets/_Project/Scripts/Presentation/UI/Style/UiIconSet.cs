using UnityEngine;

namespace Purge.Presentation.UI.Style
{
    /// <summary>
    /// White line icons of the UI (one line width, tinted by the Image colour), drawn by Purge → Dev → Build UI Icons.
    /// Screens get the sprites they need when <c>RuntimeScenesBuilder</c> builds them; weapon icons are silhouettes of
    /// the models, kept in the weapon visual catalog. Any sprite may be replaced by hand (the builder overwrites PNGs).
    /// </summary>
    [CreateAssetMenu(menuName = "Purge/UI/Icon Set", fileName = "UiIcons")]
    public sealed class UiIconSet : ScriptableObject
    {
        public Sprite Map;
        public Sprite Pause;
        public Sprite Settings;
        public Sprite Door;
        public Sprite PickUp;
        public Sprite Key;
        public Sprite Medkit;
        public Sprite Skull;
        public Sprite Melee;
        public Sprite Ranged;
        public Sprite Attack;
        public Sprite Grenade;
        public Sprite Star;
        public Sprite StarFilled;
        public Sprite Lock;
        public Sprite Heart;
        public Sprite HeartFill;
        public Sprite ChevronLeft;
        public Sprite ChevronRight;
        public Sprite Resume;
        public Sprite Retry;
        public Sprite Menu;
        public Sprite Exit;
        public Sprite Close;
        public Sprite Player;
        public Sprite Search;
        public Sprite Help;
        [Tooltip("Watch an ad: a screen with a play triangle.")]
        public Sprite Ad;
        [Tooltip("Money of the run (stage 6b): a coin.")]
        public Sprite Coin;
        [Tooltip("A filled arrowhead pointing right (+X): arrows towards off-screen monsters.")]
        public Sprite Pointer;

        [Tooltip("A car from the side (Use: get into a vehicle or out of it, stage 9a).")]
        public Sprite Vehicle;
        [Tooltip("A vehicle's throttle: a pedal with an arrow up.")]
        public Sprite Gas;
        [Tooltip("A vehicle's brake and reverse: a pedal with an arrow down.")]
        public Sprite Brake;

        [Tooltip("Background of the menu screens: a faint maze with a torch glow and a vignette.")]
        public Texture2D Backdrop;
    }
}
