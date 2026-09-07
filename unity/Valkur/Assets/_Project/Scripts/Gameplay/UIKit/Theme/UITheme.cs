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

        /// <summary>
        /// "This is real, and it is probably what you meant." The middle rung between
        /// <see cref="SUCCESS"/> and <see cref="DANGER"/>, for a state that is worth seeing and
        /// is not a defect — the Controls board rings a key amber when one of the two actions
        /// on it is a held modifier rather than a gesture, which fires together by design far
        /// more often than by mistake. Amber rather than a dimmed red, because a dimmed red
        /// reads as a red that has been turned down and invites the author to fix it.
        /// </summary>
        public static readonly Color WARNING       = new Color(0.95f, 0.68f, 0.22f, 1f);

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
        /// <summary>
        /// The backdrop the drawn keyboard and mouse sit ON. Darker than everything placed on
        /// it, which is the whole job: with the board scrolling over the generic
        /// <see cref="BG_SURFACE"/> (0.13) and an unbound cap painted
        /// <see cref="INPUT_FREE"/> (0.14), the two differed by ONE PERCENT of a channel and
        /// every free key vanished into the panel. Half a keyboard with no keys on it does not
        /// read as a keyboard, and "what is free" is the question the drawn board exists to
        /// answer.
        /// </summary>
        public static readonly Color INPUT_BOARD_BG   = new Color(0.07f, 0.07f, 0.09f, 1f);

        /// <summary>The drawn mouse's shell — a physical object on the backdrop, so it needs to
        /// be lighter than the board and darker than the buttons moulded into it.</summary>
        public static readonly Color INPUT_DEVICE_BODY = new Color(0.17f, 0.17f, 0.21f, 1f);

        public static readonly Color INPUT_FREE       = new Color(0.20f, 0.20f, 0.25f, 1f);
        public static readonly Color INPUT_MOVEMENT   = new Color(0.18f, 0.30f, 0.24f, 1f);
        public static readonly Color INPUT_TRAVERSAL  = new Color(0.18f, 0.32f, 0.34f, 1f);
        public static readonly Color INPUT_COMBAT     = new Color(0.36f, 0.18f, 0.18f, 1f);
        public static readonly Color INPUT_SPELL      = new Color(0.28f, 0.20f, 0.38f, 1f);
        public static readonly Color INPUT_INTERACT   = new Color(0.20f, 0.26f, 0.36f, 1f);
        public static readonly Color INPUT_INTERFACE  = new Color(0.24f, 0.24f, 0.30f, 1f);
        public static readonly Color INPUT_EDITOR     = new Color(0.32f, 0.28f, 0.16f, 1f);
        public static readonly Color INPUT_SYSTEM     = new Color(0.22f, 0.22f, 0.26f, 1f);

        // ── World-space instance markers (the Alt overlays) ───────────────────
        //
        // Several editors hide something the player never sees and reveal it on Alt: the
        // Spawner editor its spawners, the Lighting editor the reach of each placed light. The
        // shape is one ring per instance plus a centre dot that lights up under the cursor, and
        // the dot pair was copied verbatim into the second editor before it became a token —
        // the same route SCROLL_TRACK and DANGER_IDLE took. Tokens rather than constants
        // because two overlays whose click affordance is a different yellow teach the author
        // two things where there is one.

        /// <summary>A marker ring around an instance this editor can move, delete and save.</summary>
        public static readonly Color MARKER_RING        = new Color(1f, 0.82f, 0.30f, 0.85f);

        /// <summary>
        /// A marker ring around an instance the editor will REFUSE to edit — a light owned by
        /// a building, say. Drawn, because the author wondering why a corner is bright needs to
        /// see it; drawn differently, because a marker identical to the editable one promises
        /// an edit that cannot happen.
        /// </summary>
        public static readonly Color MARKER_RING_LOCKED = new Color(0.45f, 0.70f, 1f, 0.55f);

        /// <summary>The clickable centre of a marker, at rest.</summary>
        public static readonly Color MARKER_DOT         = new Color(1f, 0.95f, 0.30f, 1f);

        /// <summary>The same centre with the cursor on it — "click to select".</summary>
        public static readonly Color MARKER_DOT_HOVER   = new Color(0.40f, 1f, 1f, 1f);

        // ── Layout ──
        public const float PANEL_PAD       = 10f;
        public const float SECTION_SPACING = 6f;
        public const float SIDEBAR_WIDTH   = 300f;
    }
}
