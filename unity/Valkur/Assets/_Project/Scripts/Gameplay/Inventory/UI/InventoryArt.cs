using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// The inventory window's own pixel pieces, generated in code into one small point-filtered
    /// atlas: the eight equipment-slot silhouettes, the tab glyphs, the coin, the rarity corner
    /// marks and the window buttons. The shared pieces — slot frame, glows, panel stone, the
    /// bitmap font, motes — come from <see cref="HudArt"/>; this atlas holds only what no other
    /// surface draws, so the shared one does not grow with every window (the split
    /// <c>HUD_VISUAL_LANGUAGE.md</c> 5.2 asks for).
    ///
    /// <para>Every glyph is authored WHITE (three greys: <c>#</c> body, <c>h</c> light, <c>s</c>
    /// shade) with a baked one-texel dark outline, and tinted by its <c>Image.color</c>. The coin is
    /// the one piece authored in colour, because gold is what a coin is.</para>
    ///
    /// <para>Sprites are made at <see cref="HudArt.SpritePixelsPerUnit"/>, which with the window
    /// canvas's reference of 100 makes one atlas pixel exactly one texel.</para>
    /// </summary>
    public sealed class InventoryArt
    {
        private const int AtlasSize = 128;

        public Texture2D Atlas { get; private set; }

        /// <summary>One silhouette per <see cref="EquipmentSlotKind"/>, in enum order.</summary>
        public Sprite[] SlotGlyph { get; private set; }

        public Sprite TabAll { get; private set; }
        public Sprite TabEquipment { get; private set; }
        public Sprite TabConsumable { get; private set; }
        public Sprite TabMaterial { get; private set; }
        public Sprite TabQuest { get; private set; }
        public Sprite TabOther { get; private set; }
        public Sprite Sort { get; private set; }
        public Sprite Close { get; private set; }
        public Sprite Collapse { get; private set; }
        public Sprite Expand { get; private set; }
        public Sprite Coin { get; private set; }
        public Sprite Bag { get; private set; }
        public Sprite Weight { get; private set; }
        public Sprite Sword { get; private set; }
        public Sprite Shield { get; private set; }
        public Sprite NewDot { get; private set; }
        public Sprite[] Corner { get; private set; }   // TL, TR, BR, BL
        public Sprite Divider { get; private set; }
        public Sprite DividerGem { get; private set; }

        /// <summary>A one-texel line just inside a slot's bevel, 9-sliced: the legendary filet.</summary>
        public Sprite RarityFilet { get; private set; }

        private readonly Dictionary<string, RectInt> _pieces = new Dictionary<string, RectInt>();
        private Color32[] _pixels;
        private int _cursorX = 1, _cursorY = 1, _rowHeight;

        // -- Cache -------------------------------------------------------------

        private static InventoryArt s_instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInventoryArtStatics() => s_instance = null;

        /// <summary>The atlas, built on first use.</summary>
        public static InventoryArt Get()
        {
            if (s_instance != null && s_instance.Atlas != null) return s_instance;
            s_instance = new InventoryArt();
            return s_instance;
        }

        /// <summary>The pixel rect a named piece occupies. For the tests.</summary>
        public bool TryGetPieceRect(string name, out RectInt rect) => _pieces.TryGetValue(name, out rect);

        private InventoryArt()
        {
            _pixels = new Color32[AtlasSize * AtlasSize];

            SlotGlyph = new Sprite[EquipmentLayout.Count];
            var glyphNames = new string[EquipmentLayout.Count];
            for (int i = 0; i < EquipmentLayout.Count; i++)
            {
                glyphNames[i] = "slot_" + (EquipmentSlotKind)i;
                AddPattern(glyphNames[i], SlotPattern((EquipmentSlotKind)i), Shade, true);
            }

            AddPattern("tab_all", new[]
            {
                "### ###",
                "#h# #h#",
                "### ###",
                "       ",
                "### ###",
                "#h# #h#",
                "### ###",
            }, Shade, true);
            AddPattern("tab_equipment", new[]
            {
                "     ##",
                "    ###",
                "   ### ",
                "#  ##  ",
                "###    ",
                " #     ",
                "# #    ",
            }, Shade, true);
            AddPattern("tab_consumable", new[]
            {
                "  ###  ",
                "   #   ",
                "  # #  ",
                " #h### ",
                "#h#####",
                "######s",
                " ####s ",
            }, Shade, true);
            AddPattern("tab_material", new[]
            {
                "   ##  ",
                "  #h## ",
                " #h####",
                "#h#####",
                "#####s#",
                " ###s# ",
                "  ###  ",
            }, Shade, true);
            AddPattern("tab_quest", new[]
            {
                " ##### ",
                "#h###s#",
                " #   # ",
                " # # # ",
                " #   # ",
                "#h###s#",
                " ##### ",
            }, Shade, true);
            AddPattern("tab_other", new[]
            {
                "       ",
                "       ",
                "       ",
                "## ## ##",
                "## ## ##",
                "       ",
                "       ",
            }, Shade, true);
            AddPattern("sort", new[]
            {
                "  #    ",
                " ###   ",
                "# # #  ",
                "  #  # ",
                "  # # #",
                "   ### ",
                "    #  ",
            }, Shade, true);
            AddPattern("close", new[]
            {
                "#   #",
                " # # ",
                "  #  ",
                " # # ",
                "#   #",
            }, Shade, true);
            AddPattern("collapse", new[]
            {
                "     ",
                "     ",
                "#####",
                "     ",
                "     ",
            }, Shade, true);
            AddPattern("expand", new[]
            {
                "  #  ",
                "  #  ",
                "#####",
                "  #  ",
                "  #  ",
            }, Shade, true);
            AddPattern("coin", new[]
            {
                "  ooo  ",
                " ohhgo ",
                "ohggggo",
                "ohgsggo",
                "oggggso",
                " ogsso ",
                "  ooo  ",
            }, CoinPixel, false);
            AddPattern("bag", new[]
            {
                "  ###  ",
                " #   # ",
                "  ###  ",
                " #h### ",
                "#h#####",
                "######s",
                " ####s ",
            }, Shade, true);
            AddPattern("weight", new[]
            {
                "  ###  ",
                " #   # ",
                " #   # ",
                "#h#####",
                "#######",
                "######s",
                " ####s ",
            }, Shade, true);
            AddPattern("sword", new[]
            {
                "     ##",
                "    #h#",
                "   #h# ",
                "# #h#  ",
                " ##s   ",
                " ###   ",
                "#  #   ",
            }, Shade, true);
            AddPattern("shield", new[]
            {
                "#######",
                "#h#####",
                "#h#####",
                "######s",
                " ####s ",
                "  ##s  ",
                "   #   ",
            }, Shade, true);
            AddPattern("new_dot", new[]
            {
                " # ",
                "###",
                " # ",
            }, c => c == '#' ? (Color32?)new Color32(255, 255, 255, 255) : null, true);

            var cornerTL = new[] { "###", "#  ", "#  " };
            AddPattern("corner_tl", cornerTL, Solid, false);
            AddPattern("corner_tr", new[] { "###", "  #", "  #" }, Solid, false);
            AddPattern("corner_br", new[] { "  #", "  #", "###" }, Solid, false);
            AddPattern("corner_bl", new[] { "#  ", "#  ", "###" }, Solid, false);

            // A 9-slice rule for section breaks: a dark line with a lit line under it, and a small
            // gold diamond in the middle that the slice keeps at its size.
            AddDivider("divider");
            AddFilet("rarity_filet");

            Atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false, false)
            {
                name = "InventoryArt_Atlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            Atlas.SetPixels32(_pixels);
            Atlas.Apply(false, false);
            _pixels = null;

            for (int i = 0; i < EquipmentLayout.Count; i++) SlotGlyph[i] = SpriteOf(glyphNames[i], Vector4.zero);
            TabAll = SpriteOf("tab_all", Vector4.zero);
            TabEquipment = SpriteOf("tab_equipment", Vector4.zero);
            TabConsumable = SpriteOf("tab_consumable", Vector4.zero);
            TabMaterial = SpriteOf("tab_material", Vector4.zero);
            TabQuest = SpriteOf("tab_quest", Vector4.zero);
            TabOther = SpriteOf("tab_other", Vector4.zero);
            Sort = SpriteOf("sort", Vector4.zero);
            Close = SpriteOf("close", Vector4.zero);
            Collapse = SpriteOf("collapse", Vector4.zero);
            Expand = SpriteOf("expand", Vector4.zero);
            Coin = SpriteOf("coin", Vector4.zero);
            Bag = SpriteOf("bag", Vector4.zero);
            Weight = SpriteOf("weight", Vector4.zero);
            Sword = SpriteOf("sword", Vector4.zero);
            Shield = SpriteOf("shield", Vector4.zero);
            NewDot = SpriteOf("new_dot", Vector4.zero);
            Corner = new[]
            {
                SpriteOf("corner_tl", Vector4.zero), SpriteOf("corner_tr", Vector4.zero),
                SpriteOf("corner_br", Vector4.zero), SpriteOf("corner_bl", Vector4.zero),
            };
            Divider = SpriteOf("divider", Vector4.zero);
            DividerGem = SpriteOf("divider_gem", Vector4.zero);
            RarityFilet = SpriteOf("rarity_filet", new Vector4(3, 3, 3, 3));
        }

        /// <summary>The silhouette pattern of an empty equipment slot, 9x9.</summary>
        internal static string[] SlotPattern(EquipmentSlotKind kind)
        {
            switch (kind)
            {
                case EquipmentSlotKind.Head:
                    return new[]
                    {
                        "  #####  ",
                        " #h##### ",
                        "#h#######",
                        "#h#######",
                        "##  #  ##",
                        "##  #  ##",
                        "###   ###",
                        " ##   ## ",
                        "         ",
                    };
                case EquipmentSlotKind.Amulet:
                    return new[]
                    {
                        "#       #",
                        " #     # ",
                        "  #   #  ",
                        "   # #   ",
                        "   ###   ",
                        "  #h###  ",
                        "  #h###  ",
                        "  ####s  ",
                        "   ##s   ",
                    };
                case EquipmentSlotKind.Weapon:
                    return new[]
                    {
                        "       ##",
                        "      #h#",
                        "     #h# ",
                        "    #h#  ",
                        "#  #h#   ",
                        "####s    ",
                        " ##s     ",
                        "####     ",
                        "#  #     ",
                    };
                case EquipmentSlotKind.Offhand:
                    return new[]
                    {
                        "#########",
                        "#h#######",
                        "#h#######",
                        "#h#######",
                        "########s",
                        " ######s ",
                        " #####s# ",
                        "  ###s#  ",
                        "   ###   ",
                    };
                case EquipmentSlotKind.Chest:
                    return new[]
                    {
                        "##     ##",
                        "###   ###",
                        "#########",
                        " #h##### ",
                        " #h##### ",
                        " ####### ",
                        " ######s ",
                        " #####s# ",
                        "  #####  ",
                    };
                case EquipmentSlotKind.Ring:
                    return new[]
                    {
                        "   #h#   ",
                        "   ###   ",
                        "  #   #  ",
                        " #     # ",
                        " #     # ",
                        " #     # ",
                        "  #   #  ",
                        "   ###   ",
                        "         ",
                    };
                case EquipmentSlotKind.Boots:
                    return new[]
                    {
                        "  ###    ",
                        "  #h#    ",
                        "  #h#    ",
                        "  ###    ",
                        "  ###    ",
                        "  #####  ",
                        "  #h#### ",
                        "  #######",
                        "  #####ss",
                    };
                default: // Trinket
                    return new[]
                    {
                        "    #    ",
                        "    #    ",
                        "   #h#   ",
                        "####h####",
                        " ####### ",
                        "  #####  ",
                        "  ## ##  ",
                        " ##   ## ",
                        " #     # ",
                    };
            }
        }

        // -- Packing -------------------------------------------------------------

        private Sprite SpriteOf(string name, Vector4 border)
        {
            if (!_pieces.TryGetValue(name, out var r)) return null;
            var sprite = Sprite.Create(Atlas, new Rect(r.x, r.y, r.width, r.height), new Vector2(0.5f, 0.5f),
                                       HudArt.SpritePixelsPerUnit, 0, SpriteMeshType.FullRect, border);
            sprite.name = "Inv_" + name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private RectInt Reserve(int w, int h)
        {
            if (_cursorX + w + 1 > AtlasSize)
            {
                _cursorX = 1;
                _cursorY += _rowHeight + 1;
                _rowHeight = 0;
            }
            if (_cursorY + h + 1 > AtlasSize)
                throw new System.InvalidOperationException("InventoryArt atlas is full; raise AtlasSize.");
            var r = new RectInt(_cursorX, _cursorY, w, h);
            _cursorX += w + 1;
            _rowHeight = Mathf.Max(_rowHeight, h);
            return r;
        }

        private void AddPattern(string name, string[] rows, System.Func<char, Color32?> map, bool outline)
        {
            var px = HudArt.Rasterise(rows, map, outline, out int w, out int h);
            var r = Reserve(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    _pixels[(r.y + y) * AtlasSize + r.x + x] = px[y * w + x];
            _pieces[name] = r;
        }

        /// <summary>
        /// An engraved rule, 1 wide so it stretches without smearing: a dark line with a faint lit
        /// line under it. The ornament that sits on it is a SEPARATE piece — a 9-slice stretches
        /// its middle column, and the first cut put the diamond there, which turned the whole rule
        /// into a two-texel gold bar.
        /// </summary>
        private void AddDivider(string name)
        {
            var r = Reserve(1, 2);
            _pixels[r.y * AtlasSize + r.x] = new Color32(255, 255, 255, 30);           // lit edge
            _pixels[(r.y + 1) * AtlasSize + r.x] = new Color32(6, 6, 10, 255);          // the rule
            _pieces[name] = r;

            AddPattern(name + "_gem", new[]
            {
                "  o  ",
                " oho ",
                "ohgso",
                " oso ",
                "  o  ",
            }, CoinPixel, false);
        }

        private void AddFilet(string name)
        {
            const int n = 7;
            var r = Reserve(n, n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(n - 1 - x, n - 1 - y));
                    _pixels[(r.y + y) * AtlasSize + r.x + x] = d == 2 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            _pieces[name] = r;
        }

        private static Color32? Shade(char c)
        {
            switch (c)
            {
                case '#': return new Color32(205, 205, 205, 255);
                case 'h': return new Color32(255, 255, 255, 255);
                case 's': return new Color32(140, 140, 140, 255);
                default: return null;
            }
        }

        private static Color32? Solid(char c) => c == '#' ? (Color32?)new Color32(255, 255, 255, 255) : null;

        private static Color32? CoinPixel(char c)
        {
            switch (c)
            {
                case 'o': return new Color32(92, 60, 18, 255);
                case 'g': return new Color32(236, 190, 72, 255);
                case 'h': return new Color32(255, 243, 190, 255);
                case 's': return new Color32(176, 124, 38, 255);
                default: return null;
            }
        }
    }
}
