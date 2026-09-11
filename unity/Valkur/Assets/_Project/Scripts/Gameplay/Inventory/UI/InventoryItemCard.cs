using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// The card that opens beside a hovered slot: what the item is, how rare, what wearing it
    /// would change against what is worn now, what using it does, what it is worth, and which
    /// gesture does what to it.
    ///
    /// <para><b>The numbers come from the code that applies them.</b> A wearable item's lines are
    /// <see cref="EquipmentStatSource.AppendItem"/> — the exact modifiers equipping it adds — and
    /// each is phrased by <see cref="StatModifier.Describe"/>, the one formatter the skill tree
    /// and the character sheet already share. A card that computed its own numbers would be a
    /// second source of truth that goes stale the first time the mapping is retuned.</para>
    ///
    /// <para>Opaque (in linear space a 4 % gap lets ~18 % of what is behind through) and opened
    /// BESIDE the window, never over the slots: a card over the grid hides the neighbours the
    /// player is comparing against.</para>
    /// </summary>
    public sealed class InventoryItemCard
    {
        private const int Pad = 5;
        private const float TitleSize = 8f;
        private const float BodySize = 6.5f;

        public RectTransform Root { get; }
        public bool Visible => Root.gameObject.activeSelf;
        public string Title => _title.text;
        public string Body => _body.text;
        public ItemDefinition Item { get; private set; }

        private readonly int _width;
        private readonly Image _card;
        private readonly Image _rule;
        private readonly TextMeshProUGUI _title;
        private readonly TextMeshProUGUI _body;
        private readonly List<StatModifier> _mods = new List<StatModifier>();
        private readonly List<StatModifier> _worn = new List<StatModifier>();
        private readonly StringBuilder _sb = new StringBuilder(256);

        public InventoryItemCard(Transform parent, HudArt art, int width)
        {
            _width = width;
            Root = HudRect.Make("ItemCard", parent, 0, 0, width, 40);
            _card = HudRect.MakeImage("Card", Root, art.Slot, 0, 0, width, 40, Image.Type.Sliced);
            _rule = HudRect.MakeImage("Rule", Root, art.White, 3, 37, width - 6, 1);
            _title = MakeText("Title", Root, TitleSize, FontStyles.Bold);
            _body = MakeText("Body", Root, BodySize, FontStyles.Normal);
            _body.enableWordWrapping = true;
            _body.lineSpacing = -8f;
            Root.gameObject.SetActive(false);
        }

        private static TextMeshProUGUI MakeText(string name, Transform parent, float size, FontStyles style)
        {
            var rt = HudRect.Make(name, parent, Pad, 0, 10, 10);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            // A TMP label added at runtime picks its font up in Awake, which Unity never calls in
            // Edit Mode — and GetPreferredValues then throws from inside TMP. Assign it first.
            if (t.font == null) t.font = TMP_Settings.defaultFontAsset;
            t.fontSize = size;
            t.fontStyle = style;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>
        /// Fills the card for <paramref name="item"/> and sizes it to its text. Does not place it;
        /// the window does, because only the window knows where the screen edges are.
        /// </summary>
        public void Build(ItemDefinition item, int quantity, bool equipped, ItemDefinition worn,
                          int characterLevel, HudTheme theme)
        {
            Item = item;
            if (item == null) { Hide(); return; }

            var rarity = theme.RarityColour(item.rarity);
            string name = string.IsNullOrEmpty(item.displayName) ? item.itemId : item.displayName;
            _title.text = quantity > 1 ? name + "  <size=80%>×" + quantity + "</size>" : name;
            _title.color = rarity;
            _body.color = theme.text;
            _rule.color = rarity;

            _sb.Clear();
            string dim = Hex(theme.textDim);
            int home = EquipmentLayout.IndexFor(item);
            string kind = home >= 0
                ? EquipmentLayout.DisplayName((EquipmentSlotKind)home)
                : CategoryName(item.GetCategory());
            Line("<color=" + dim + ">" + kind + "  ·  <color=" + Hex(rarity) + ">" +
                 HudTheme.RarityName(item.rarity) + "</color></color>");

            // Wearing it.
            _mods.Clear();
            EquipmentStatSource.AppendItem(item, _mods);
            if (_mods.Count > 0)
            {
                Gap();
                for (int i = 0; i < _mods.Count; i++) Line(_mods[i].Describe());
            }

            // Against what is worn in that slot now.
            if (!equipped && home >= 0)
            {
                _worn.Clear();
                if (worn != null && worn != item) EquipmentStatSource.AppendItem(worn, _worn);
                if (worn != null && worn != item)
                {
                    Gap();
                    string wornName = string.IsNullOrEmpty(worn.displayName) ? worn.itemId : worn.displayName;
                    Line("<color=" + dim + ">Frente a " + wornName + ":</color>");
                    AppendDiff(theme);
                }
                else if (worn == null && _mods.Count > 0)
                {
                    Gap();
                    Line("<color=" + Hex(theme.success) + ">Hueco libre: todo suma</color>");
                }
            }

            // Using it.
            bool consumable = AppendConsume(item, theme);

            if (!string.IsNullOrEmpty(item.description))
            {
                Gap();
                Line("<i><color=" + dim + ">" + item.description.Trim() + "</color></i>");
            }

            // What it is worth and what it costs to carry.
            Gap();
            _sb.Append("<color=").Append(dim).Append(">");
            int value = item.sellPrice > 0 ? item.sellPrice : item.value;
            bool any = false;
            if (value > 0) { _sb.Append("Vale ").Append(value).Append(" oro"); any = true; }
            if (item.weight > 0f)
            {
                if (any) _sb.Append("  ·  ");
                _sb.Append("Peso ").Append(FormatWeight(item.weight * Mathf.Max(1, quantity)));
                any = true;
            }
            if (item.durability > 0)
            {
                if (any) _sb.Append("  ·  ");
                _sb.Append("Durabilidad ").Append(item.durability);
                any = true;
            }
            if (!any) _sb.Append("Sin valor de venta");
            _sb.Append("</color>\n");

            int need = EquipmentLayout.RequiredLevel(item);
            if (need > 0)
            {
                bool met = characterLevel < 0 || characterLevel >= need;
                Line("<color=" + Hex(met ? theme.textDim : theme.danger) + ">Requiere nivel " + need + "</color>");
            }

            // The gestures, so nothing has to be learned from a manual.
            Gap();
            _sb.Append("<size=88%><color=").Append(Hex(theme.textDisabled)).Append(">");
            if (equipped) _sb.Append("Clic derecho: quitar");
            else if (home >= 0) _sb.Append("Clic derecho: equipar");
            else if (consumable) _sb.Append("Clic derecho: usar");
            else _sb.Append("Arrastra para mover");
            if (!equipped && quantity > 1) _sb.Append("\nMayús + arrastrar: dividir");
            _sb.Append("\nArrastra fuera de la ventana: soltar");
            _sb.Append("</color></size>");

            _body.text = _sb.ToString();
            Layout();
        }

        /// <summary>A plain title-and-line card, for the window's own controls.</summary>
        public void BuildText(string title, string body, Color accent, HudTheme theme)
        {
            Item = null;
            _title.text = title;
            _title.color = accent;
            _rule.color = accent;
            _body.color = theme.textDim;
            _body.text = body ?? "";
            Layout();
        }

        private void AppendDiff(HudTheme theme)
        {
            // Sum each (stat, op) on both sides; a line per pair that differs.
            var keys = new List<(StatKind, StatOp)>();
            var delta = new Dictionary<(StatKind, StatOp), float>();
            for (int i = 0; i < _mods.Count; i++) Accumulate(keys, delta, _mods[i], 1f);
            for (int i = 0; i < _worn.Count; i++) Accumulate(keys, delta, _worn[i], -1f);
            bool anyLine = false;
            for (int i = 0; i < keys.Count; i++)
            {
                float d = delta[keys[i]];
                if (Mathf.Abs(d) < 0.0001f) continue;
                var m = new StatModifier(keys[i].Item1, keys[i].Item2, d);
                bool better = StatCatalog.LowerIsBetter(m.stat) ? d < 0f : d > 0f;
                Line("<color=" + Hex(better ? theme.success : theme.danger) + ">" + m.Describe() + "</color>");
                anyLine = true;
            }
            if (!anyLine) Line("<color=" + Hex(theme.textDim) + ">Sin cambios</color>");
        }

        private static void Accumulate(List<(StatKind, StatOp)> keys, Dictionary<(StatKind, StatOp), float> delta,
                                       StatModifier m, float sign)
        {
            var k = (m.stat, m.op);
            if (!delta.ContainsKey(k)) { delta[k] = 0f; keys.Add(k); }
            delta[k] += m.value * sign;
        }

        private bool AppendConsume(ItemDefinition item, HudTheme theme)
        {
            if (item.GetCategory() != ItemCategory.Consumable) return false;
            bool started = false;
            void Effect(string text)
            {
                if (!started) { Gap(); started = true; }
                Line("<color=" + Hex(theme.success) + ">" + text + "</color>");
            }
            if (item.healing > 0f) Effect("Cura " + Mathf.RoundToInt(item.healing) + " de vida");
            if (item.mana > 0f) Effect("Restaura " + Mathf.RoundToInt(item.mana) + " de maná");
            if (item.energy > 0f) Effect("+" + Mathf.RoundToInt(item.energy) + " de energía");
            if (item.hunger > 0f) Effect("Sacia " + Mathf.RoundToInt(item.hunger) + " de hambre");
            if (item.buffModifiers != null && item.buffModifiers.Length > 0)
            {
                string dur = item.duration > 0f ? " durante " + item.duration.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s" : "";
                for (int i = 0; i < item.buffModifiers.Length; i++) Effect(item.buffModifiers[i].Describe() + dur);
            }
            return true;
        }

        private void Layout()
        {
            int inner = _width - Pad * 2;
            var titleSize = _title.GetPreferredValues(_title.text, inner, 0f);
            _body.rectTransform.sizeDelta = new Vector2(inner, 10f);
            var bodySize = _body.GetPreferredValues(_body.text, inner, 0f);
            int titleH = Mathf.CeilToInt(titleSize.y);
            int bodyH = Mathf.CeilToInt(bodySize.y);
            int h = Pad + titleH + 3 + bodyH + Pad - 1;

            HudRect.Place(Root, (int)Root.anchoredPosition.x, (int)Root.anchoredPosition.y, _width, h);
            HudRect.Place(_card.rectTransform, 0, 0, _width, h);
            HudRect.Place(_rule.rectTransform, 3, h - Pad - titleH - 2, _width - 6, 1);
            HudRect.Place(_title.rectTransform, Pad, h - Pad - titleH + 1, inner, titleH);
            HudRect.Place(_body.rectTransform, Pad, Pad - 1, inner, bodyH);
        }

        /// <summary>Card height in texels, once built.</summary>
        public int Height => Mathf.RoundToInt(Root.sizeDelta.y);

        public int Width => _width;

        /// <summary>Places the card's bottom-left corner, in window texels.</summary>
        public void PlaceAt(int x, int y)
        {
            Root.anchoredPosition = new Vector2(x, y);
            Root.SetAsLastSibling();
            if (!Root.gameObject.activeSelf) Root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (Root.gameObject.activeSelf) Root.gameObject.SetActive(false);
        }

        private void Line(string s) => _sb.Append(s).Append('\n');

        private void Gap()
        {
            if (_sb.Length > 0) _sb.Append("<size=45%>\n</size>");
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        internal static string FormatWeight(float w)
        {
            float r = Mathf.Round(w * 10f) / 10f;
            return Mathf.Approximately(r, Mathf.Round(r)) ? Mathf.RoundToInt(r).ToString() : r.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static string CategoryName(ItemCategory c)
        {
            switch (c)
            {
                case ItemCategory.Equipment: return "Equipo";
                case ItemCategory.Consumable: return "Consumible";
                case ItemCategory.Material: return "Material";
                case ItemCategory.Quest: return "Objeto de misión";
                default: return "Objeto";
            }
        }
    }
}
