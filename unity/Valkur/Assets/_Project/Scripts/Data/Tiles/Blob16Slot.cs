namespace Valkur.Data
{
    /// <summary>
    /// Slot in a 16-tile blob auto-tiling model (4-bit cardinal connectivity).
    ///
    /// Bit layout (matches <c>BitmaskCalculator</c>):
    ///   bit 0 = N neighbor is same terrain
    ///   bit 1 = E neighbor is same terrain
    ///   bit 2 = S neighbor is same terrain
    ///   bit 3 = W neighbor is same terrain
    ///
    /// Each enum name lists the cardinal directions whose neighbor IS the same terrain.
    /// "Isolated" = no same-terrain neighbors (visible borders on all 4 sides).
    /// "Center" = all 4 cardinal neighbors are same terrain (no visible borders).
    /// </summary>
    public enum Blob16Slot : byte
    {
        Isolated   = 0b0000,
        ConnectN   = 0b0001,
        ConnectE   = 0b0010,
        ConnectNE  = 0b0011,
        ConnectS   = 0b0100,
        ConnectNS  = 0b0101,
        ConnectES  = 0b0110,
        ConnectNES = 0b0111,
        ConnectW   = 0b1000,
        ConnectNW  = 0b1001,
        ConnectEW  = 0b1010,
        ConnectNEW = 0b1011,
        ConnectSW  = 0b1100,
        ConnectNSW = 0b1101,
        ConnectESW = 0b1110,
        Center     = 0b1111
    }

    /// <summary>
    /// Slot in a 16-tile CORNER auto-tiling model (4-bit corner majority). Used by
    /// transition tilesets whose art is legitimately corner-Wang rather than
    /// edge-Wang — measured across 13 real packs under <c>Resources/Tiles/</c>,
    /// borders sample as a genuine 50/50 blend of both materials (a diagonal cut
    /// has no single "owner"), while corners never cross the cut. See
    /// <c>BitmaskCalculator.CornerMask</c> (Gameplay assembly) for how the mask is
    /// derived from a cell-based <c>TerrainMap</c> without a dual grid.
    ///
    /// Bit layout (matches <c>BitmaskCalculator</c>, and the offline
    /// <c>tools/atlas/generated/tile_rulesets.json</c> corner order "NW,NE,SE,SW" —
    /// a 4-character JSON key like "0110" parses directly as this byte via
    /// <c>Convert.ToByte(key, 2)</c>):
    ///   bit 3 (0x8) = NW corner shows the secondary material
    ///   bit 2 (0x4) = NE corner shows the secondary material
    ///   bit 1 (0x2) = SE corner shows the secondary material
    ///   bit 0 (0x1) = SW corner shows the secondary material
    ///
    /// Each enum name lists the corners whose majority material is the SECONDARY
    /// terrain (<c>TilesetRuleset.TerrainSecondary</c>). "CornerNone" = solid
    /// primary tile (no secondary anywhere). "CornerFull" = solid secondary tile
    /// (fully surrounded).
    /// </summary>
    public enum Corner16Slot : byte
    {
        CornerNone   = 0b0000,
        CornerNW     = 0b1000,
        CornerNE     = 0b0100,
        CornerNWNE   = 0b1100,
        CornerSE     = 0b0010,
        CornerNWSE   = 0b1010,
        CornerNESE   = 0b0110,
        CornerNWNESE = 0b1110,
        CornerSW     = 0b0001,
        CornerNWSW   = 0b1001,
        CornerNESW   = 0b0101,
        CornerNWNESW = 0b1101,
        CornerSESW   = 0b0011,
        CornerNWSESW = 0b1011,
        CornerNESESW = 0b0111,
        CornerFull   = 0b1111
    }

    /// <summary>
    /// Auto-tile model used by a <c>TilesetRuleset</c>.
    /// Blob16 (4-bit cardinal, 16 slots) is the v1 default. Blob47 (8-bit with inner
    /// corners, 47 slots) is reserved for v2 — its values exist so legacy assets
    /// upgrade without losing data. Corner16 (4-bit corner majority, 16 slots,
    /// <see cref="Corner16Slot"/>) is the v3 model for two-material transition
    /// tilesets whose art is drawn corner-first (grass/dirt, sand/grass, ...).
    /// New values are appended, never renumbered — serialized assets reference
    /// these numbers directly.
    /// </summary>
    public enum AutoTileModel : byte
    {
        Blob16 = 0,
        Blob47 = 1,
        Corner16 = 2
    }
}