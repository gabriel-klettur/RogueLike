using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// The one question the window asks: "throw this on the ground?", and only for an item worth
    /// asking about (Epic and above). Everything cheaper drops at once — a window that confirms
    /// every potion is a window whose confirmation gets clicked through without being read, and
    /// then it protects nothing.
    ///
    /// <para>Enter confirms and Escape cancels, read by the window (the one Escape owner while it
    /// is up), so this card owns no input of its own.</para>
    /// </summary>
    public sealed class InventoryConfirm
    {
        private const int Width = 104;
        private const int Height = 38;
        private const int ButtonW = 40;
        private const int ButtonH = 11;

        public RectTransform Root { get; }
        public bool Visible => Root.gameObject.activeSelf;
        public string Question => _text.text;

        private readonly TextMeshProUGUI _text;
        private Action _onYes;

        public InventoryConfirm(Transform parent, HudArt art, HudTheme theme)
        {
            Root = HudRect.Make("Confirm", parent, 0, 0, Width, Height);
            // Opaque: a question drawn over the grid must not let the grid through.
            var card = HudRect.MakeImage("Card", Root, art.Slot, 0, 0, Width, Height, Image.Type.Sliced);
            card.raycastTarget = true;
            var rule = HudRect.MakeImage("Rule", Root, art.White, 3, Height - 4, Width - 6, 1);
            rule.color = theme.danger;

            var textRt = HudRect.Make("Text", Root, 5, 15, Width - 10, 18);
            _text = textRt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_text.font == null) _text.font = TMP_Settings.defaultFontAsset;
            _text.fontSize = 6.5f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.enableWordWrapping = true;
            _text.overflowMode = TextOverflowModes.Ellipsis;
            _text.raycastTarget = false;
            _text.color = theme.text;

            MakeButton(Root, art, 6, 4, "TIRAR", theme.danger, () => Resolve(true));
            MakeButton(Root, art, Width - 6 - ButtonW, 4, "NO", theme.textDim, () => Resolve(false));
            Root.gameObject.SetActive(false);
        }

        private static HudPixelText MakeButton(Transform parent, HudArt art, int x, int y, string label, Color tint,
                                               Action onClick)
        {
            var frame = HudRect.MakeImage(label, parent, art.Slot, x, y, ButtonW, ButtonH, Image.Type.Sliced);
            frame.raycastTarget = true;
            var text = HudPixelText.Create(frame.rectTransform, "Label", art, HudFontFace.Small, HudTextAlign.Centre,
                                           0, 0, ButtonW, ButtonH);
            text.SetText(label);
            text.SetColour(tint);
            var relay = frame.gameObject.AddComponent<InventoryPointerRelay>();
            relay.Click = ev => { if (ev.button == PointerEventData.InputButton.Left) onClick(); };
            relay.Enter = _ => text.SetColour(Color.Lerp(tint, Color.white, 0.4f));
            relay.Exit = _ => text.SetColour(tint);
            return text;
        }

        /// <summary>Asks about <paramref name="item"/> x <paramref name="quantity"/>; <paramref name="onYes"/> runs on confirm.</summary>
        public void Ask(ItemDefinition item, int quantity, HudTheme theme, int x, int y, Action onYes)
        {
            _onYes = onYes;
            string name = item != null ? (string.IsNullOrEmpty(item.displayName) ? item.itemId : item.displayName) : "";
            string hex = "#" + ColorUtility.ToHtmlStringRGB(item != null ? theme.RarityColour(item.rarity) : theme.text);
            _text.text = "¿Tirar <color=" + hex + ">" + name + "</color>" + (quantity > 1 ? " ×" + quantity : "") + " al suelo?";
            Root.anchoredPosition = new Vector2(x, y);
            Root.SetAsLastSibling();
            Root.gameObject.SetActive(true);
        }

        /// <summary>Answers the question: true drops, false keeps.</summary>
        public void Resolve(bool yes)
        {
            if (!Visible) return;
            var act = _onYes;
            _onYes = null;
            Root.gameObject.SetActive(false);
            if (yes) act?.Invoke();
        }
    }
}
