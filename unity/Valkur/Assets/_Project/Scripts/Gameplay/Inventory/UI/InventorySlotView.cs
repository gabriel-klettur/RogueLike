using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>What a slot is telling the player right now, beyond its contents.</summary>
    public enum InventorySlotMark
    {
        None = 0,
        Hover = 1,
        Selected = 2,
        /// <summary>A drag is in flight and the held item may go here.</summary>
        ValidTarget = 3,
        /// <summary>A drag is in flight, the pointer is here, and the held item may NOT go here.</summary>
        InvalidTarget = 4,
        /// <summary>A drag is in flight and the pointer is on a slot it may go in.</summary>
        HoverTarget = 5,
    }

    /// <summary>
    /// One slot of the inventory window, bag or equipment: a hollow in the stone, the item's
    /// icon minified to the slot's own pixel size, its rarity told by corner marks as well as
    /// colour, its quantity in the bitmap face, and — for an empty equipment slot — the
    /// silhouette of what goes there instead of a nine-pixel word.
    ///
    /// <para>A plain class driven by <c>InventoryUI</c>, so an EditMode test can build one and
    /// read back what it shows. It writes to its graphics only when something changed: a colour
    /// write dirties the canvas, and 33 slots each writing every frame would rebuild the
    /// window's batch sixty times a second for nothing.</para>
    /// </summary>
    public sealed class InventorySlotView
    {
        public RectTransform Root { get; }
        public int UnifiedIndex { get; }
        public bool IsEquipment { get; }

        /// <summary>The slot kind for an equipment slot; meaningless for a bag slot.</summary>
        public EquipmentSlotKind Kind { get; }

        public ItemDefinition Item { get; private set; }
        public int Quantity { get; private set; }
        public InventorySlotMark Mark { get; private set; }
        public bool IsNew { get; private set; }
        public bool Filtered { get; private set; }
        public bool DragSource { get; private set; }

        /// <summary>How many rarity corners are lit. For the tests.</summary>
        public int CornersShown
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _corners.Length; i++) if (_corners[i].enabled) n++;
                return n;
            }
        }

        public bool SilhouetteShown => _silhouette != null && _silhouette.enabled;
        public bool IconShown => _icon.enabled;
        public string QuantityLabel => _qty.Text;
        public Vector2 Centre => Root.anchoredPosition + Root.sizeDelta * 0.5f;
        public int Size => _size;

        private readonly int _size;
        private readonly int _inset;
        private readonly Image _frame;
        private readonly Image _silhouette;
        private readonly RawImage _icon;
        private readonly Image _fallbackIcon;
        private readonly Image _ring;
        private readonly Image[] _corners = new Image[4];
        private readonly HudPixelText _qty;
        private readonly Image _newDot;
        private readonly Image _mark;
        private readonly Image _flash;

        private Sprite _iconSource;
        private int _iconPx;
        private float _flashLeft, _flashTotal;
        private Color _flashColour;

        public InventorySlotView(Transform parent, HudArt art, InventoryArt inv, int unifiedIndex, bool isEquipment,
                                 EquipmentSlotKind kind, int x, int y, int size, int inset, Material additive)
        {
            UnifiedIndex = unifiedIndex;
            IsEquipment = isEquipment;
            Kind = kind;
            _size = size;
            _inset = inset;

            Root = HudRect.Make(isEquipment ? "Equip_" + kind : "Slot_" + unifiedIndex, parent, x, y, size, size);

            _frame = HudRect.MakeImage("Frame", Root, art.Slot, 0, 0, size, size, Image.Type.Sliced);
            _frame.raycastTarget = true;

            if (isEquipment)
            {
                int g = inv.SlotGlyph[(int)kind] != null ? (int)inv.SlotGlyph[(int)kind].rect.width : 11;
                int o = (size - g) / 2;
                _silhouette = HudRect.MakeImage("Silhouette", Root, inv.SlotGlyph[(int)kind], o, o, g, g);
            }

            int inner = size - inset * 2;
            var iconRt = HudRect.Make("Icon", Root, inset, inset, inner, inner);
            _icon = iconRt.gameObject.AddComponent<RawImage>();
            _icon.raycastTarget = false;
            _icon.enabled = false;
            // Without a GPU (batch mode) the bake refuses and the raw sprite is drawn instead.
            _fallbackIcon = HudRect.MakeImage("IconRaw", Root, null, inset, inset, inner, inner);
            _fallbackIcon.preserveAspect = true;
            _fallbackIcon.enabled = false;

            // The legendary filet: a one-texel line just inside the bevel, static — a state is read,
            // never animated. Deliberately NOT a glow: a glowing ring is what selection looks like,
            // and a legendary in the bag must not read as the selected slot.
            _ring = HudRect.MakeImage("RarityFilet", Root, inv.RarityFilet != null ? inv.RarityFilet : art.SlotGlow,
                                      0, 0, size, size, Image.Type.Sliced);
            _ring.enabled = false;

            int c = inv.Corner[0] != null ? (int)inv.Corner[0].rect.width : 3;
            _corners[0] = HudRect.MakeImage("CornerTL", Root, inv.Corner[0], 1, size - 1 - c, c, c);
            _corners[1] = HudRect.MakeImage("CornerTR", Root, inv.Corner[1], size - 1 - c, size - 1 - c, c, c);
            _corners[2] = HudRect.MakeImage("CornerBR", Root, inv.Corner[2], size - 1 - c, 1, c, c);
            _corners[3] = HudRect.MakeImage("CornerBL", Root, inv.Corner[3], 1, 1, c, c);
            for (int i = 0; i < 4; i++) _corners[i].enabled = false;

            _qty = HudPixelText.Create(Root, "Qty", art, HudFontFace.Small, HudTextAlign.Right, 1, 2, size - 3, 5);

            int d = inv.NewDot != null ? (int)inv.NewDot.rect.width : 5;
            // Top centre: the four corners belong to rarity, and a mark in one of them would read
            // as a corner of a tier the item does not have.
            _newDot = HudRect.MakeImage("New", Root, inv.NewDot, (size - d) / 2, size - d, d, d);
            _newDot.enabled = false;

            _mark = HudRect.MakeImage("Mark", Root, art.SlotGlow, 0, 0, size, size, Image.Type.Sliced);
            _mark.enabled = false;

            _flash = HudRect.MakeImage("Flash", Root, art.SlotGlow, -1, -1, size + 2, size + 2, Image.Type.Sliced);
            if (additive != null) _flash.material = additive;
            _flash.enabled = false;
        }

        // -- Content -----------------------------------------------------------------

        /// <summary>Shows <paramref name="item"/> x <paramref name="quantity"/> (null for empty).</summary>
        public void SetContent(ItemDefinition item, int quantity, HudTheme theme, int pixelScale)
        {
            if (item == null || quantity <= 0) { item = null; quantity = 0; }
            bool changed = item != Item || quantity != Quantity;
            Item = item;
            Quantity = quantity;

            _qty.SetText(item != null && quantity > 1 ? quantity.ToString() : "");
            _qty.SetColour(theme.text);
            if (_silhouette != null) _silhouette.enabled = item == null;

            int corners = item != null ? HudTheme.RarityCorners(item.rarity) : 0;
            var rarity = item != null ? theme.RarityColour(item.rarity) : Color.clear;
            // A lone Uncommon corner sits top-left; Rare adds the opposite corner so the pair reads
            // as a diagonal rather than as "two of four missing".
            bool[] lit = { corners >= 1, corners >= 4, corners >= 2, corners >= 4 };
            for (int i = 0; i < 4; i++)
            {
                if (_corners[i].enabled != lit[i]) _corners[i].enabled = lit[i];
                if (lit[i] && _corners[i].color != rarity) _corners[i].color = rarity;
            }
            bool ring = item != null && item.rarity == ItemRarity.Legendary;
            if (_ring.enabled != ring) _ring.enabled = ring;
            if (ring)
            {
                var rc = rarity;
                rc.a = 0.9f;
                if (_ring.color != rc) _ring.color = rc;
            }

            if (item == null) IsNew = false;
            UpdateIcon(pixelScale, changed);
            ApplyFilter();
            ApplyNewDot(theme);
        }

        /// <summary>Re-bakes the icon at the new pixel size when the screen scale changed.</summary>
        public void Rescale(int pixelScale) => UpdateIcon(pixelScale, force: false);

        private void UpdateIcon(int pixelScale, bool force)
        {
            var sprite = Item != null ? (Item.icon != null ? Item.icon : Item.iconSmall) : null;
            int px = (_size - _inset * 2) * Mathf.Max(1, pixelScale);
            if (!force && sprite == _iconSource && px == _iconPx) return;
            _iconSource = sprite;
            _iconPx = px;

            if (sprite == null)
            {
                _icon.enabled = false;
                _fallbackIcon.enabled = false;
                return;
            }
            var baked = HudTextureBaker.Icon(sprite, px);
            if (baked != null)
            {
                _icon.texture = baked;
                _icon.enabled = true;
                _fallbackIcon.enabled = false;
            }
            else
            {
                _icon.enabled = false;
                _fallbackIcon.sprite = sprite;
                _fallbackIcon.enabled = true;
            }
        }

        // -- State ---------------------------------------------------------------------

        public void SetMark(InventorySlotMark mark, HudTheme theme)
        {
            if (mark == Mark) return;
            Mark = mark;
            switch (mark)
            {
                case InventorySlotMark.Hover:
                    SetMarkColour(new Color(1f, 1f, 1f, 0.32f));
                    break;
                case InventorySlotMark.Selected:
                    SetMarkColour(theme.gold);
                    break;
                case InventorySlotMark.ValidTarget:
                    SetMarkColour(UITint(theme.gold, 0.45f));
                    break;
                case InventorySlotMark.HoverTarget:
                    SetMarkColour(Color.Lerp(theme.gold, Color.white, 0.4f));
                    break;
                case InventorySlotMark.InvalidTarget:
                    SetMarkColour(theme.danger);
                    break;
                default:
                    _mark.enabled = false;
                    break;
            }
        }

        private void SetMarkColour(Color c)
        {
            _mark.color = c;
            _mark.enabled = true;
        }

        private static Color UITint(Color c, float a) { c.a = a; return c; }

        /// <summary>Dims a slot whose item does not match the active tab.</summary>
        public void SetFiltered(bool filtered)
        {
            if (filtered == Filtered) return;
            Filtered = filtered;
            ApplyFilter();
        }

        /// <summary>Dims the slot a drag lifted its item out of.</summary>
        public void SetDragSource(bool source)
        {
            if (source == DragSource) return;
            DragSource = source;
            ApplyFilter();
        }

        private float _filteredAlpha = 0.22f, _dragAlpha = 0.35f;

        public void SetAlphas(float filtered, float drag)
        {
            _filteredAlpha = filtered;
            _dragAlpha = drag;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            float a = DragSource ? _dragAlpha : Filtered && Item != null ? _filteredAlpha : 1f;
            var c = new Color(1f, 1f, 1f, a);
            if (_icon.color != c) _icon.color = c;
            if (_fallbackIcon.color != c) _fallbackIcon.color = c;
        }

        public void SetSilhouetteColour(Color c)
        {
            if (_silhouette != null && _silhouette.color != c) _silhouette.color = c;
        }

        /// <summary>Marks the slot as holding something the player has not looked at yet.</summary>
        public void SetNew(bool isNew, HudTheme theme)
        {
            if (Item == null) isNew = false;
            if (isNew == IsNew) return;
            IsNew = isNew;
            ApplyNewDot(theme);
        }

        private void ApplyNewDot(HudTheme theme)
        {
            bool on = IsNew && Item != null;
            if (_newDot.enabled != on) _newDot.enabled = on;
            if (on)
            {
                var c = Color.Lerp(theme.RarityColour(Item.rarity), Color.white, 0.35f);
                if (_newDot.color != c) _newDot.color = c;
            }
        }

        // -- Events --------------------------------------------------------------------

        /// <summary>A bright ring that flares and dies — the slot acknowledging an event.</summary>
        public void Flash(Color colour, float seconds)
        {
            if (seconds <= 0f) return;
            _flashColour = colour;
            _flashTotal = seconds;
            _flashLeft = seconds;
            _flash.enabled = true;
            _flash.color = colour;
        }

        public bool Flashing => _flashLeft > 0f;

        public void Tick(float dt)
        {
            if (_flashLeft <= 0f) return;
            _flashLeft -= dt;
            if (_flashLeft <= 0f)
            {
                _flash.enabled = false;
                return;
            }
            float t = _flashLeft / Mathf.Max(0.001f, _flashTotal);
            var c = _flashColour;
            c.a *= t * t;
            _flash.color = c;
        }
    }
}
