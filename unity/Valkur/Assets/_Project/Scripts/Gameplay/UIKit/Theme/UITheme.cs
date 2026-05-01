using UnityEngine;

namespace Valkur.UIKit
{
    /// <summary>
    /// Single source of truth for UI design tokens (colors, paddings, sizes).
    /// Consumed by every panel, button, label, slider, modal and toolbar in
    /// the kit so the visual language stays consistent across the in-game
    /// editors (Tile, Map, Buildings, FSM, Items, Spells, Particles, Lighting,
    /// Inventory) and the runtime HUD widgets (MusicPlayer, SpellBar, etc.).
    /// </summary>
    public static class UITheme
    {
        // ── Surfaces / backgrounds ──
        public static readonly Color BG_PANEL      = new Color(0.09f, 0.09f, 0.12f, 0.94f);
        public static readonly Color BG_SURFACE    = new Color(0.13f, 0.13f, 0.17f, 1f);
        public static readonly Color BG_ELEVATED   = new Color(0.17f, 0.17f, 0.22f, 1f);
        public static readonly Color BG_HEADER     = new Color(0.07f, 0.07f, 0.09f, 0.98f);

        // ── Accent (gold) ──
        public static readonly Color ACCENT        = new Color(0.90f, 0.76f, 0.38f, 1f);
        public static readonly Color ACCENT_DIM    = new Color(0.90f, 0.76f, 0.38f, 0.45f);
        public static readonly Color ACCENT_BG     = new Color(0.90f, 0.76f, 0.38f, 0.15f);

        // ── Text ──
        public static readonly Color TEXT_PRIMARY  = new Color(0.93f, 0.93f, 0.96f, 1f);
        public static readonly Color TEXT_SECONDARY = new Color(0.60f, 0.62f, 0.68f, 1f);
        public static readonly Color TEXT_MUTED    = new Color(0.42f, 0.44f, 0.50f, 1f);

        // ── Buttons ──
        public static readonly Color BTN_NORMAL    = new Color(0.16f, 0.16f, 0.21f, 1f);
        public static readonly Color BTN_HOVER     = new Color(0.22f, 0.22f, 0.28f, 1f);
        public static readonly Color BTN_ACTIVE    = new Color(0.90f, 0.76f, 0.38f, 0.55f);

        // ── Slot grids ──
        public static readonly Color SLOT_BG       = new Color(0.13f, 0.13f, 0.17f, 1f);
        public static readonly Color SLOT_HOVER    = new Color(0.22f, 0.22f, 0.28f, 1f);
        public static readonly Color SLOT_SELECTED = new Color(0.90f, 0.76f, 0.38f, 0.65f);

        // ── Lines / overlays ──
        public static readonly Color BORDER        = new Color(0.90f, 0.76f, 0.38f, 0.35f);
        public static readonly Color SEPARATOR     = new Color(0.25f, 0.25f, 0.30f, 0.6f);

        // ── State colors ──
        public static readonly Color DANGER        = new Color(0.90f, 0.30f, 0.30f, 1f);
        public static readonly Color SUCCESS       = new Color(0.30f, 0.90f, 0.45f, 1f);

        // ── Layout ──
        public const float PANEL_PAD       = 10f;
        public const float SECTION_SPACING = 6f;
        public const float SIDEBAR_WIDTH   = 300f;
    }
}
