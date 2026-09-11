using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core.Input;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The two pieces of text the dial draws over itself: a tooltip naming the glyph under the
    /// pointer, and the distance beside each edge pin.
    ///
    /// <para><b>Why a tooltip.</b> Seven vendors share one gold, six trades share one frame of
    /// reference, and a skull does not say which boss. The icon answers "what kind"; the
    /// tooltip answers "who", on demand, without putting a caption on every glyph — which is
    /// what the old dial did with two-letter initials of NAMES ("SM", "VA") that meant nothing to
    /// a player who had not met the character.</para>
    ///
    /// <para><b>Why a distance.</b> A pin on the rim says which way; it does not say whether
    /// "that way" is a street or a province. One number turns the pin into a plan.</para>
    /// </summary>
    public sealed partial class MinimapHUD
    {
        private const int PinLabelCount = 4;

        private RectTransform _tooltip;
        private TextMeshProUGUI _tooltipText;
        private readonly List<TextMeshProUGUI> _pinLabels = new List<TextMeshProUGUI>(PinLabelCount);
        private string _tooltipShown;

        private void BuildOverlays()
        {
            for (int i = 0; i < PinLabelCount; i++)
            {
                var t = AddLabel(_discRt, "PinDistance" + i, 9.5f, FontStyles.Bold, Color.white);
                t.alignment = TextAlignmentOptions.Center;
                t.enableWordWrapping = false;
                t.outlineWidth = 0.28f;
                t.outlineColor = new Color32(0, 0, 0, 235);
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(44f, 12f);
                t.gameObject.SetActive(false);
                _pinLabels.Add(t);
            }

            var bg = AddImage(_root, "Tooltip", MinimapChromeSprites.Plate(_style), Color.white);
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 3f;
            bg.raycastTarget = false;
            _tooltip = bg.rectTransform;
            _tooltip.anchorMin = _tooltip.anchorMax = new Vector2(0.5f, 1f);
            _tooltip.pivot = new Vector2(0.5f, 0f);
            _tooltip.sizeDelta = new Vector2(80f, 18f);
            _tooltipText = AddLabel(_tooltip, "Text", 11f, FontStyles.Normal, Color.white);
            _tooltipText.alignment = TextAlignmentOptions.Center;
            _tooltipText.enableWordWrapping = false;
            var tr = _tooltipText.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            _tooltip.gameObject.SetActive(false);
        }

        private void UpdateOverlays()
        {
            UpdatePinLabels();
            UpdateTooltip();
        }

        private void UpdatePinLabels()
        {
            var pins = _view.Pins;
            int n = Mathf.Min(pins.Count, _pinLabels.Count);
            for (int i = 0; i < _pinLabels.Count; i++)
            {
                var label = _pinLabels[i];
                if (i >= n) { if (label.gameObject.activeSelf) label.gameObject.SetActive(false); continue; }
                var p = pins[i];
                if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
                label.rectTransform.anchoredPosition = p.Local + p.Inward * 20f;
                int metres = Mathf.RoundToInt(p.Distance);
                label.SetText(metres >= 1000 ? "{0:1}k" : "{0} m", metres >= 1000 ? metres / 1000f : metres);
                var c = Color.Lerp(p.Color, Color.white, 0.45f);
                label.color = c;
            }
        }

        private void UpdateTooltip()
        {
            if (_tooltip == null) return;
            string label = null;
            Vector2 at = default;
            if (_discInput != null && _discInput.Hovered)
            {
                var screen = MouseInputManager.GetScreenMousePosition();
                var rt = _mapImage.rectTransform;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out var local))
                    _view.TryPick(local - rt.rect.center, out label, out at);
            }

            if (string.IsNullOrEmpty(label))
            {
                if (_tooltip.gameObject.activeSelf) _tooltip.gameObject.SetActive(false);
                _tooltipShown = null;
                return;
            }

            if (!_tooltip.gameObject.activeSelf) _tooltip.gameObject.SetActive(true);
            if (label != _tooltipShown)
            {
                _tooltipShown = label;
                _tooltipText.text = label;
                float w = _tooltipText.GetPreferredValues(label).x + 14f;
                _tooltip.sizeDelta = new Vector2(Mathf.Max(40f, w), 18f);
            }
            // The dial sits against the right edge of the screen: grow the tooltip leftwards from
            // anything in the right half so it never runs off.
            _tooltip.pivot = new Vector2(at.x > 0f ? 1f : 0.5f, 0f);
            _tooltip.anchoredPosition = DiscCentre + at + new Vector2(at.x > 0f ? 8f : 0f, 9f);
            _tooltip.SetAsLastSibling();
        }
    }
}
