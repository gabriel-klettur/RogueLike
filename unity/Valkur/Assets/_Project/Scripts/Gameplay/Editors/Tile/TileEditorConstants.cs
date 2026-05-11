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
    }
}
