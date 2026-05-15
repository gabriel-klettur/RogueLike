using UnityEngine;

namespace Valkur.Gameplay.World.Layering
{
    /// <summary>
    /// Read-only helper that observes which visual layers have a non-empty tile at a
    /// world-space point. Pure observation — does NOT mutate any entity's
    /// <see cref="VisualLayerOccupant"/>.
    ///
    /// Use cases:
    ///   • Debug HUD: show the player "you are over Ground + Decorations".
    ///   • Future trigger zones: a zone portal that wants to flip the player's
    ///     <see cref="VisualLayerOccupant.SetVisualLayer(int)"/> when they walk over
    ///     a "stairs" tile painted on a specific layer.
    ///
    /// Why this is intentionally separate from <see cref="VisualLayerOccupant"/>:
    /// the occupant's value is the SOURCE OF TRUTH for gameplay decisions
    /// (collision filtering, sortingOrder hints, etc.). Letting the world observe
    /// itself into the occupant would couple "I happen to walk under a Decorations
    /// sprite" with "I am now logically on the Decorations layer" — which is
    /// almost always wrong (the player walking past a tree doesn't suddenly mean
    /// they're on the tree's layer).
    /// </summary>
    public static class VisualLayerProbe
    {
        private const int LayerCount = 9; // 0..8 matches TilemapLayerSetup.TilemapLayer

        /// <summary>
        /// Fill <paramref name="layersWithTile"/> with one bool per visual layer (0..8)
        /// indicating whether that layer's tilemap has a non-empty tile at the cell
        /// containing <paramref name="worldPos"/>. Returns the count of layers that
        /// were populated (0 when <paramref name="grid"/> is null).
        ///
        /// The buffer is expected to be length 9 (allocated by the caller). Indices
        /// match <see cref="TilemapLayerSetup.TilemapLayer"/> values directly.
        /// </summary>
        public static int Sample(Vector3 worldPos, WorldGridBuilder grid, bool[] layersWithTile)
        {
            if (layersWithTile == null || layersWithTile.Length < LayerCount) return 0;
            for (int i = 0; i < LayerCount; i++) layersWithTile[i] = false;
            if (grid == null) return 0;

            int populated = 0;
            for (int i = 0; i < LayerCount; i++)
            {
                var tm = grid.GetTilemap((TilemapLayerSetup.TilemapLayer)i);
                if (tm == null) continue;
                var cell = tm.WorldToCell(worldPos);
                if (tm.GetTile(cell) != null)
                {
                    layersWithTile[i] = true;
                    populated++;
                }
            }
            return populated;
        }

        /// <summary>
        /// Return the HIGHEST VISIBLE layer index (0..8) that has a non-empty tile
        /// at <paramref name="worldPos"/>, or -1 if no visible layer does.
        ///
        /// <para>
        /// The Collision tilemap (index 2) is intentionally SKIPPED — its tiles
        /// are invisible <c>wall</c> markers used to bake the physics composite,
        /// not authored visual surfaces. Treating them as a "topmost step-able
        /// layer" would make the M1.9 auto-drop system snap the player onto
        /// walls instead of the ground below them. Callers that need the raw
        /// per-layer presence (including Collision) should use <see cref="Sample"/>
        /// instead.
        /// </para>
        ///
        /// Useful for a quick "topmost visual surface at this point" lookup
        /// without allocating a buffer.
        /// </summary>
        public static int GetTopmostLayer(Vector3 worldPos, WorldGridBuilder grid)
        {
            if (grid == null) return -1;
            for (int i = LayerCount - 1; i >= 0; i--)
            {
                if (i == (int)TilemapLayerSetup.TilemapLayer.Collision) continue;
                var tm = grid.GetTilemap((TilemapLayerSetup.TilemapLayer)i);
                if (tm == null) continue;
                var cell = tm.WorldToCell(worldPos);
                if (tm.GetTile(cell) != null) return i;
            }
            return -1;
        }
    }
}
