using System;
using System.Collections.Generic;
using System.Text;
using Purge.Core.Common;
using Purge.Core.Feel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Purge.Presentation.Stress
{
    /// <summary>
    /// Stand of the shooting feel (dev, sandbox only): a row per <see cref="FeelChannel"/> — a switch and a slider of its
    /// multiplier (0..<see cref="FeelChannels.MaxScale"/>, step 0.05) — plus all on / all off, reset and "log" (the
    /// values to the log, channel Gameplay, to be written into the weapons' Feel modules). Built in code on its own
    /// canvas, right side, collapsible. The values live for the session (<see cref="FeelChannels"/>).
    /// </summary>
    public sealed class FeelOverlay : IDisposable
    {
        private static readonly Color Panel = new Color(0.04f, 0.05f, 0.08f, 0.72f);
        private static readonly Color Idle = new Color(0.18f, 0.2f, 0.26f, 0.95f);
        private static readonly Color Active = new Color(0.95f, 0.62f, 0.15f, 1f);

        private readonly FeelChannels _channels;
        private readonly GameObject _root;
        private readonly GameObject _body;
        private readonly TMP_FontAsset _font;
        private readonly List<Row> _rows = new List<Row>();

        public FeelOverlay(FeelChannels channels)
        {
            _channels = channels;
            _font = TMP_Settings.defaultFontAsset;

            _root = new GameObject("Feel Overlay", typeof(RectTransform));
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 401;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            var panel = Rect("Panel", _root.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-24f, -170f);
            panel.sizeDelta = new Vector2(560f, 0f);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 10);
            layout.spacing = 4f;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.gameObject.AddComponent<Image>().color = Panel;

            var header = HorizontalRow(panel, 46f);
            Button(header, "FEEL  [-/+]", () => _body.SetActive(!_body.activeSelf), 200f);
            Button(header, "ON", () => SetAll(true), 80f);
            Button(header, "OFF", () => SetAll(false), 80f);
            Button(header, "RESET", () =>
            {
                _channels.Reset();
                Refresh();
            }, 100f);
            Button(header, "LOG", Log, 76f);

            _body = new GameObject("Body", typeof(RectTransform));
            _body.transform.SetParent(panel, false);
            var bodyLayout = _body.AddComponent<VerticalLayoutGroup>();
            bodyLayout.spacing = 3f;
            bodyLayout.childControlHeight = bodyLayout.childControlWidth = true;
            bodyLayout.childForceExpandHeight = false;
            var body = (RectTransform)_body.transform;

            foreach (FeelChannel channel in Enum.GetValues(typeof(FeelChannel)))
                _rows.Add(ChannelRow(body, channel));
            _body.SetActive(false);
            Refresh();
        }

        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }

        private void SetAll(bool on)
        {
            foreach (FeelChannel channel in Enum.GetValues(typeof(FeelChannel)))
                _channels.SetOn(channel, on);
            Refresh();
        }

        private void Refresh()
        {
            foreach (var row in _rows)
            {
                row.Switch.color = _channels.IsOn(row.Channel) ? Active : Idle;
                row.Slider.SetValueWithoutNotify(_channels.Scale(row.Channel));
                row.Value.SetText("x{0:0.00}", _channels.Scale(row.Channel));
            }
        }

        private void Log()
        {
            var text = new StringBuilder("Feel channels:");
            foreach (FeelChannel channel in Enum.GetValues(typeof(FeelChannel)))
                text.Append(' ').Append(channel).Append('=')
                    .Append(_channels.IsOn(channel) ? _channels.Scale(channel).ToString("0.00") : "off");
            GameLog.Info(LogChannel.Gameplay, text.ToString());
        }

        private Row ChannelRow(RectTransform parent, FeelChannel channel)
        {
            var rect = HorizontalRow(parent, 38f);
            var row = new Row { Channel = channel };
            row.Switch = Button(rect, channel.ToString(), () =>
            {
                _channels.SetOn(channel, !_channels.IsOn(channel));
                Refresh();
            }, 170f).transform.parent.GetComponent<Image>();
            row.Slider = Slider(rect, value =>
            {
                _channels.SetScale(channel, Mathf.Round(value * 20f) / 20f);
                row.Value.SetText("x{0:0.00}", _channels.Scale(channel));
            });
            row.Value = Text(rect, "", 20f);
            row.Value.gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;
            row.Value.alignment = TextAlignmentOptions.MidlineRight;
            return row;
        }

        private Slider Slider(RectTransform row, Action<float> changed)
        {
            var rect = Rect("Slider", row);
            rect.gameObject.AddComponent<LayoutElement>().preferredWidth = 270f;
            var background = Rect("Background", rect);
            Stretch(background, 14f);
            background.gameObject.AddComponent<Image>().color = Idle;

            var fillArea = Rect("Fill Area", rect);
            Stretch(fillArea, 14f);
            var fill = Rect("Fill", fillArea);
            fill.gameObject.AddComponent<Image>().color = new Color(Active.r, Active.g, Active.b, 0.55f);
            fill.sizeDelta = Vector2.zero;

            var handleArea = Rect("Handle Slide Area", rect);
            Stretch(handleArea, 0f);
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);
            var handle = Rect("Handle", handleArea);
            handle.sizeDelta = new Vector2(20f, 0f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Color.white;

            var slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.minValue = 0f;
            slider.maxValue = FeelChannels.MaxScale;
            slider.onValueChanged.AddListener(v => changed(v));
            return slider;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(0f, inset);
            rect.offsetMax = new Vector2(0f, -inset);
        }

        private RectTransform HorizontalRow(RectTransform parent, float height)
        {
            var row = Rect("Row", parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return row;
        }

        private TextMeshProUGUI Button(RectTransform row, string label, Action onClick, float width)
        {
            var rect = Rect("Button", row);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Idle;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            var text = Text(rect, label, 19f);
            text.alignment = TextAlignmentOptions.Center;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
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

        private sealed class Row
        {
            public FeelChannel Channel;
            public Image Switch;
            public Slider Slider;
            public TextMeshProUGUI Value;
        }
    }
}
