using System.Collections.Generic;
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
        /// Resolve the auto-tile variant for <paramref name="terrain"/> at
        /// <paramref name="cell"/>. Looks up the base ruleset for that terrain, then
        /// dispatches to <see cref="ResolveVariantForCell"/> so Corner16 rulesets
        /// resolve on the corner mask and Blob16 rulesets resolve on the cardinal
        /// mask. Returns null if the terrain has no base ruleset, or the resolved
        /// slot has no sprite assigned.
        /// </summary>
        public static TileBase ResolveTerrainVariant(
            TerrainCatalog catalog,
            string terrain,
            IReadOnlyDictionary<Vector2Int, string> grid,
            Vector2Int cell,
            int hashSeed)
        {
            if (catalog == null || string.IsNullOrEmpty(terrain)) return null;
            var ruleset = catalog.FindPaintRuleset(terrain);
            if (ruleset == null) return null;
            var sprite = ResolveVariantForCell(ruleset, grid, cell, terrain, hashSeed);
            return ResolveTile(sprite);
        }

        /// <summary>
        /// Computes the model-appropriate auto-tile mask for <paramref name="cell"/>
        /// against <paramref name="ruleset"/> and resolves it to a sprite. The model
        /// choice always comes from <see cref="TilesetRuleset.Model"/> — never a
        /// global flag — so base (Blob16) and transition (Corner16) rulesets coexist
        /// in the same catalog:
        ///
        /// <list type="bullet">
        /// <item>
        /// <see cref="AutoTileModel.Corner16"/> tests each corner's 2x2 block majority
        /// against <see cref="TilesetRuleset.TerrainSecondary"/> via
        /// <see cref="BitmaskCalculator.CornerMask"/>. Returns null if the ruleset has
        /// no secondary terrain configured (a Corner16 ruleset is always a
        /// two-material transition — without a secondary there is nothing to test
        /// corners against).
        /// </item>
        /// <item>
        /// Every other model (Blob16 today) falls back to the cardinal same-terrain
        /// mask against <paramref name="terrain"/> via <see cref="BitmaskCalculator.CardinalMask"/>.
        /// </item>
        /// </list>
        ///
        /// Returns null if the resolved slot has no sprite variant assigned — the
        /// caller (<see cref="TerrainPainter"/>) treats that as "leave the cell as-is",
        /// same as an unassigned Blob16 slot does today.
        /// </summary>
        public static Sprite ResolveVariantForCell(
            TilesetRuleset ruleset,
            IReadOnlyDictionary<Vector2Int, string> grid,
            Vector2Int cell,
            string terrain,
            int hashSeed)
        {
            if (ruleset == null) return null;

            if (ruleset.Model == AutoTileModel.Corner16)
            {
                if (string.IsNullOrEmpty(ruleset.TerrainSecondary)) return null;
                byte cornerMask = BitmaskCalculator.CornerMask(grid, cell, ruleset.TerrainSecondary);
                return RulesetSolver.ResolveCorner(ruleset, cornerMask, hashSeed);
            }

            byte cardinalMask = BitmaskCalculator.CardinalMask(grid, cell, terrain);
            return RulesetSolver.Resolve(ruleset, cardinalMask, hashSeed);
        }
    }
}
