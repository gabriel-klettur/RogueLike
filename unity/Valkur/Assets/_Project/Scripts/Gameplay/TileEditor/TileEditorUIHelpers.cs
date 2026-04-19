using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Design tokens and reusable UI factory methods for the tile editor.
    /// Extracted from TileEditorUI to isolate styling and primitive construction.
    /// </summary>
    public static class TileEditorUIHelpers
    {
        // ── Design Tokens ──
        public static readonly Color BG_PANEL       = new Color(0.09f, 0.09f, 0.12f, 0.94f);
        public static readonly Color BG_SURFACE     = new Color(0.13f, 0.13f, 0.17f, 1f);
        public static readonly Color BG_ELEVATED    = new Color(0.17f, 0.17f, 0.22f, 1f);
        public static readonly Color ACCENT         = new Color(0.90f, 0.76f, 0.38f, 1f);
        public static readonly Color ACCENT_DIM     = new Color(0.90f, 0.76f, 0.38f, 0.45f);
        public static readonly Color ACCENT_BG      = new Color(0.90f, 0.76f, 0.38f, 0.15f);
        public static readonly Color TEXT_PRIMARY    = new Color(0.93f, 0.93f, 0.96f, 1f);
        public static readonly Color TEXT_SECONDARY  = new Color(0.60f, 0.62f, 0.68f, 1f);
        public static readonly Color TEXT_MUTED      = new Color(0.42f, 0.44f, 0.50f, 1f);
        public static readonly Color BTN_NORMAL      = new Color(0.16f, 0.16f, 0.21f, 1f);
        public static readonly Color BTN_HOVER       = new Color(0.22f, 0.22f, 0.28f, 1f);
        public static readonly Color BTN_ACTIVE      = new Color(0.90f, 0.76f, 0.38f, 0.55f);
        public static readonly Color SLOT_BG         = new Color(0.13f, 0.13f, 0.17f, 1f);
        public static readonly Color SLOT_HOVER      = new Color(0.22f, 0.22f, 0.28f, 1f);
        public static readonly Color SLOT_SELECTED   = new Color(0.90f, 0.76f, 0.38f, 0.65f);
        public static readonly Color LAYER_ACTIVE_BG = new Color(0.90f, 0.76f, 0.38f, 0.12f);
        public static readonly Color VIS_ON          = new Color(0.40f, 0.88f, 0.40f, 1f);
        public static readonly Color VIS_OFF         = new Color(0.50f, 0.50f, 0.50f, 0.45f);
        public static readonly Color BORDER          = new Color(0.90f, 0.76f, 0.38f, 0.35f);
        public static readonly Color SEPARATOR       = new Color(0.25f, 0.25f, 0.30f, 0.6f);
        public static readonly Color CYAN_ACCENT     = new Color(0.30f, 0.85f, 0.90f, 1f);
        public static readonly Color GREEN_ACCENT    = new Color(0.30f, 0.90f, 0.45f, 1f);

        // Colliders panel — bright red fill+border applied over collision tiles
        // (the underlying tile is invisible, so this is the only visible cue).
        public static readonly Color COLLIDER_FILL   = new Color(1f, 0.10f, 0.15f, 0.32f);
        public static readonly Color COLLIDER_BORDER = new Color(1f, 0.10f, 0.15f, 1f);
        public static readonly Color RED_ACCENT      = new Color(1f, 0.32f, 0.36f, 1f);

        // ── Layout Constants ──
        public const float LEFT_WIDTH = 300f;
        public const float RIGHT_WIDTH = 230f;
        public const float PANEL_PAD = 10f;
        public const float SECTION_SPACING = 6f;
        public const float INNER_PAD = 10f;

        // ── Dock Layout (panel placement) ──
        /// <summary>Pixel gap between panels and screen edges / between docked panels.</summary>
        public const float PANEL_GAP = 8f;
        /// <summary>Vertical offset from the top of the screen to the first row of panels (sits below the menu bar).</summary>
        public const float PANEL_TOP_OFFSET = 34f; // = MENUBAR_HEIGHT (30) + 4

        /// <summary>Anchor corner for a docked dropdown panel.</summary>
        public enum PanelDock
        {
            /// <summary>Anchored to the top-left corner. Offsets are pixels right (x) and pixels down (y).</summary>
            TopLeft,
            /// <summary>Anchored to the top-right corner. Offsets are pixels left from right edge (x) and pixels down (y).</summary>
            TopRight,
            /// <summary>Anchored to the bottom-left corner. Offsets are pixels right (x) and pixels up (y).</summary>
            BottomLeft,
            /// <summary>Anchored to the bottom-right corner. Offsets are pixels left from right edge (x) and pixels up (y).</summary>
            BottomRight
        }

        // ── Menu Bar ──
        public const float MENUBAR_HEIGHT = 30f;
        public const float MENUBAR_SPACING = 3f;
        public const float MENUBAR_PAD_H = 10f;
        public static readonly Color MENUBAR_BG = new Color(0.07f, 0.07f, 0.09f, 0.97f);
        public static readonly Color MENU_BTN_NORMAL = new Color(0.07f, 0.07f, 0.09f, 0f);
        public static readonly Color MENU_BTN_HOVER = new Color(0.22f, 0.22f, 0.28f, 1f);
        public static readonly Color MENU_BTN_OPEN = new Color(0.90f, 0.76f, 0.38f, 0.18f);
        public static readonly Color DROPDOWN_BG = new Color(0.09f, 0.09f, 0.12f, 0.97f);
        public static readonly Color DROPDOWN_BORDER = new Color(0.90f, 0.76f, 0.38f, 0.25f);

        // ── Menu Button Widths (for dropdown positioning) ──
        public const float TITLE_W = 110f;
        public const float TOOLS_BTN_W = 66f;
        public const float TILES_BTN_W = 62f;
        public const float LAYERS_BTN_W = 72f;
        public const float INSPECTOR_BTN_W = 86f;
        // Colliders dropdown sits to the LEFT of Inspector on the top-right side.
        public const float COLLIDERS_BTN_W = 84f;
        // Size dropdown sits to the LEFT of Colliders on the top-right side.
        public const float SIZE_BTN_W = 64f;
        // Dropdown widths/heights
        // Compact icon toolbar: inner width = 60 - 8(L) - 8(R) = 44  =  BTN_H → perfect square buttons
        public const float TOOLS_DROP_W = 60f;
        public const float TOOLS_DROP_H = 460f;
        public const float TILES_DROP_W = 256f;
        public const float TILES_DROP_H = 540f;
        /// <summary>Number of columns in the tile picker grid.</summary>
        public const int TILES_GRID_COLS = 4;
        /// <summary>Spacing between cells in the tile picker grid.</summary>
        public const float TILES_GRID_SPACING = 4f;
        /// <summary>Width of the visible vertical scrollbar reserved space inside scroll panels.</summary>
        public const float TILES_SCROLLBAR_W = 12f;
        /// <summary>Square cell size for the 4-column tile picker. Sized to roughly match the in-game tile footprint.
        /// Derived from: TILES_DROP_W(256) - VerticalLayoutGroup padding(8+8) - Scrollbar(12) - GridLayout padding(4+4) - 3*spacing(4) = 212, /4 = 53. We use 52 for a clean integer.</summary>
        public const float TILES_CELL_SIZE = 52f;
        /// <summary>Full inner row width (used by the categories list which spans the whole panel width minus the scrollbar).</summary>
        public const float TILES_ROW_WIDTH = TILES_DROP_W - 16f - TILES_SCROLLBAR_W - 8f;
        public const float LAYERS_DROP_W = 240f;
        public const float LAYERS_DROP_H = 300f;
        public const float INSPECTOR_DROP_W = 250f;
        public const float INSPECTOR_DROP_H = 256f;
        public const float COLLIDERS_DROP_W = 230f;
        public const float COLLIDERS_DROP_H = 220f;
        public const float SIZE_DROP_W = 200f;
        public const float SIZE_DROP_H = 150f;

        // ── Factory Methods ──

        public static GameObject CreateUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        public static GameObject MakePanel(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = CreateUI(name, parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.pivot = pivot;
            r.anchoredPosition = anchoredPos; r.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = BG_PANEL;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = BORDER; ol.effectDistance = new Vector2(1f, 1f);
            return go;
        }

        public static void MakeBtn(GameObject go, string label, UnityEngine.Events.UnityAction onClick, float fontSize = 13f)
        {
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;
            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            AddCenteredText(go.transform, label, fontSize, FontStyles.Bold, TEXT_PRIMARY);
        }

        public static TextMeshProUGUI AddCenteredText(Transform parent, string text, float size, FontStyles style, Color color)
        {
            var go = CreateUI("Txt", parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.sizeDelta = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = color;
            return tmp;
        }

        public static void StretchFill(GameObject go)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.sizeDelta = Vector2.zero;
        }

        public static void BuildSectionHeader(Transform parent, string text, float fontSize)
        {
            var go = CreateUI("Header_" + text, parent);
            go.AddComponent<LayoutElement>().preferredHeight = fontSize + 6f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = fontSize; tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = ACCENT;
            tmp.characterSpacing = 4f;
        }

        public static void BuildSectionLabel(Transform parent, string text)
        {
            var go = CreateUI("Label_" + text, parent);
            go.AddComponent<LayoutElement>().preferredHeight = 18f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 11f; tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Left; tmp.color = TEXT_SECONDARY;
            tmp.characterSpacing = 2f;
        }

        public static void BuildSeparator(Transform parent)
        {
            var go = CreateUI("Sep", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 1f;
            go.AddComponent<Image>().color = SEPARATOR;
        }
    }
}
