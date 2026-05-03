using UnityEngine.Tilemaps;
using Valkur.Data.Chunks;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Gameplay.World.Chunks
{
    /// <summary>
    /// Production bridge between <see cref="TileRegistry"/> (the runtime
    /// catalog of named <see cref="TileBase"/> assets used by every other
    /// editor and overlay) and <see cref="IChunkTileResolver"/>
    /// (the painter's view of "id -> asset"). Phase 2.6 wires
    /// <see cref="ProceduralWorldFactory"/> into the actual scene by
    /// pointing chunk painting at the same TileBase instances the rest
    /// of the game already uses.
    ///
    /// EditMode tests still construct <see cref="TileIdTableResolver"/>
    /// directly with a fixture lambda — this file is the production-only
    /// shortcut so callers don't have to reach into TileRegistry every
    /// time they wire a streamed world.
    /// </summary>
    public static class TileRegistryNameResolver
    {
        /// <summary>
        /// Build a resolver that translates ids through <paramref name="idTable"/>
        /// and names through the live <see cref="TileRegistry"/> singleton.
        /// </summary>
        public static IChunkTileResolver Build(ITileIdTable idTable)
        {
            return new TileIdTableResolver(idTable, NameLookup);
        }

        private static TileBase NameLookup(string name)
        {
            var registry = TileRegistry.Instance;
            return registry.IsLoaded ? registry.GetTile(name) : null;
        }
    }
}
