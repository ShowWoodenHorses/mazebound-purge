using System;
using System.Collections.Generic;
using Maze.Application.Services;
using Maze.Presentation.Localization;
using Maze.Presentation.UI.Shapes;
using Maze.Presentation.UI.Style;
using Maze.Presentation.UI.Touch;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Maze.Presentation.UI
{
    /// <summary>
    /// In-level HUD (design mock-up). Top left: a ring with a heart, the level name, HP as a number and a thin bar in an
    /// arrow-tipped frame — a light "trail" shows the lost part for a moment, low health turns the frame red and makes
    /// it pulse; under it map fragments, zombies killed and the carried keys in their doors' colours. Top centre: a
    /// short message between diamonds. Top right: Map and Pause. Weapon slots: the on-screen cluster on touch devices,
    /// small slots at the bottom right otherwise. A red pulse at the screen edges when hurt.
    /// </summary>
    public sealed class HudScreen : UIScreen
    {
        private const float MessageSeconds = 2f;
        private const float LowHealth = 0.3f;
        private const float TrailDelay = 0.35f;
        private const float TrailSpeed = 0.9f;
        private const float LowPulseSpeed = 5f;

        private readonly char[] _timeChars = new char[TimeFormat.MaxLength];
        private int _timeShown = -1;

        [SerializeField] private UiStyle _style;
        [SerializeField] private TMP_Text _levelName;

        [Tooltip("Play time of the run (top centre).")]
        [SerializeField] private TMP_Text _timer;

        [Header("Health")]
        [SerializeField] private TMP_Text _healthText;
        [SerializeField] private UiShape _healthFrame;
        [SerializeField] private UiShape _heartRing;

        [Tooltip("Bar fill; its right anchor is the health share.")]
        [SerializeField] private RectTransform _healthFill;

        [Tooltip("Light part behind the fill that follows it down after a hit.")]
        [SerializeField] private RectTransform _healthTrail;

        [Header("Counters")]
        [SerializeField] private GameObject _fragments;
        [SerializeField] private TMP_Text _fragmentsText;
        [SerializeField] private GameObject _kills;
        [SerializeField] private TMP_Text _killsText;
        [SerializeField] private RectTransform _keys;
        [SerializeField] private Image _keyTemplate;

        [Header("Message")]
        [SerializeField] private GameObject _messageRow;
        [SerializeField] private TMP_Text _message;

        [Header("Buttons")]
        [SerializeField] private Button _pauseButton;
        [SerializeField] private Button _mapButton;
        private HorizontalShift _topRightShift;

        [Tooltip("On-screen stick and buttons (ТЗ §64, Android): a separate canvas, shown while the HUD is. " +
                 "They drive gamepad controls through the Input System.")]
        [SerializeField] private TouchControls _touchControls;

        [Tooltip("Weapon slots for devices without the on-screen controls.")]
        [SerializeField] private GameObject _weaponPanel;
        [SerializeField] private HudWeaponSlot _meleeSlot;
        [SerializeField] private HudWeaponSlot _rangedSlot;

        [Header("Damage pulse")]
        [Tooltip("Full-screen image under the HUD: red at the screen edges when the player is hurt. Its texture is made on start.")]
        [SerializeField] private RawImage _damageFlash;

        [SerializeField] private Color _damageColor = new Color(0.85f, 0.04f, 0.04f, 0.5f);
        [SerializeField, Min(0.05f)] private float _damageTime = 0.6f;

        private const int VignetteSize = 64;
        private const float DamageRise = 0.06f;

        private readonly List<Image> _keyImages = new List<Image>();
        private float _messageHideTime;
        private float _damageElapsed = float.MaxValue;
        private Texture2D _vignette;
        private int _health = -1;
        private int _maxHealth = -1;
        private float _healthShare = 1f;
        private float _trailShare = 1f;
        private float _trailWait;
        private bool _low;
        private int _fragmentsShown = -1;
        private int _fragmentsTotal = -1;
        private int _killsShown = -1;
        private int _killsTotal = -1;

        /// <summary>A real touch was seen on a platform that is not mobile (e.g. a desktop browser on a touchscreen).</summary>
        private static bool _touchSeen;

        public event Action PauseClicked;
        public event Action MapClicked;

        /// <summary>
        /// Touch controls are shown at once on mobile (including mobile browsers). Elsewhere a touchscreen device may be
        /// reported without one (desktop browsers expose touch support), so they appear only after a real touch.
        /// </summary>
        public static bool IsTouchAvailable => UnityEngine.Application.isMobilePlatform;

        /// <summary>The on-screen controls are used: a mobile platform, or a touch was seen (desktop touchscreen).</summary>
        public static bool TouchShown => IsTouchAvailable || _touchSeen;

        private void Awake()
        {
            Bind(_pauseButton, () => PauseClicked?.Invoke());
            Bind(_mapButton, () => MapClicked?.Invoke());
            if (_keyTemplate != null) _keyTemplate.gameObject.SetActive(false);
            if (_messageRow != null) _messageRow.SetActive(false);

            if (_damageFlash != null)
            {
                _vignette = CreateVignette();
                _damageFlash.texture = _vignette;
                _damageFlash.raycastTarget = false;
                _damageFlash.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_vignette != null) Destroy(_vignette);
        }

        private void OnEnable() => RefreshTouchControls();

        private void OnDisable()
        {
            if (_touchControls != null) _touchControls.SetShown(false);
        }

        private void Update()
        {
            if (!IsTouchAvailable && !_touchSeen)
            {
                var touch = Touchscreen.current;
                if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                {
                    _touchSeen = true;
                    RefreshTouchControls();
                }
            }

            if (_messageRow != null && _messageRow.activeSelf && Time.unscaledTime >= _messageHideTime)
                _messageRow.SetActive(false);

            UpdateHealthBar(Time.unscaledDeltaTime);
            UpdateDamagePulse();
        }

        // ---- health ----

        public void SetHealth(int current, int max)
        {
            if (current == _health && max == _maxHealth) return;
            var first = _health < 0;
            var hurt = !first && current < _health;
            _health = current;
            _maxHealth = max;
            _healthShare = max > 0 ? Mathf.Clamp01(current / (float)max) : 0f;
            if (_healthText != null) _healthText.text = current + " / " + max;

            if (hurt) _trailWait = TrailDelay;
            else _trailShare = _healthShare; // Healed or first shown: no trail.
            SetShare(_healthFill, _healthShare);
            SetShare(_healthTrail, _trailShare);

            _low = _healthShare <= LowHealth && current > 0;
            PaintHealth(1f);
        }

        private void UpdateHealthBar(float deltaTime)
        {
            if (_trailShare > _healthShare)
            {
                if (_trailWait > 0f) _trailWait -= deltaTime;
                else
                {
                    _trailShare = Mathf.MoveTowards(_trailShare, _healthShare, TrailSpeed * deltaTime);
                    SetShare(_healthTrail, _trailShare);
                }
            }

            if (_low) PaintHealth(0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * LowPulseSpeed)));
        }

        private void PaintHealth(float pulse)
        {
            if (_style == null) return;
            var frame = _low ? _style.Danger : _style.Line;
            frame.a *= _low ? pulse : 1f;
            if (_healthFrame != null) _healthFrame.Stroke = frame;
            if (_heartRing != null) _heartRing.Stroke = _low ? frame : _style.Line;
            if (_healthText != null) _healthText.color = _low ? _style.Danger : _style.Text;
        }

        private static void SetShare(RectTransform bar, float share)
        {
            if (bar == null) return;
            var max = bar.anchorMax;
            if (Mathf.Approximately(max.x, share)) return;
            bar.anchorMax = new Vector2(share, max.y);
        }

        // ---- counters ----

        /// <summary>Map fragments; hidden when the level has none.</summary>
        public void SetFragments(int collected, int total)
        {
            if (collected == _fragmentsShown && total == _fragmentsTotal) return;
            _fragmentsShown = collected;
            _fragmentsTotal = total;
            if (_fragments != null) _fragments.SetActive(total > 0);
            if (_fragmentsText != null) _fragmentsText.text = collected + "/" + total;
        }

        /// <summary>Zombies killed; hidden when the level has none.</summary>
        public void SetKills(int killed, int total)
        {
            if (killed == _killsShown && total == _killsTotal) return;
            _killsShown = killed;
            _killsTotal = total;
            if (_kills != null) _kills.SetActive(total > 0);
            if (_killsText != null) _killsText.text = killed + "/" + total;
        }

        /// <summary>One key icon per carried key, in its door's colour.</summary>
        public void SetKeys(IReadOnlyList<Color> colors)
        {
            if (_keyTemplate == null) return;
            while (_keyImages.Count < colors.Count)
            {
                var image = Instantiate(_keyTemplate, _keys);
                image.gameObject.name = "Key" + _keyImages.Count;
                _keyImages.Add(image);
            }

            for (var i = 0; i < _keyImages.Count; i++)
            {
                var shown = i < colors.Count;
                _keyImages[i].gameObject.SetActive(shown);
                if (shown) _keyImages[i].color = colors[i];
            }
        }

        // ---- weapons, message ----

        /// <summary>Weapon slots shown where there are no on-screen controls.</summary>
        public void SetWeapons(in WeaponSlotState melee, in WeaponSlotState ranged)
        {
            if (_weaponPanel == null || !_weaponPanel.activeSelf) return;
            _meleeSlot.Set(melee);
            _rangedSlot.Set(ranged);
        }

        /// <summary>A short message (e.g. "Locked: needs the red key") that disappears by itself.</summary>
        public void ShowMessage(string message)
        {
            if (_message == null) return;
            _message.text = message;
            if (_messageRow != null) _messageRow.SetActive(!string.IsNullOrEmpty(message));
            _messageHideTime = Time.unscaledTime + MessageSeconds;
        }

        /// <summary>The level caption as shown (already in the language's letter case).</summary>
        public void SetLevelName(string levelName)
        {
            if (_levelName != null) _levelName.text = levelName ?? string.Empty;
        }

        public override void ApplyLanguage(LocalizationService texts)
        {
            var reload = texts.Get(TextKeys.HudReload);
            if (_meleeSlot != null) _meleeSlot.SetReloadText(reload);
            if (_rangedSlot != null) _rangedSlot.SetReloadText(reload);
        }

        /// <summary>Shows the play time; the text changes only when the whole seconds do (no allocations).</summary>
        public void SetTime(float seconds)
        {
            var whole = TimeFormat.WholeSeconds(seconds);
            if (whole == _timeShown || _timer == null) return;
            _timeShown = whole;
            _timer.SetCharArray(_timeChars, 0, TimeFormat.Write(whole, _timeChars));
        }

        /// <summary>Moves Pause and Map left of their places (a site's bar over the right edge; 0 — in place).</summary>
        public void ShiftTopRightButtons(float left)
        {
            _topRightShift ??= new HorizontalShift(_pauseButton, _mapButton);
            _topRightShift.Apply(left);
        }

        public void ClearLevelInfo()
        {
            _timeShown = -1;
            SetTime(0f);
            _health = _maxHealth = -1;
            _fragmentsShown = _fragmentsTotal = _killsShown = _killsTotal = -1;
            _low = false;
            SetKeys(Array.Empty<Color>());
            if (_messageRow != null) _messageRow.SetActive(false);
            _damageElapsed = float.MaxValue;
            if (_damageFlash != null) _damageFlash.enabled = false;
        }

        private void RefreshTouchControls()
        {
            var touch = isActiveAndEnabled && TouchShown;
            if (_touchControls != null) _touchControls.SetShown(touch);
            if (_weaponPanel != null) _weaponPanel.SetActive(!TouchShown);
        }

        // ---- damage pulse ----

        /// <summary>A red pulse at the screen edges (the player was hurt). Restarts if already shown.</summary>
        public void PulseDamage()
        {
            if (_damageFlash == null) return;
            _damageElapsed = 0f;
            _damageFlash.enabled = true;
            UpdateDamagePulse();
        }

        private void UpdateDamagePulse()
        {
            if (_damageFlash == null || !_damageFlash.enabled) return;

            if (_damageElapsed >= _damageTime)
            {
                // Off when not shown: a full-screen transparent image still costs fill rate on phones.
                _damageFlash.enabled = false;
                return;
            }

            var strength = _damageElapsed < DamageRise
                ? _damageElapsed / DamageRise
                : 1f - (_damageElapsed - DamageRise) / Mathf.Max(_damageTime - DamageRise, 0.01f);
            strength = Mathf.Clamp01(strength);
            var color = _damageColor;
            color.a *= strength * strength * (3f - 2f * strength);
            _damageFlash.color = color;
            _damageElapsed += Time.unscaledDeltaTime;
        }

        /// <summary>White, transparent in the middle, opaque towards the edges (a rounded-square falloff).</summary>
        private static Texture2D CreateVignette()
        {
            var texture = new Texture2D(VignetteSize, VignetteSize, TextureFormat.RGBA32, false)
            {
                name = "DamageVignette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[VignetteSize * VignetteSize];
            for (var y = 0; y < VignetteSize; y++)
            for (var x = 0; x < VignetteSize; x++)
            {
                var u = Mathf.Abs((x + 0.5f) / VignetteSize * 2f - 1f);
                var v = Mathf.Abs((y + 0.5f) / VignetteSize * 2f - 1f);
                var edge = Mathf.Pow(Mathf.Pow(u, 4f) + Mathf.Pow(v, 4f), 0.25f);
                var alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1.1f, edge));
                pixels[y * VignetteSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
