using System;
using System.Collections.Generic;
using System.Text;
using Purge.Gameplay.Horde;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.Stress
{
    /// <summary>
    /// Measuring overlay of the stress test: live numbers and buttons that change the load (monster count, camera,
    /// weapon, rendering options, resolution) and start the benchmark. Built in code (dev levels only, not part of
    /// the game's UI), left side under the HUD, collapsible. Text is refreshed twice a second without allocations.
    /// </summary>
    public sealed class StressOverlay : IDisposable
    {
        private static readonly Color Panel = new Color(0.04f, 0.05f, 0.08f, 0.72f);
        private static readonly Color Idle = new Color(0.18f, 0.2f, 0.26f, 0.95f);
        private static readonly Color Active = new Color(0.95f, 0.62f, 0.15f, 1f);

        private readonly StressController _controller;
        private readonly GameObject _root;
        private readonly GameObject _body;
        private readonly TextMeshProUGUI _stats;
        private readonly TextMeshProUGUI _benchLabel;
        private readonly List<(Image Image, Func<bool> IsActive)> _states = new List<(Image, Func<bool>)>();
        private readonly TMP_FontAsset _font;

        public StressOverlay(StressController controller)
        {
            _controller = controller;
            _font = TMP_Settings.defaultFontAsset;

            _root = new GameObject("Stress Overlay", typeof(RectTransform));
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            var panel = Rect("Panel", _root.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(24f, -170f);
            panel.sizeDelta = new Vector2(640f, 0f);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 10);
            layout.spacing = 6f;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.gameObject.AddComponent<Image>().color = Panel;

            var sandbox = controller.Settings.IsSandbox;
            var header = Row(panel);
            Button(header, sandbox ? "SANDBOX  [-/+]" : "STRESS  [-/+]", () => _body.SetActive(!_body.activeSelf), null, 260f);
            if (sandbox)
                Button(header, "AMMO", controller.RefillAmmo, null, 220f);
            else
                _benchLabel = Button(header, "RUN BENCH", ToggleBenchmark, () => _controller.BenchmarkRunning, 220f);

            _body = new GameObject("Body", typeof(RectTransform));
            _body.transform.SetParent(panel, false);
            var bodyLayout = _body.AddComponent<VerticalLayoutGroup>();
            bodyLayout.spacing = 6f;
            bodyLayout.childControlHeight = bodyLayout.childControlWidth = true;
            bodyLayout.childForceExpandHeight = false;
            var bodyRect = (RectTransform)_body.transform;

            _stats = Text(bodyRect, "", 22f);
            _stats.alignment = TextAlignmentOptions.TopLeft;

            var settings = controller.Settings;
            var counts = Row(bodyRect, "Count");
            foreach (var count in settings.MonsterCounts)
            {
                var value = count;
                Button(counts, value.ToString(), () => controller.SetCount(value), () => controller.Horde.TargetCount == value);
            }

            var cameras = Row(bodyRect, "Camera");
            foreach (var extent in settings.CameraHalfExtents)
            {
                var value = extent;
                Button(cameras, "+-" + value.ToString("0"), () => controller.SetCamera(value),
                    () => Mathf.Approximately(controller.CameraHalfExtent, value));
            }

            if (!sandbox)
                StressWeaponRows(bodyRect, settings);

            var render = Row(bodyRect, "Render");
            Toggle(render, "Draw", () => controller.Renderer.Enabled, v => controller.Renderer.Enabled = v);
            Toggle(render, "Shadow", () => controller.Renderer.Shadows, v => controller.Renderer.Shadows = v);
            Toggle(render, "VAT", () => controller.Renderer.UseVat, v => controller.Renderer.UseVat = v);
            Toggle(render, "Lerp", () => controller.Renderer.Interpolate, v => controller.Renderer.Interpolate = v);
            Toggle(render, "Cull", () => controller.Renderer.CullByVisibility, v => controller.Renderer.CullByVisibility = v);

            var effects = Row(bodyRect, "Effects");
            Toggle(effects, "Tracer", () => controller.Effects.Tracers, v => controller.Effects.Tracers = v);
            Toggle(effects, "Partic", () => controller.Effects.Particles, v => controller.Effects.Particles = v);
            Toggle(effects, "FireFx", () => controller.Effects.FireCells, v => controller.Effects.FireCells = v);

            var sim = Row(bodyRect, "Sim");
            Toggle(sim, "Freeze", () => controller.Horde.Frozen, v => controller.Horde.Frozen = v);
            Toggle(sim, "God", () => controller.Horde.GodMode, v => controller.Horde.GodMode = v);
            if (!sandbox)
            {
                Toggle(sim, "Auto", () => controller.Autopilot, v => controller.Autopilot = v);
                Toggle(sim, "AutoFire", () => controller.Weapons.AutoFire, v => controller.Weapons.AutoFire = v);
            }

            var resolution = Row(bodyRect, "Res");
            foreach (var scale in new[] { 1f, 0.75f, 0.5f })
            {
                var value = scale;
                Button(resolution, value.ToString("0.##"), () => controller.SetResolutionScale(value),
                    () => Mathf.Approximately(controller.ResolutionScale, value));
            }

            var cap = Row(bodyRect, "FPS cap");
            foreach (var fps in new[] { 30, 60, StressController.MaxFrameRate })
            {
                var value = fps;
                Button(cap, value.ToString(), () => controller.SetFrameRateCap(value), () => controller.FrameRateCap == value);
            }

            Refresh();
        }

        private void StressWeaponRows(RectTransform bodyRect, Core.Horde.StressTestDefinition settings)
        {
            var weapons = Row(bodyRect, "Weapon");
            WeaponButton(weapons, "Gun", StressWeapon.MachineGun);
            WeaponButton(weapons, "Shotgun", StressWeapon.Shotgun);
            WeaponButton(weapons, "Flame", StressWeapon.Flamethrower);
            WeaponButton(weapons, "Off", StressWeapon.None);

            var storm = Row(bodyRect, "Storm");
            foreach (var multiplier in new[] { 1f, 3f, 5f })
            {
                var value = multiplier;
                Button(storm, "x" + value.ToString("0"), () => _controller.Weapons.RateMultiplier = value,
                    () => Mathf.Approximately(_controller.Weapons.RateMultiplier, value));
            }

            var fireTarget = settings.Weapons.FireCellsTarget;
            Button(storm, "Fire" + fireTarget,
                () => _controller.Weapons.KeepFireCells = _controller.Weapons.KeepFireCells > 0 ? 0 : fireTarget,
                () => _controller.Weapons.KeepFireCells > 0);

        }

        public void SetStats(StringBuilder text) => _stats.SetText(text);

        public void Refresh()
        {
            foreach (var (image, isActive) in _states)
                image.color = isActive() ? Active : Idle;
            if (_benchLabel != null) _benchLabel.text = _controller.BenchmarkRunning ? "STOP BENCH" : "RUN BENCH";
        }

        public void Dispose()
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
        }

        private void ToggleBenchmark()
        {
            if (_controller.BenchmarkRunning) _controller.StopBenchmark();
            else _controller.StartBenchmark();
        }

        private void WeaponButton(RectTransform row, string label, StressWeapon weapon) =>
            Button(row, label, () => _controller.Weapons.Weapon = weapon, () => _controller.Weapons.Weapon == weapon);

        private void Toggle(RectTransform row, string label, Func<bool> get, Action<bool> set) =>
            Button(row, label, () => set(!get()), get);

        private RectTransform Row(RectTransform parent, string label = null)
        {
            var row = Rect("Row", parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;
            if (label != null)
            {
                var text = Text(row, label, 20f);
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;
            }

            return row;
        }

        private TextMeshProUGUI Button(RectTransform row, string label, Action onClick, Func<bool> isActive, float width = 0f)
        {
            var rect = Rect("Button", row);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Idle;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                onClick();
                Refresh();
            });
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width > 0f ? width : 84f;
            element.preferredHeight = 44f;
            var text = Text(rect, label, 20f);
            text.alignment = TextAlignmentOptions.Center;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            if (isActive != null)
                _states.Add((image, isActive));
            return text;
        }

        private TextMeshProUGUI Text(RectTransform parent, string value, float size)
        {
            var rect = Rect("Text", parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) text.font = _font;
            text.fontSize = size;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
    }
}
