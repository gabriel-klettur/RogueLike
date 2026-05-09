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
            if (existing != null) return existing;

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.color = Color.white;
            tile.colliderType = Tile.ColliderType.None;
            tile.name = sprite.name;
            registry.Register(sprite.name, tile);
            return tile;
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
