using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Bridges the auto-tile <see cref="RulesetSolver"/> (which returns a
    /// <see cref="Sprite"/>) with the visual <see cref="Tilemap"/> (which needs a
    /// <see cref="TileBase"/>). Looks up the corresponding tile in the runtime
    /// <see cref="TileRegistry"/> by sprite name; if not present (e.g. the sprite
    /// belongs to a folder the catalog hasn't loaded yet), wraps it in a fresh
    /// <see cref="Tile"/> instance and registers it for future lookups.
    /// </summary>
    public static class TerrainTileResolver
    {
        public static TileBase ResolveTile(Sprite sprite)
        {
            if (sprite == null) return null;

            var registry = TileRegistry.Instance;
            var existing = registry.GetTile(sprite.name);
            if (IsCachedTileStillValid(existing, sprite))
                return existing;

            // Cache miss OR cached entry's sprite was destroyed (the latter can
            // happen across EditMode test runs and after Domain Reload while
            // Sprites are GC'd). Evict and rebuild.
            if (existing != null) registry.Unregister(sprite.name);

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.color = Color.white;
            tile.colliderType = Tile.ColliderType.None;
            tile.name = sprite.name;
            registry.Register(sprite.name, tile);
            return tile;
        }

        /// <summary>
        /// Returns true if <paramref name="cached"/> is non-null AND still
        /// references a live sprite. Unity's overloaded <c>==</c> treats
        /// destroyed UnityEngine.Object instances as null, so a plain
        /// reference check is sufficient — the explicit cast just makes the
        /// intent obvious to the reader.
        /// </summary>
        private static bool IsCachedTileStillValid(TileBase cached, Sprite expectedSprite)
        {
            if (cached == null) return false;
            if (cached is Tile t)
            {
                // Sprite identity matters: if the cache holds a Tile whose
                // sprite was destroyed (== null under Unity overload), or that
                // points at a different live sprite, treat as stale.
                if (t.sprite == null) return false;
                if (t.sprite != expectedSprite && t.sprite.name != expectedSprite.name) return false;
            }
            return true;
        }

        /// <summary>
        /// Resolve the auto-tile variant for a given terrain at the given cell mask.
        /// Returns null if the terrain has no base ruleset, or the slot has no sprite assigned.
        /// </summary>
        public static TileBase ResolveTerrainVariant(TerrainCatalog catalog, string terrain, byte cardinalMask, int hashSeed)
        {
            if (catalog == null || string.IsNullOrEmpty(terrain)) return null;
            var ruleset = catalog.FindBaseRuleset(terrain);
            if (ruleset == null) return null;
            var sprite = RulesetSolver.Resolve(ruleset, cardinalMask, hashSeed);
            return ResolveTile(sprite);
        }
    }
}
