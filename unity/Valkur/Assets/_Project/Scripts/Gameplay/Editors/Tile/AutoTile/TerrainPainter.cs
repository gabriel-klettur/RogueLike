using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Pure logic that paints a rectangular region with a terrain and resolves
    /// auto-tile variants for each affected cell. Mirrors <see cref="TileBrush"/>'s
    /// API shape (returns a list of <see cref="TileEdit"/>s for undo) so the rest
    /// of the tile editor doesn't care whether a stroke came from the manual brush
    /// or the auto-tile tool.
    /// </summary>
    public static class TerrainPainter
    {
        /// <summary>
        /// Stamps <paramref name="terrain"/> onto every cell of <paramref name="rect"/>
        /// in <paramref name="terrainMap"/>, then re-resolves the auto-tile variant
        /// for every cell in the rect <i>plus a one-cell ring around it</i> so cells
        /// at the rect's edge see their (possibly newly set) neighbours.
        /// </summary>
        public static (List<TileEdit> TileEdits, List<MetadataEdit> MetadataEdits) PaintRegion(
            Tilemap tilemap,
            BoundsInt rect,
            string terrain,
            TerrainCatalog catalog,
            TerrainMap terrainMap,
            Func<Vector3Int, bool> canEditCell = null)
        {
            var edits = new List<TileEdit>();
            var metadataEdits = new List<MetadataEdit>();
            if (tilemap == null || catalog == null || terrainMap == null) return (edits, metadataEdits);
            if (string.IsNullOrEmpty(terrain)) return (edits, metadataEdits);

            // 1. Stamp terrain onto every editable cell in the rect. Only cells whose
            //    terrain actually changes get a MetadataEdit — undo would otherwise
            //    have to reapply a no-op "same terrain" write.
            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (canEditCell != null && !canEditCell(cell)) continue;
                string oldTerrain = terrainMap.GetTerrain(cell);
                if (oldTerrain == terrain) continue; // no-op: nothing to undo
                metadataEdits.Add(new MetadataEdit(cell, oldTerrain, terrain, terrainMap));
                terrainMap.SetTerrain(cell, terrain);
            }

            // 2. Recompute variants for the rect + 1-cell ring. Ring cells outside
            //    the rect are only re-tiled if they already had a known terrain
            //    (otherwise we'd overwrite existing manual tiles with auto-tile
            //    variants that don't belong).
            int xMin = rect.xMin - 1;
            int yMin = rect.yMin - 1;
            int xMax = rect.xMax + 1;
            int yMax = rect.yMax + 1;
            for (int y = yMin; y < yMax; y++)
            for (int x = xMin; x < xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (canEditCell != null && !canEditCell(cell)) continue;

                bool inRect = (x >= rect.xMin && x < rect.xMax && y >= rect.yMin && y < rect.yMax);
                string cellTerrain = inRect ? terrain : terrainMap.GetTerrain(cell);
                if (string.IsNullOrEmpty(cellTerrain)) continue;

                var ruleset = catalog.FindPaintRuleset(cellTerrain);
                if (ruleset == null) continue;

                int seed = HashCell(cell);
                var sprite = TerrainTileResolver.ResolveVariantForCell(
                    ruleset, terrainMap.Cells, new Vector2Int(x, y), cellTerrain, seed);
                if (sprite == null) continue;

                var newTile = TerrainTileResolver.ResolveTile(sprite);
                var oldTile = tilemap.GetTile(cell);
                if (newTile == oldTile) continue;
                edits.Add(new TileEdit(cell, oldTile, newTile));
                tilemap.SetTile(cell, newTile);
            }

            return (edits, metadataEdits);
        }

        /// <summary>
        /// Recompute the auto-tile variant for a single cell. Used by the "recalculate
        /// region" UX path and by load-time auto-curation in Fase 5.
        /// </summary>
        public static TileEdit? Resolve(
            Tilemap tilemap,
            Vector3Int cell,
            TerrainCatalog catalog,
            TerrainMap terrainMap)
        {
            if (tilemap == null || catalog == null || terrainMap == null) return null;
            string terrain = terrainMap.GetTerrain(cell);
            if (string.IsNullOrEmpty(terrain)) return null;
            var ruleset = catalog.FindPaintRuleset(terrain);
            if (ruleset == null) return null;

            int seed = HashCell(cell);
            var sprite = TerrainTileResolver.ResolveVariantForCell(
                ruleset, terrainMap.Cells, new Vector2Int(cell.x, cell.y), terrain, seed);
            if (sprite == null) return null;

            var newTile = TerrainTileResolver.ResolveTile(sprite);
            var oldTile = tilemap.GetTile(cell);
            if (newTile == oldTile) return null;
            tilemap.SetTile(cell, newTile);
            return new TileEdit(cell, oldTile, newTile);
        }

        /// <summary>
        /// Convenience wrapper around <see cref="PaintRegion"/> for a single cell —
        /// stamps <paramref name="terrain"/> onto <paramref name="cell"/> and
        /// re-resolves the cell plus its full 8-neighbour ring. The ring must include
        /// the 4 diagonal neighbours (not just N/E/S/W): a Corner16 tile's signature
        /// reads the 2x2 corner block shared with each of its 8 neighbours, so
        /// painting one cell can change a diagonal neighbour's corner reading even
        /// though it never changes that neighbour's own cardinal mask.
        /// </summary>
        public static (List<TileEdit> TileEdits, List<MetadataEdit> MetadataEdits) PaintCell(
            Tilemap tilemap,
            Vector3Int cell,
            string terrain,
            TerrainCatalog catalog,
            TerrainMap terrainMap,
            Func<Vector3Int, bool> canEditCell = null)
        {
            var rect = new BoundsInt(cell.x, cell.y, 0, 1, 1, 1);
            return PaintRegion(tilemap, rect, terrain, catalog, terrainMap, canEditCell);
        }

        /// <summary>Stable per-cell hash so the same cell always picks the same variant
        /// when a slot has multiple sprites.</summary>
        private static int HashCell(Vector3Int c)
        {
            return unchecked(c.x * 73856093 ^ c.y * 19349663);
        }
    }
}
