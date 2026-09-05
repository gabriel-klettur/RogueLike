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

        // ── Scrollbars ──
        //
        // The track and its handle were hand-copied, verbatim, into five editors
        // (Items, Map, Particles, Spells, Tile) — twelve and ten sites. Nothing was
        // wrong with any single copy; what was wrong is that restyling the scrollbar
        // meant finding five files, and the track had already drifted: it is 0.85
        // alpha here while the panel behind it is 0.82, which is a value somebody
        // meant to match and mistyped.
        public static readonly Color SCROLL_TRACK  = new Color(0.08f, 0.08f, 0.10f, 0.85f);
        public static readonly Color SCROLL_HANDLE = new Color(0.55f, 0.45f, 0.22f, 0.85f);

        // ── Lines / overlays ──
        public static readonly Color BORDER        = new Color(0.90f, 0.76f, 0.38f, 0.35f);
        /// <summary>
        /// Hard, fully opaque yellow for the selected-item frame in picker grids.
        /// Deliberately louder than <see cref="SLOT_SELECTED"/>: a translucent
        /// background tint disappears entirely behind a slot whose icon or live
        /// preview covers the whole cell.
        /// </summary>
        public static readonly Color SELECTION_BORDER = new Color(1f, 0.84f, 0.20f, 1f);
        public static readonly Color SEPARATOR     = new Color(0.25f, 0.25f, 0.30f, 0.6f);

        // ── State colors ──
        public static readonly Color DANGER        = new Color(0.90f, 0.30f, 0.30f, 1f);

        /// <summary>
        /// A destructive control at REST — the delete button before it is armed.
        /// <see cref="DANGER"/> is the same control once it is active.
        ///
        /// Copied by hand into nineteen sites across Boss, Buildings and FSM, one of
        /// which had already named it <c>dangerBase</c> locally. Two shades of "this
        /// deletes something" is exactly the pair that must not drift apart: the whole
        /// signal is that the armed one is BRIGHTER than the idle one.
        /// </summary>
        public static readonly Color DANGER_IDLE   = new Color(0.55f, 0.15f, 0.15f, 1f);
        public static readonly Color SUCCESS       = new Color(0.30f, 0.90f, 0.45f, 1f);

        // ── Modal scrim ──────────────────────────────────────────────────────
        /// <summary>Full-screen dim behind a capture or a modal. Dark enough that the panel
        /// below stops competing, light enough that the author can still see what they are
        /// about to rebind.</summary>
        public static readonly Color OVERLAY_SCRIM = new Color(0f, 0f, 0f, 0.72f);

        // ── Input category tints (the drawn keyboard) ─────────────────────────
        //
        // One fill per InputActionCategory, so a key cap says what KIND of verb is on it
        // before the reader has parsed the label. Deliberately low-saturation, with ACCENT
        // and DANGER left free for selection and conflict: a board where every key shouts is
        // a board where the two things that matter do not.
        public static readonly Color INPUT_FREE       = new Color(0.14f, 0.14f, 0.18f, 1f);
        public static readonly Color INPUT_MOVEMENT   = new Color(0.18f, 0.30f, 0.24f, 1f);
        public static readonly Color INPUT_TRAVERSAL  = new Color(0.18f, 0.32f, 0.34f, 1f);
        public static readonly Color INPUT_COMBAT     = new Color(0.36f, 0.18f, 0.18f, 1f);
        public static readonly Color INPUT_SPELL      = new Color(0.28f, 0.20f, 0.38f, 1f);
        public static readonly Color INPUT_INTERACT   = new Color(0.20f, 0.26f, 0.36f, 1f);
        public static readonly Color INPUT_INTERFACE  = new Color(0.24f, 0.24f, 0.30f, 1f);
        public static readonly Color INPUT_EDITOR     = new Color(0.32f, 0.28f, 0.16f, 1f);
        public static readonly Color INPUT_SYSTEM     = new Color(0.22f, 0.22f, 0.26f, 1f);

        // ── Layout ──
        public const float PANEL_PAD       = 10f;
        public const float SECTION_SPACING = 6f;
        public const float SIDEBAR_WIDTH   = 300f;
    }
}
