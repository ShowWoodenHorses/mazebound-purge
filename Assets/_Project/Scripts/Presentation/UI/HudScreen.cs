using System;
using System.Collections.Generic;
using Purge.Application.Services;
using Purge.Presentation.Localization;
using Purge.Presentation.UI.Shapes;
using Purge.Presentation.UI.Style;
using Purge.Presentation.UI.Touch;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Purge.Presentation.UI
{
    /// <summary>
    /// In-level HUD (design mock-up). Top left: a ring with a heart, the level name, HP as a number and a thin bar in an
    /// arrow-tipped frame — a light "trail" shows the lost part for a moment, low health turns the frame red and makes
    /// it pulse; under it zombies killed and the carried keys in their doors' colours. Top centre: a
    /// short message between diamonds. Top right: Map and Pause. Weapon slots: the on-screen cluster on touch devices,
    /// small slots at the bottom right otherwise. A red pulse at the screen edges when hurt. A level with waves (stage 6b):
    /// under the time the wave, enemies left and money of the run; in the pause between waves the countdown,
    /// «Пропустить +N» and the ammo reserve by type; a big caption when a wave starts or is cleared.
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

        [Tooltip("The throwable slot (key G): kind, count of max, accent while aiming.")]
        [SerializeField] private HudWeaponSlot _throwSlot;

        [Header("Waves (stage 6b)")]
        [Tooltip("Top centre under the time: the wave of how many, enemies left, money of the run.")]
        [SerializeField] private GameObject _waveRow;
        [SerializeField] private TMP_Text _waveText;
        [SerializeField] private TMP_Text _enemiesText;
        [SerializeField] private TMP_Text _moneyText;
        [Tooltip("The coin icon of the run's money: the kills' coins fly to it (stage 6e).")]
        [SerializeField] private RectTransform _moneyIcon;
        [Tooltip("Full-screen layer under the HUD's controls for the flying coins; its first child is the coin template.")]
        [SerializeField] private RectTransform _coinLayer;

        [Header("Experience (stage 6c)")]
        [Tooltip("Under the HP bar: the run's level and the way to the next one.")]
        [SerializeField] private GameObject _experience;
        [SerializeField] private TMP_Text _experienceLevel;
        [SerializeField] private RectTransform _experienceFill;

        [Tooltip("The pause between waves: the countdown, «Пропустить +N» and the ammo reserve by type.")]
        [SerializeField] private GameObject _countdown;
        [SerializeField] private TMP_Text _countdownText;
        [SerializeField] private Button _skipButton;
        [SerializeField] private TMP_Text _skipText;
        [Tooltip("The key hint on the skip button (hidden with the on-screen controls).")]
        [SerializeField] private GameObject _skipKey;
        [SerializeField] private RectTransform _ammoRow;
        [SerializeField] private TMP_Text _ammoTemplate;

        [Tooltip("A big caption in the middle that fades by itself: «Волна 3», «Волна пройдена».")]
        [SerializeField] private TMP_Text _banner;
        [SerializeField, Min(0.5f)] private float _bannerTime = 2.2f;

        [Header("Damage pulse")]
        [Tooltip("Full-screen image under the HUD: red at the screen edges when the player is hurt. Its texture is made on start.")]
        [SerializeField] private RawImage _damageFlash;

        [Tooltip("Arcs around the screen centre pointing where a blow came from (ТЗ: дуга у края экрана со стороны удара); textures made on start.")]
        [SerializeField] private RawImage[] _damageArcs = new RawImage[0];

        [Tooltip("The panel over a weapon on the ground (stage 6e2), hidden until the player stands at one.")]
        [SerializeField] private PickupComparePanel _comparePanel;

        public PickupComparePanel ComparePanel => _comparePanel;

        [Tooltip("The bar over a chest being opened (stage 7в).")]
        [SerializeField] private HudWorldBar _chestBar;

        [Tooltip("Under the time: the safe dome's seconds left in this wave, while the player is inside (stage 7в).")]
        [SerializeField] private TMP_Text _refugeText;

        public HudWorldBar ChestBar => _chestBar;

        /// <summary>The dome's line under the time; <paramref name="text"/> — only when it changed (null = keep).</summary>
        public void SetRefuge(bool shown, string text = null, bool warning = false)
        {
            if (_refugeText == null) return;
            if (_refugeText.gameObject.activeSelf != shown) _refugeText.gameObject.SetActive(shown);
            if (!shown) return;
            if (text != null) _refugeText.text = text;
            var color = warning && _style != null ? _style.Danger : _style != null ? _style.Accent : Color.white;
            if (_refugeText.color != color) _refugeText.color = color;
        }
        [SerializeField] private Color _damageArcColor = new Color(1f, 0.12f, 0.08f, 0.9f);
        [SerializeField, Min(0.1f)] private float _damageArcTime = 0.9f;

        [SerializeField] private Color _damageColor = new Color(0.85f, 0.04f, 0.04f, 0.5f);
        [SerializeField, Min(0.05f)] private float _damageTime = 0.6f;

        private const int VignetteSize = 64;
        private const float DamageRise = 0.06f;

        private readonly List<Image> _keyImages = new List<Image>();
        private float _messageHideTime;
        private float _damageElapsed = float.MaxValue;
        private Texture2D _vignette;
        private Texture2D _arcTexture;
        private float[] _arcElapsed = new float[0];
        private int _nextArc;
        private const int ArcTextureSize = 256;
        private int _health = -1;
        private int _maxHealth = -1;
        private float _healthShare = 1f;
        private float _trailShare = 1f;
        private float _trailWait;
        private bool _low;
        private int _killsShown = -1;
        private int _killsTotal = -1;
        private int _enemiesShown = -1;
        private int _moneyShown = -1;
        private float _bannerElapsed = float.MaxValue;
        private readonly List<TMP_Text> _ammoTexts = new List<TMP_Text>();

        /// <summary>A real touch was seen on a platform that is not mobile (e.g. a desktop browser on a touchscreen).</summary>
        private static bool _touchSeen;

        public event Action PauseClicked;
        public event Action MapClicked;

        /// <summary>«Пропустить +N» of the pause between waves.</summary>
        public event Action SkipClicked;

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
            Bind(_skipButton, () => SkipClicked?.Invoke());
            if (_keyTemplate != null) _keyTemplate.gameObject.SetActive(false);
            if (_ammoTemplate != null) _ammoTemplate.gameObject.SetActive(false);
            HideWaves();
            if (_messageRow != null) _messageRow.SetActive(false);

            if (_damageFlash != null)
            {
                _vignette = CreateVignette();
                _damageFlash.texture = _vignette;
                _damageFlash.raycastTarget = false;
                _damageFlash.enabled = false;
            }

            _arcElapsed = new float[_damageArcs.Length];
            if (_damageArcs.Length > 0) _arcTexture = CreateArc();
            for (var a = 0; a < _damageArcs.Length; a++)
            {
                _arcElapsed[a] = float.MaxValue;
                if (_damageArcs[a] == null) continue;
                _damageArcs[a].texture = _arcTexture;
                _damageArcs[a].raycastTarget = false;
                _damageArcs[a].enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_vignette != null) Destroy(_vignette);
            if (_arcTexture != null) Destroy(_arcTexture);
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
            UpdateDamageArcs();
            UpdateBanner();
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

        // ---- waves ----

        /// <summary>The wave row (a level with waves); hidden — the countdown and the banner go too.</summary>
        public void SetWaveShown(bool shown)
        {
            if (_waveRow != null && _waveRow.activeSelf != shown) _waveRow.SetActive(shown);
            if (!shown) HideWaves();
        }

        /// <summary>The caption of the wave ("ВОЛНА 2/5", already in the language).</summary>
        public void SetWave(string text)
        {
            if (_waveText != null) _waveText.text = text;
        }

        /// <summary>Enemies of the wave left (no allocations; the text changes only with the number).</summary>
        public void SetEnemies(int count)
        {
            if (count == _enemiesShown || _enemiesText == null) return;
            _enemiesShown = count;
            _enemiesText.SetText("{0}", count);
        }

        /// <summary>The experience bar: hidden without cards; <paramref name="level"/> — its caption ("УР. 3").</summary>
        public void SetExperience(bool shown, string level, float progress)
        {
            if (_experience == null) return;
            if (_experience.activeSelf != shown) _experience.SetActive(shown);
            if (!shown) return;
            if (_experienceLevel != null && _experienceLevel.text != level) _experienceLevel.text = level;
            SetShare(_experienceFill, Mathf.Clamp01(progress));
        }

        /// <summary>Layer of the flying coins (its first child is the template); null — no coins.</summary>
        public RectTransform CoinLayer => _coinLayer;

        /// <summary>Where the coins fly: the money's icon in <see cref="CoinLayer"/>; false while it is hidden.</summary>
        public bool TryGetCoinTarget(out Vector2 local)
        {
            local = default;
            if (_coinLayer == null || _moneyIcon == null || !_moneyIcon.gameObject.activeInHierarchy) return false;
            var screen = RectTransformUtility.WorldToScreenPoint(null, _moneyIcon.position);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_coinLayer, screen, null, out local);
        }

        /// <summary>A screen point in <see cref="CoinLayer"/>.</summary>
        public bool TryGetCoinPoint(Vector2 screen, out Vector2 local)
        {
            local = default;
            return _coinLayer != null &&
                   RectTransformUtility.ScreenPointToLocalPointInRectangle(_coinLayer, screen, null, out local);
        }

        /// <summary>The money icon's punch when a coin arrives: 1 = at rest.</summary>
        public void SetMoneyBump(float scale)
        {
            if (_moneyIcon != null) _moneyIcon.localScale = Vector3.one * scale;
        }

        public void SetMoney(int money)
        {
            if (money == _moneyShown || _moneyText == null) return;
            _moneyShown = money;
            _moneyText.SetText("{0}", money);
        }

        /// <summary>
        /// The countdown under the wave row: <paramref name="text"/> ("ВОЛНА 3 ЧЕРЕЗ 12"); <paramref name="skip"/> — the
        /// skip button's caption, null = no button (before the first wave).
        /// </summary>
        public void SetCountdown(bool shown, string text = null, string skip = null)
        {
            if (_countdown == null) return;
            if (_countdown.activeSelf != shown) _countdown.SetActive(shown);
            if (!shown) return;
            if (_countdownText != null && text != null) _countdownText.text = text;
            var skippable = skip != null;
            if (_skipButton != null && _skipButton.gameObject.activeSelf != skippable) _skipButton.gameObject.SetActive(skippable);
            if (_skipText != null && skippable) _skipText.text = skip;
            if (_skipKey != null) _skipKey.SetActive(!TouchShown);
        }

        /// <summary>The ammo reserve by type under the countdown: one "name count" per entry (empty — hidden).</summary>
        public void SetAmmo(IReadOnlyList<string> entries)
        {
            if (_ammoTemplate == null || _ammoRow == null) return;
            while (_ammoTexts.Count < entries.Count)
            {
                var text = Instantiate(_ammoTemplate, _ammoRow);
                text.gameObject.name = "Ammo" + _ammoTexts.Count;
                _ammoTexts.Add(text);
            }

            for (var i = 0; i < _ammoTexts.Count; i++)
            {
                var shown = i < entries.Count;
                _ammoTexts[i].gameObject.SetActive(shown);
                if (shown) _ammoTexts[i].text = entries[i];
            }

            _ammoRow.gameObject.SetActive(entries.Count > 0);
        }

        /// <summary>A big caption in the middle of the screen that fades by itself.</summary>
        public void ShowBanner(string text)
        {
            if (_banner == null) return;
            _banner.text = text;
            _bannerElapsed = 0f;
            _banner.gameObject.SetActive(true);
            UpdateBanner();
        }

        private void UpdateBanner()
        {
            if (_banner == null || !_banner.gameObject.activeSelf) return;
            if (_bannerElapsed >= _bannerTime)
            {
                _banner.gameObject.SetActive(false);
                return;
            }

            // Quick in, a hold, a slow fade; it grows a little.
            var t = _bannerElapsed / _bannerTime;
            var alpha = Mathf.Clamp01(t / 0.08f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, t)));
            var color = _banner.color;
            color.a = alpha;
            _banner.color = color;
            _banner.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.92f, 1.04f, Mathf.Sqrt(t));
            _bannerElapsed += Time.unscaledDeltaTime;
        }

        private void HideWaves()
        {
            _enemiesShown = _moneyShown = -1;
            if (_countdown != null) _countdown.SetActive(false);
            if (_ammoRow != null) _ammoRow.gameObject.SetActive(false);
            _bannerElapsed = float.MaxValue;
            if (_banner != null) _banner.gameObject.SetActive(false);
        }

        // ---- weapons, message ----

        /// <summary>Weapon slots shown where there are no on-screen controls.</summary>
        public void SetWeapons(in WeaponSlotState melee, in WeaponSlotState ranged)
        {
            if (_weaponPanel == null || !_weaponPanel.activeSelf) return;
            _meleeSlot.Set(melee);
            _rangedSlot.Set(ranged);
        }

        /// <summary>The throwable slot where there are no on-screen controls (Magazine = the stack's max, Ammo = count).</summary>
        public void SetThrowable(in WeaponSlotState state)
        {
            if (_weaponPanel == null || !_weaponPanel.activeSelf || _throwSlot == null) return;
            _throwSlot.Set(state);
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
            _killsShown = _killsTotal = -1;
            _low = false;
            SetKeys(Array.Empty<Color>());
            SetWaveShown(false);
            if (_messageRow != null) _messageRow.SetActive(false);
            _damageElapsed = float.MaxValue;
            if (_damageFlash != null) _damageFlash.enabled = false;
            foreach (var arc in _damageArcs)
                if (arc != null) arc.enabled = false;
        }

        private void RefreshTouchControls()
        {
            var touch = isActiveAndEnabled && TouchShown;
            if (_touchControls != null) _touchControls.SetShown(touch);
            if (_weaponPanel != null) _weaponPanel.SetActive(!TouchShown);
        }

        // ---- damage direction ----

        /// <summary>
        /// An arc near the screen edge on the side a blow came from: <paramref name="direction"/> on the ground (x = East,
        /// y = North = up on screen, the camera never turns). Several blows — several arcs (the oldest is reused).
        /// </summary>
        public void ShowDamageArc(Vector2 direction)
        {
            if (_damageArcs.Length == 0 || direction.sqrMagnitude < 1e-6f) return;
            var arc = _damageArcs[_nextArc];
            if (arc == null) return;
            _arcElapsed[_nextArc] = 0f;
            _nextArc = (_nextArc + 1) % _damageArcs.Length;
            var rect = arc.rectTransform;
            var parent = rect.parent as RectTransform;
            if (parent != null)
            {
                // A square as large as the shorter side of the screen: the arc is round on any aspect.
                var side = Mathf.Min(parent.rect.width, parent.rect.height) * 0.92f;
                rect.sizeDelta = new Vector2(side, side);
            }

            rect.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg);
            arc.enabled = true;
            UpdateDamageArcs();
        }

        private void UpdateDamageArcs()
        {
            for (var a = 0; a < _damageArcs.Length; a++)
            {
                var arc = _damageArcs[a];
                if (arc == null || !arc.enabled) continue;
                var t = _arcElapsed[a] / _damageArcTime;
                if (t >= 1f)
                {
                    arc.enabled = false;
                    continue;
                }

                var color = _damageArcColor;
                color.a *= 1f - t * t;
                arc.color = color;
                _arcElapsed[a] += Time.unscaledDeltaTime;
            }
        }

        /// <summary>White arc at the top of a square texture (±35° from up, near the edge), soft at its ends.</summary>
        private static Texture2D CreateArc()
        {
            var texture = new Texture2D(ArcTextureSize, ArcTextureSize, TextureFormat.RGBA32, false)
            {
                name = "DamageArc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[ArcTextureSize * ArcTextureSize];
            for (var y = 0; y < ArcTextureSize; y++)
            for (var x = 0; x < ArcTextureSize; x++)
            {
                var u = (x + 0.5f) / ArcTextureSize * 2f - 1f;
                var v = (y + 0.5f) / ArcTextureSize * 2f - 1f;
                var r = Mathf.Sqrt(u * u + v * v);
                var angle = Mathf.Abs(Mathf.Atan2(u, v)) * Mathf.Rad2Deg;
                var band = 1f - Mathf.Clamp01(Mathf.Abs(r - 0.93f) / 0.05f);
                var ends = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20f, 35f, angle));
                var alpha = Mathf.SmoothStep(0f, 1f, band) * ends;
                pixels[y * ArcTextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
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
