using UnityEngine;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Centralized constants for Tile Editor to avoid duplication across partial classes.
    /// </summary>
    public static class TileEditorConstants
    {
        // Brush size range
        public const int MinBrushSize = 1;
        public const int MaxBrushSize = 25;

        /// <summary>
        /// Shown when Brush or Fill is clicked with no tile picked. Both tools
        /// used to return silently in that state, which reads as "the editor is
        /// broken" rather than "you have not chosen what to paint yet".
        /// </summary>
        public const string NoTileSelectedHint =
            "No tile selected - pick one from the TILES panel before painting.";

        /// <summary>
        /// Shown when the AUTO brush modifier can't paint because the pack of the
        /// currently selected tile has no usable ruleset — either no
        /// <c>TilesetRuleset</c> asset exists for that folder, or the folder's
        /// ruleset is a transition (Corner16, always two-material) with no matching
        /// base (single-material) ruleset registered for its primary terrain.
        /// Without this, AUTO would silently paint nothing on every stroke — the
        /// exact "broken editor" failure mode <see cref="NoTileSelectedHint"/>
        /// already exists to avoid.
        /// </summary>
        public const string NoRulesetForCategoryHint =
            "AUTO needs a base ruleset for this tile's terrain - configure one before painting.";

        // ── Clipboard (copied-cells) highlight ────────────────────────────────

        /// <summary>
        /// Thick yellow border drawn on the map GL overlay to mark every cell
        /// that is currently in the tile clipboard (Copy or Cut source).
        /// Distinct from the green "selected" outline so clipboard state is
        /// visible independently of the current selection.
        /// </summary>
        public static readonly Color ClipboardOutlineColor = new Color(1f, 0.85f, 0.15f, 1f);

        /// <summary>
        /// Thickness in screen-pixels of the clipboard outline (multi-pass GL quad).
        /// Thicker than the hover ring so it reads clearly over the green selection.
        /// </summary>
        public const float ClipboardOutlineThicknessPx = 4f;

        // ── Picker slot copy-highlight ─────────────────────────────────────────

        /// <summary>
        /// Stroke color for the CopyHL frame on picker slots that are currently in
        /// the clipboard. The CopyHL is built as four thin Image strips (top /
        /// bottom / left / right) forming a frame around the slot — the tile preview
        /// in the centre stays fully visible. Opaque on purpose: a transparent
        /// border reads as a fill at small sizes.
        /// </summary>
        public static readonly Color PickerCopyHighlightColor = new Color(1f, 0.85f, 0.15f, 1f);

        /// <summary>
        /// Thickness in pixels of each CopyHL strip. ~3 px reads clearly at the
        /// default picker zoom (32–64 px slot size) without overpowering the tile
        /// preview underneath.
        /// </summary>
        public const float PickerCopyHighlightBorderPx = 3f;

        // ── Overlay persistence (auto-save debounce) ───────────────────────────

        /// <summary>
        /// Quiet period (seconds, real time) after the last tile/terrain/
        /// collision-tag/layer-jump edit before <see cref="TileOverlayPersistence"/>'s
        /// deferred autosave pump flushes the affected zones to disk. Coalesces a
        /// burst of separate strokes (each of which still calls the synchronous,
        /// immediate <c>SaveAllDirty()</c> on its own mouse-up today) into a single
        /// background write whenever a caller marks cells dirty without also
        /// forcing an immediate flush right after. Only armed while
        /// <c>Application.isPlaying</c> — never during EditMode tests.
        /// </summary>
        public const float AutosaveDebounceSeconds = 0.4f;
    }
}
