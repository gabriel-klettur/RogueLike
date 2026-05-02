using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Runtime-only tile clipboard captured from a Select / Cut / Copy operation.
    /// The grid is laid out so that <c>Tiles[dx, dy]</c> corresponds to cell
    /// <c>(SourceBounds.xMin + dx, SourceBounds.yMin + dy)</c> on the source tilemap —
    /// i.e. <c>dx</c> grows right (+X) and <c>dy</c> grows up (+Y), matching Unity's
    /// tilemap world axes. Pasting flips <c>dy</c> so that the cursor (top-left anchor)
    /// extends right-and-down, matching the brush footprint convention used everywhere
    /// else in the editor.
    ///
    /// Not a ScriptableObject: the clipboard is in-memory only and intentionally lost
    /// when the editor is closed, mirroring how OS clipboards work for runtime tools.
    /// </summary>
    public sealed class TileClipboard
    {
        public TileBase[,] Tiles;
        public BoundsInt SourceBounds;
        public TilemapLayerSetup.TilemapLayer SourceLayer;
        public bool IsCut;

        public int Width  => SourceBounds.size.x;
        public int Height => SourceBounds.size.y;

        public bool IsEmpty => Tiles == null || Width <= 0 || Height <= 0;
    }
}
