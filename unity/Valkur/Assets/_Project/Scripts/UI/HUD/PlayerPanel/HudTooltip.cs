using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// A small card above a hovered slot: the spell's name, its cost and cooldown, the button that
    /// casts it, and — for a spell the character has not learned — that it is locked.
    ///
    /// <para>The ONLY text on the panel set in TMP, because spell names are Spanish with accents
    /// and the bitmap face spells digits and capitals only. It shows on hover and never on its
    /// own: the panel must not grow text at rest.</para>
    /// </summary>
    public sealed class HudTooltip
    {
        private const int Width = 104;
        private const int Height = 34;

        public RectTransform Root { get; }

        private readonly TextMeshProUGUI _title;
        private readonly TextMeshProUGUI _body;
        private readonly int _anchorY;

        /// <summary>True while the card is showing.</summary>
        public bool Visible => Root.gameObject.activeSelf;

        /// <summary>The card's title, for the tests.</summary>
        public string Title => _title.text;

        /// <param name="anchorY">The panel's top edge, in the parent's texels. The card opens
        /// ABOVE the panel, never over it: a card over the bars hides the numbers the player was
        /// reading when they reached for the slot.</param>
        public HudTooltip(Transform parent, HudArt art, int anchorY)
        {
            _anchorY = anchorY;
            Root = HudRect.Make("Tooltip", parent, 0, 0, Width, Height);
            HudRect.MakeImage("Card", Root, art.Slot, 0, 0, Width, Height, Image.Type.Sliced);
            _title = MakeText("Title", Root, 5, Height - 14, Width - 10, 10, 8f, FontStyles.Bold);
            _body = MakeText("Body", Root, 5, 3, Width - 10, 18, 6.5f, FontStyles.Normal);
            Root.gameObject.SetActive(false);
        }

        private static TextMeshProUGUI MakeText(string name, Transform parent, int x, int y, int w, int h,
                                                float size, FontStyles style)
        {
            var rt = HudRect.Make(name, parent, x, y, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            // A TMP label added at runtime picks its font up in Awake, which Unity never calls in
            // Edit Mode — and GetPreferredValues then throws from inside TMP. Assign it first.
            if (t.font == null) t.font = TMP_Settings.defaultFontAsset;
            t.fontSize = size;
            t.fontStyle = style;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.raycastTarget = false;
            t.color = PlayerHudStyle.Active.text;
            return t;
        }

        /// <summary>Shows the card for a slot, above it, clamped inside the panel's width.</summary>
        public void Show(HudAbilitySlot slot, int panelWidth, Color accent)
        {
            if (slot == null || slot.Spell == null) { Hide(); return; }
            var spell = slot.Spell;
            _title.text = string.IsNullOrEmpty(spell.displayName) ? spell.spellKey : spell.displayName;
            _title.color = accent;

            string cost = spell.manaCost > 0f ? Mathf.RoundToInt(spell.manaCost) + " maná" : "sin coste";
            string cd = spell.cooldownDuration > 0.05f ? spell.cooldownDuration.ToString("0.#") + " s" : "sin recarga";
            string button = slot.BindingLabel;
            string line = cost + "  ·  " + cd;
            if (slot.State == HudSlotState.Locked) line = "Sin aprender  ·  " + line;
            if (!string.IsNullOrEmpty(button)) line += "\n" + button;

            ShowText(_title.text, line, accent, slot.Centre.x, _anchorY, panelWidth);
        }

        /// <summary>Shows arbitrary text above a point, clamped inside the panel's width.</summary>
        public void ShowText(string title, string body, Color accent, float centreX, float aboveY, int panelWidth)
        {
            _title.text = title;
            _title.color = accent;
            _body.text = body;
            int x = Mathf.Clamp(Mathf.RoundToInt(centreX - Width * 0.5f), 2, Mathf.Max(2, panelWidth - Width - 2));
            int y = Mathf.RoundToInt(Mathf.Max(aboveY, _anchorY) + 2f);
            Root.anchoredPosition = new Vector2(x, y);
            Root.SetAsLastSibling();
            Root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (Root.gameObject.activeSelf) Root.gameObject.SetActive(false);
        }
    }
}
