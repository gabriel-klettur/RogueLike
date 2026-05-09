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
    /// Auto-tile model used by a <c>TilesetRuleset</c>.
    /// Blob16 (4-bit cardinal, 16 slots) is the v1 default. Blob47 (8-bit with inner
    /// corners, 47 slots) is reserved for v2 — its values exist so legacy assets
    /// upgrade without losing data.
    /// </summary>
    public enum AutoTileModel : byte
    {
        Blob16 = 0,
        Blob47 = 1
    }
}