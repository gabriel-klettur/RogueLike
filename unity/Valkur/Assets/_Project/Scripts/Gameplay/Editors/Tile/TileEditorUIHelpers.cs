using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.UIKit;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// TileEditor-specific design tokens and a couple of factory shims that
    /// keep the TileEditor's particular look (slightly darker panel bg, gray
    /// outlines instead of gold) and its tile-grid + dropdown geometry.
    ///
    /// All the generic GameObject builders (panel, button, label, scroll,
    /// separator) are now in <see cref="Valkur.UIKit"/> — this class only
    /// holds what is genuinely unique to the tile editor's UX.
    /// </summary>
    public static class TileEditorUIHelpers
    {
        // ── Design tokens forwarded from UITheme (kept as aliases so the
        //    files that read TileEditorUIHelpers.X via `using static` keep
        //    compiling without changes) ──
        public static readonly Color BG_SURFACE       = UITheme.BG_SURFACE;
        public static readonly Color BG_ELEVATED      = UITheme.BG_ELEVATED;
        public static readonly Color ACCENT           = UITheme.ACCENT;
        public static readonly Color ACCENT_DIM       = UITheme.ACCENT_DIM;
        public static readonly Color ACCENT_BG        = UITheme.ACCENT_BG;
        public static readonly Color TEXT_PRIMARY     = UITheme.TEXT_PRIMARY;
        public static readonly Color TEXT_SECONDARY   = UITheme.TEXT_SECONDARY;
        public static readonly Color TEXT_MUTED       = UITheme.TEXT_MUTED;
        public static readonly Color BTN_NORMAL       = UITheme.BTN_NORMAL;
        public static readonly Color BTN_HOVER        = UITheme.BTN_HOVER;
        public static readonly Color BTN_ACTIVE       = UITheme.BTN_ACTIVE;
        public static readonly Color SLOT_BG          = UITheme.SLOT_BG;
        public static readonly Color SLOT_HOVER       = UITheme.SLOT_HOVER;
        public static readonly Color SLOT_SELECTED    = UITheme.SLOT_SELECTED;
        public static readonly Color SEPARATOR        = UITheme.SEPARATOR;

        // ── TileEditor-specific overrides (intentionally different from UITheme) ──
        public static readonly Color BG_PANEL       = new Color(0.08f, 0.08f, 0.10f, 0.82f);
        public static readonly Color BORDER         = new Color(0.20f, 0.22f, 0.28f, 0.65f);

        // ── TileEditor-only tokens ──
        public static readonly Color LAYER_ACTIVE_BG = new Color(0.90f, 0.76f, 0.38f, 0.12f);
        public static readonly Color VIS_ON          = new Color(0.40f, 0.88f, 0.40f, 1f);
        public static readonly Color VIS_OFF         = new Color(0.50f, 0.50f, 0.50f, 0.45f);
        public static readonly Color CYAN_ACCENT     = new Color(0.30f, 0.85f, 0.90f, 1f);
        public static readonly Color GREEN_ACCENT    = new Color(0.30f, 0.90f, 0.45f, 1f);

        // Colliders panel — bright red fill+border applied over collision tiles
        // (the underlying tile is invisible, so this is the only visible cue).
        public static readonly Color COLLIDER_FILL   = new Color(1f, 0.10f, 0.15f, 0.32f);
        public static readonly Color COLLIDER_BORDER = new Color(1f, 0.10f, 0.15f, 1f);
        public static readonly Color RED_ACCENT      = new Color(1f, 0.32f, 0.36f, 1f);

        // ── Layout constants ──
        public const float LEFT_WIDTH = 300f;
        public const float RIGHT_WIDTH = 230f;
        public const float PANEL_PAD = 10f;
        public const float SECTION_SPACING = 6f;
        public const float INNER_PAD = 10f;

        // ── Dock layout (panel placement) ──
        /// <summary>Pixel gap between panels and screen edges / between docked panels.</summary>
        public const float PANEL_GAP = 8f;
        /// <summary>Vertical offset from the top of the screen to the first row of panels (sits below the menu bar).</summary>
        public const float PANEL_TOP_OFFSET = 34f; // = MENUBAR_HEIGHT (30) + 4

        public enum PanelDock
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        // ── Draggable panel header ──
        public const float PANEL_HDR_H = 24f;
        public const float PANEL_HDR_BTN_W = 22f;
        public static readonly Color PANEL_HDR_BG          = new Color(0.06f, 0.06f, 0.08f, 0.92f);
        public static readonly Color PANEL_HDR_TITLE       = new Color(0.93f, 0.93f, 0.96f, 1f);
        public static readonly Color PANEL_HDR_SEP         = new Color(0.30f, 0.32f, 0.38f, 0.55f);
        public static readonly Color PANEL_BORDER          = new Color(0.20f, 0.22f, 0.28f, 0.65f);
        public static readonly Color PANEL_HDR_BTN_HOVER   = new Color(0.28f, 0.28f, 0.36f, 1f);
        public static readonly Color PANEL_HDR_CLOSE_HOVER = new Color(0.72f, 0.10f, 0.10f, 0.9f);

        // ── Menu bar ──
        public const float MENUBAR_HEIGHT = 30f;
        public const float MENUBAR_SPACING = 3f;
        public const float MENUBAR_PAD_H = 10f;
        public static readonly Color MENUBAR_BG       = new Color(0.07f, 0.07f, 0.09f, 0.97f);
        public static readonly Color MENU_BTN_NORMAL  = new Color(0.07f, 0.07f, 0.09f, 0f);
        public static readonly Color MENU_BTN_HOVER   = new Color(0.22f, 0.22f, 0.28f, 1f);
        public static readonly Color MENU_BTN_OPEN    = new Color(0.90f, 0.76f, 0.38f, 0.18f);
        public static readonly Color DROPDOWN_BG      = new Color(0.09f, 0.09f, 0.12f, 0.97f);
        public static readonly Color DROPDOWN_BORDER  = new Color(0.90f, 0.76f, 0.38f, 0.25f);

        // ── Menu button widths (for dropdown positioning) ──
        public const float TITLE_W = 110f;
        public const float TOOLS_BTN_W = 66f;
        public const float TILES_BTN_W = 62f;
        public const float LAYERS_BTN_W = 72f;
        public const float INSPECTOR_BTN_W = 86f;
        public const float COLLIDERS_BTN_W = 84f;
        public const float SIZE_BTN_W = 64f;
        public const float VIEW_BTN_W = 64f;
        public const float JUMPS_BTN_W = 72f; // "Jumps v" — slightly wider than View to fit the s
        public const float PLAYER_LAYER_BTN_W = 100f; // "Player Layer v" — wider for the longer label

        public const float UX_BTN_W     = 50f;
        public const float PERF_BTN_W   = 60f;
        public const float PANELS_BTN_W = 68f;

        // Compact icon toolbar: inner width = 60 - 8(L) - 8(R) = 44 = BTN_H → perfect square buttons.
        public const float TOOLS_DROP_W = 60f;
        // 6 tool buttons (Select/Brush/Erase/Fill/Pick/Auto) + Undo/Redo + 1 separator +
        // paddings ≈ 410. Save was removed since every edit auto-saves on mouse-up
        // (see persistence flushes in the brush/eraser/fill/colliders/cut/paste handlers).
        public const float TOOLS_DROP_H = 410f + PANEL_HDR_H;   // 434
        // Wider than the historical 256 px so the top row (SELECTED preview +
        // RULESET button on the left, CATEGORIES on the right) has comfortable
        // breathing room. With 256 the SELECTED tile name and "NO RULESET FOR
        // CATEGORY" both wrapped to 2-3 lines, which the user flagged as
        // "muy pegados". 384 lets each side of the top row settle at ~200/160 px
        // and the TILES grid below also gets noticeably more tiles per visible
        // row, since the picker viewport scales with the panel content width.
        public const float TILES_DROP_W = 384f;
        public const float TILES_DROP_H = 540f + PANEL_HDR_H;   // 564
        public const int   TILES_GRID_COLS = 4;
        public const float TILES_GRID_SPACING = 4f;
        public const float TILES_SCROLLBAR_W = 12f;
        // 4-column tile picker cell when a legacy (non-tilesheet) category is
        // active. Bumped up from 52 → 64 since the wider panel has the room.
        public const float TILES_CELL_SIZE = 64f;
        public const float TILES_ROW_WIDTH = TILES_DROP_W - 16f - TILES_SCROLLBAR_W - 8f;
        // Layers panel — sized to fit the longest layer name ("OverheadDetails", 15 chars
        // at fontSize 11) plus the Vis (16) + Idx (18) icons and inner padding (~50 px).
        public const float LAYERS_DROP_W = 155f;
        public const float LAYERS_DROP_H = 300f + PANEL_HDR_H;      // 324

        // "PLAYER LAYER" diagnostic panel — appears bottom-right, immediately to
        // the LEFT of the Layers dropdown. Visible whenever the Tile Editor is
        // active AND the View panel's "Show Player Layer" toggle is ON (default
        // ON). Independent of Colliders / Layer Jumps because the readout is
        // useful for any layer-related authoring. Shows the player's logical
        // layer (from VisualLayerOccupant) + a snapshot of which visual layers
        // have a tile underfoot (from VisualLayerProbe).
        public const float PLAYER_LAYER_DROP_W = 220f;
        // 3 readout rows: Layer / Underfoot / Cell — 22px each + a touch of padding.
        public const float PLAYER_LAYER_DROP_H = 90f + PANEL_HDR_H;  // 114

        // M1.8 "LAYER JUMPS" panel — mirrors the Colliders panel architecture
        // (Show + Draw + Erase toggles + 9-button target picker). Slightly taller
        // than Colliders to fit the larger picker row.
        public const float LAYER_JUMPS_DROP_W = 230f;
        public const float LAYER_JUMPS_DROP_H = 230f + PANEL_HDR_H;     // 254
        // Generic "properties panel" width — reused by Items/Buildings/FSM editors.
        // Don't shrink without checking those callers.
        public const float INSPECTOR_DROP_W = 250f;
        public const float INSPECTOR_DROP_H = 256f + PANEL_HDR_H;   // 280
        // Tile Editor only: the Inspector here just shows three small tile previews
        // + layer info. Sized to fit a typical 15-char tile name (e.g. "pandora_r06_c07")
        // next to the 32-px preview thumbnail; longer names fall back to ellipsis.
        public const float TILE_INSPECTOR_DROP_W = 170f;
        public const float COLLIDERS_DROP_W = 230f;
        // Note for COLLIDERS_DROP_H below: 140 → 210 to host the Apply-To-Layer section
        // (separator + header label + active-tag value label + 10-button picker row).
        // Content: Show toggle (30) + sep (1) + EDIT MODE label (16) + Draw (30) + Erase (30)
        // + VLG padding (12) + spacing × 4 (16) ≈ 135. + 1 px content/header gap.
        public const float COLLIDERS_DROP_H = 210f + PANEL_HDR_H;   // 234
        public const float SIZE_DROP_W = 200f;
        // Content: value label (32) + slider row (28) +
        // VLG padding (12) + spacing × 1 (4) ≈ 76.
        public const float SIZE_DROP_H = 78f + PANEL_HDR_H;         // 102
        public const float VIEW_DROP_W = 230f;
        public const float VIEW_DROP_H = 170f + PANEL_HDR_H;        // 194 (rolled back from 234 in M1.8c — Show Player Layer moved to menu-bar)
        public const float UX_DROP_W   = 320f;
        public const float UX_DROP_H   = 520f + PANEL_HDR_H;        // 544

        // SelectModes panel — appears immediately to the right of Tools whenever the
        // Select tool is active. Mirrors the Colliders panel layout (3 toggle rows
        // for Single/Rect/Multi + a clipboard action row + hint).
        public const float SELECT_MODES_DROP_W = 200f;
        // Height bumped from 230 → 320 to host the Move-To-Layer section
        // (separator + section label + value label + slider + footer hint).
        // The slider commits the move on pointer-release; no separate button.
        public const float SELECT_MODES_DROP_H = 320f + PANEL_HDR_H; // 344

        // ── Factory methods ──
        // Generic primitives delegate to the kit; only the TileEditor-specific
        // builders (MakePanel with darker bg, MakeBtn that adopts an existing
        // GameObject, BuildSectionLabel for the small-bold-left-aligned style)
        // stay local.

        public static GameObject CreateUI(string name, Transform parent)
            => UIFactory.CreateUI(name, parent);

        public static void StretchFill(GameObject go) => UIFactory.StretchFill(go);

        public static void BuildSeparator(Transform parent) => UISeparator.Build(parent);

        public static TextMeshProUGUI AddCenteredText(Transform parent, string text,
            float size, FontStyles style, Color color)
            => UILabel.AddCenteredText(parent, text, size, style, color);

        /// <summary>
        /// TileEditor variant of <see cref="UIPanel.Make"/> that paints the
        /// initial bg with the editor's darker <see cref="BG_PANEL"/> and the
        /// gray <see cref="BORDER"/> outline. PanelChrome (when attached) will
        /// re-paint these on enable from <c>TileEditorTheme</c>.
        /// </summary>
        public static GameObject MakePanel(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = UIFactory.CreateUI(name, parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.pivot = pivot;
            r.anchoredPosition = anchoredPos; r.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = BG_PANEL;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = BORDER; ol.effectDistance = new Vector2(1f, 1f);
            return go;
        }

        /// <summary>
        /// In-place button builder: attaches Image + Button + label to the
        /// given <paramref name="go"/> (rather than creating a new child).
        /// Used by the menu-bar buttons that have their RectTransform pre-laid
        /// by a HorizontalLayoutGroup.
        /// </summary>
        public static void MakeBtn(GameObject go, string label,
            UnityEngine.Events.UnityAction onClick, float fontSize = 13f)
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
            UILabel.AddCenteredText(go.transform, label, fontSize, FontStyles.Bold, TEXT_PRIMARY);
        }

        /// <summary>
        /// TileEditor section header: required <paramref name="fontSize"/>,
        /// height = fontSize + 6 (one row tighter than the kit's default).
        /// </summary>
        public static void BuildSectionHeader(Transform parent, string text, float fontSize)
        {
            var go = UIFactory.CreateUI("Header_" + text, parent);
            go.AddComponent<LayoutElement>().preferredHeight = fontSize + 6f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = fontSize; tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = ACCENT;
            tmp.characterSpacing = 4f;
        }

        /// <summary>
        /// Small-bold-left-aligned section label used between sections in the
        /// tile editor side panels.
        /// </summary>
        public static void BuildSectionLabel(Transform parent, string text)
        {
            var go = UIFactory.CreateUI("Label_" + text, parent);
            go.AddComponent<LayoutElement>().preferredHeight = 18f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 11f; tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Left; tmp.color = TEXT_SECONDARY;
            tmp.characterSpacing = 2f;
        }
    }
}
