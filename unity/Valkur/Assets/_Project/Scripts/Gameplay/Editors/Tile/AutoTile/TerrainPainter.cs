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
        /// <param name="ruleset">
        /// The pack to resolve variants against, when the caller already knows it.
        /// Null falls back to <c>catalog.FindPaintRuleset(cellTerrain)</c>, which resolves
        /// a terrain NAME to exactly ONE ruleset — so a second pack claiming the same
        /// primary is unreachable, silently. Shipped today: <c>grass_rock</c> claims
        /// 'grass' and loses it to <c>grass_dirt</c>, and <c>sand_rock</c> loses 'sand'
        /// to <c>sand_grass</c>. An author who picked a tile has already answered the
        /// question the name lookup is guessing at, so the auto brush passes the pack
        /// it read off that tile and paints what was clicked.
        /// </param>
        public static (List<TileEdit> TileEdits, List<MetadataEdit> MetadataEdits) PaintRegion(
            Tilemap tilemap,
            BoundsInt rect,
            string terrain,
            TerrainCatalog catalog,
            TerrainMap terrainMap,
            Func<Vector3Int, bool> canEditCell = null,
            TilesetRuleset ruleset = null,
            AutoTileSheetFilter filter = null)
        {
            var edits = new List<TileEdit>();
            var metadataEdits = new List<MetadataEdit>();
            if (tilemap == null || catalog == null || terrainMap == null) return (edits, metadataEdits);
            if (string.IsNullOrEmpty(terrain)) return (edits, metadataEdits);

            // WHICH SPACE the terrain is stamped in follows the PACK'S MODEL, because each
            // model's art is authored in a different one and a single global choice is
            // wrong for one of them. Corner16 art is authored per grid POINT — a tile's
            // four corners are four vertices — so its terrain layer sits half a cell off
            // the render layer (the dual grid), which is the only arrangement that can
            // draw a boundary tile that is half one terrain and half the other. Blob16 art
            // is authored per CELL and asks which cardinal neighbours share the cell's own
            // terrain, so it keeps cell keys and behaves exactly as before. No pack at all
            // also keeps cell keys: the stroke still records the author's intent, and that
            // intent is about cells.
            //
            // The two key spaces share one dictionary and could collide where a Corner16
            // and a Blob16 pack were painted over each other. No shipped data does that —
            // measured, all three Blob16 rulesets in the catalog carry zero slots and can
            // resolve nothing — and a second dictionary is two things that eventually
            // disagree about what a coordinate means.
            var pack = ruleset ?? catalog.FindPaintRuleset(terrain);
            bool vertexSpace = pack != null && pack.Model == AutoTileModel.Corner16;

            // 1. Stamp. A w x h rect is bounded by (w+1) x (h+1) VERTICES — the corners on
            //    its far edges belong to it as much as those on its near edges, and leaving
            //    them out makes a painted region's own outer row read as a boundary. In cell
            //    space it is the w x h cells themselves. Only an entry whose terrain actually
            //    changes gets a MetadataEdit; undo would otherwise reapply a no-op write.
            int stampXMax = vertexSpace ? rect.xMax : rect.xMax - 1;
            int stampYMax = vertexSpace ? rect.yMax : rect.yMax - 1;
            for (int y = rect.yMin; y <= stampYMax; y++)
            for (int x = rect.xMin; x <= stampXMax; x++)
            {
                // The guard is authored per CELL, so a vertex is gated by the cell it
                // anchors. Keeps "this area is not editable" meaning one thing.
                var anchor = new Vector3Int(x, y, 0);
                if (canEditCell != null && !canEditCell(anchor)) continue;

                var key = new Vector2Int(x, y);
                string oldTerrain = terrainMap.GetTerrain(key);
                if (oldTerrain == terrain) continue;
                metadataEdits.Add(new MetadataEdit(anchor, oldTerrain, terrain, terrainMap));
                terrainMap.SetTerrain(key, terrain);
            }

            // The terrain is stamped even when no pack resolves: the map records the author
            // intent and stays undoable, and a later import that gives the terrain a ruleset
            // re-tiles it. Only the VISUAL half needs a pack.
            if (pack == null) return (edits, metadataEdits);

            // 2. Re-resolve the rect plus a one-cell ring.
            var corners = new Vector2Int[4];
            for (int y = rect.yMin - 1; y <= rect.yMax; y++)
            for (int x = rect.xMin - 1; x <= rect.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (canEditCell != null && !canEditCell(cell)) continue;

                string cellTerrain;
                if (vertexSpace)
                {
                    // A cell has no terrain of its own here — its four corners do. It is
                    // re-tiled only when one of them carries a terrain of THIS pack, or the
                    // ring would repaint scenery that merely sits next to the stroke.
                    BitmaskCalculator.CornersOf(new Vector2Int(x, y), corners);
                    if (!TouchesPack(terrainMap, corners, pack)) continue;
                    cellTerrain = terrain;
                }
                else
                {
                    bool inRect = (x >= rect.xMin && x < rect.xMax && y >= rect.yMin && y < rect.yMax);
                    cellTerrain = inRect ? terrain : terrainMap.GetTerrain(cell);
                    if (string.IsNullOrEmpty(cellTerrain)) continue;
                    if (!inRect && ruleset == null && catalog.FindPaintRuleset(cellTerrain) != pack) continue;
                }

                int seed = HashCell(cell);
                var sprite = TerrainTileResolver.ResolveVariantForCell(
                    pack, terrainMap.Cells, new Vector2Int(x, y), cellTerrain, seed, filter);
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
            TerrainMap terrainMap,
            TileCatalog tileCatalog = null)
        {
            if (tilemap == null || catalog == null || terrainMap == null) return null;

            // THE PACK COMES FROM THE TILE THAT IS THERE, never from the terrain name.
            //
            // This used to ask FindPaintRuleset(terrain), which resolves a NAME to exactly one
            // ruleset — and two packs share 'grass'. So every cell an author had painted with
            // grass_rock was re-resolved against grass_dirt and came back as different art, on
            // every single open of the editor. Measured on the shipped overrides: zone_100_50
            // holds 1,377 grass/rock terrain cells, and Forest and Lobby another 2,867 between
            // them. The tiles loaded correctly and then changed the moment the editor opened,
            // which is exactly the shape reported.
            //
            // The placed tile does not have that ambiguity: the slot that holds its sprite is
            // one pack's own statement about it.
            var placed = (tilemap.GetTile(cell) as Tile)?.sprite;
            if (placed == null) return null;

            var found = AutoBrushPackLookup.FindPackAndSlot(catalog, placed, tileCatalog);
            var ruleset = found.Ruleset;
            if (ruleset == null) return null;   // not auto-tile art; leave it exactly as it is
            if (string.IsNullOrEmpty(ruleset.TerrainSecondary)) return null;

            // Every corner must be known, or the mask invents a boundary out of an absent
            // vertex — which reads as primary and cuts an edge through solid ground.
            var corners = new Vector2Int[4];
            BitmaskCalculator.CornersOf(new Vector2Int(cell.x, cell.y), corners);
            if (!AllCornersKnown(terrainMap, corners, ruleset)) return null;

            // A cell whose SLOT is already right is left alone. Re-resolving it would hand back
            // some other variant of that same slot — grass_dirt now carries six sheets, so an
            // open would reshuffle the art of every correctly-painted cell on the map.
            byte want = BitmaskCalculator.CornerMask(
                terrainMap.Cells, new Vector2Int(cell.x, cell.y), ruleset.TerrainSecondary);
            if (found.SlotMask == want) return null;

            // Only now, on a cell that genuinely needs curing, is the sheet worth resolving:
            // keep the art in the sheet the author painted from.
            var filter = AutoTileSheetFilter.ForSprite(tileCatalog, placed, ruleset);

            int seed = HashCell(cell);
            var sprite = TerrainTileResolver.ResolveVariantForCell(
                ruleset, terrainMap.Cells, new Vector2Int(cell.x, cell.y),
                ruleset.TerrainSecondary, seed, filter);
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


        /// <summary>
        /// Reads the tiles ALREADY on the map inside <paramref name="rect"/> and joins them:
        /// recovers what terrain each of their corners stands for, writes that into the vertex
        /// terrain layer, and re-resolves the neighbourhood so the boundary between them is
        /// drawn instead of being a hard cut.
        ///
        /// <para><b>Why this is not PaintRegion.</b> PaintRegion STAMPS one terrain over an
        /// area — it is how you lay ground down. This is the other verb an auto-tiler needs:
        /// the author has already placed the tiles they want and is asking for the seam between
        /// them to be resolved. Stamping cannot do that, because it replaces both sides with
        /// the terrain being painted.</para>
        ///
        /// <para><b>The recovery is exact, not a guess.</b> A Corner16 slot IS the pack's own
        /// statement about which of a tile's four corners are the secondary terrain — mask bit
        /// set means that corner is secondary — so a placed tile tells you all four of its
        /// corner terrains with no inference. Measured on the shipped water pack, all 16 slots
        /// round-trip from the placed sprite, duplicate sheet cells included (those resolve
        /// through the manifest's uniqueId).</para>
        ///
        /// <para><b>Ties go to the author.</b> A vertex is shared by four cells and they can
        /// disagree — along a hard cut between two solid areas, two cells vote primary and two
        /// vote secondary at every vertex of the seam. The tie is broken by
        /// <paramref name="preferredTerrain"/>, the terrain of the tile the author has selected,
        /// so which way the new boundary leans is a choice they make by picking a tile rather
        /// than something the seam decides for them. A vertex no tile of this pack touches is
        /// left alone: inventing terrain there would repaint scenery the author never aimed at.</para>
        /// </summary>
        public static (List<TileEdit> TileEdits, List<MetadataEdit> MetadataEdits) ConnectRegion(
            Tilemap tilemap,
            BoundsInt rect,
            TilesetRuleset pack,
            TileCatalog tileCatalog,
            TerrainMap terrainMap,
            string preferredTerrain,
            Func<Vector3Int, bool> canEditCell = null,
            AutoTileSheetFilter filter = null)
        {
            var edits = new List<TileEdit>();
            var metadataEdits = new List<MetadataEdit>();
            if (tilemap == null || pack == null || terrainMap == null) return (edits, metadataEdits);
            if (string.IsNullOrEmpty(pack.TerrainSecondary)) return (edits, metadataEdits);

            // 1. Decide each vertex from the tiles that touch it — over the rect AND one ring
            //    beyond it. The ring is not optional: step 2 re-resolves the cells around the
            //    footprint too, and an UNDECIDED corner reads as the primary terrain, so a cell
            //    sitting deep inside a solid secondary area would be handed a boundary it has no
            //    business having. Measured before the ring was added: a stroke down a seam turned
            //    a column two cells INSIDE the deep water into a half-and-half tile.
            for (int vy = rect.yMin - 1; vy <= rect.yMax + 1; vy++)
            for (int vx = rect.xMin - 1; vx <= rect.xMax + 1; vx++)
            {
                var anchor = new Vector3Int(vx, vy, 0);
                if (canEditCell != null && !canEditCell(anchor)) continue;

                int secondary = 0, primary = 0;
                CountCornerVotes(tilemap, pack, tileCatalog, vx, vy, ref secondary, ref primary);
                if (secondary == 0 && primary == 0) continue; // no tile of this pack touches it

                string decided = secondary > primary ? pack.TerrainSecondary
                               : primary > secondary ? pack.TerrainPrimary
                               : preferredTerrain;
                if (string.IsNullOrEmpty(decided)) continue;

                var vertex = new Vector2Int(vx, vy);
                string old = terrainMap.GetTerrain(vertex);
                if (old == decided) continue;
                metadataEdits.Add(new MetadataEdit(anchor, old, decided, terrainMap));
                terrainMap.SetTerrain(vertex, decided);
            }

            // 2. Re-resolve every cell those vertices are a corner of.
            var corners = new Vector2Int[4];
            for (int y = rect.yMin - 1; y <= rect.yMax; y++)
            for (int x = rect.xMin - 1; x <= rect.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (canEditCell != null && !canEditCell(cell)) continue;

                BitmaskCalculator.CornersOf(new Vector2Int(x, y), corners);
                // EVERY corner must be decided, not merely one of them. A cell with an unknown
                // corner cannot be resolved without inventing a terrain for it, and the invented
                // answer is always "primary" — which draws an edge through solid ground.
                if (!AllCornersKnown(terrainMap, corners, pack)) continue;

                // A cell whose SLOT is already right is left exactly as it is. Re-resolving it
                // would hand back some other variant of that same slot — the solver picks by cell
                // hash and the sheet filter narrows the pool — so a stroke over settled ground
                // would reshuffle art that was already correct. AUTO's job is to fix the slot,
                // not to re-roll the variant.
                byte want = BitmaskCalculator.CornerMask(
                    terrainMap.Cells, new Vector2Int(x, y), pack.TerrainSecondary);
                var placed = (tilemap.GetTile(cell) as Tile)?.sprite;
                if (placed != null)
                {
                    var already = AutoBrushPackLookup.FindPackAndSlot(CatalogOf(pack), placed, tileCatalog);
                    if (already.Ruleset == pack && already.SlotMask == want) continue;
                }

                var sprite = TerrainTileResolver.ResolveVariantForCell(
                    pack, terrainMap.Cells, new Vector2Int(x, y), pack.TerrainSecondary,
                    HashCell(cell), filter);
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
        /// True only when all four corners carry a terrain of <paramref name="pack"/>. Anything
        /// less and the cell cannot be resolved from what is actually known.
        /// </summary>
        private static bool AllCornersKnown(TerrainMap terrainMap, Vector2Int[] corners, TilesetRuleset pack)
        {
            for (int i = 0; i < corners.Length; i++)
            {
                string t = terrainMap.GetTerrain(corners[i]);
                if (t != pack.TerrainPrimary && t != pack.TerrainSecondary) return false;
            }
            return true;
        }

        /// <summary>
        /// Adds the vote of each of the four cells that meet at vertex (vx, vy). A cell holds
        /// that vertex in a different corner depending on which side of it the cell sits, and
        /// the slot's bit for THAT corner is the vote.
        /// </summary>
        private static void CountCornerVotes(Tilemap tilemap, TilesetRuleset pack, TileCatalog tileCatalog,
                                             int vx, int vy, ref int secondary, ref int primary)
        {
            // cell offset -> which corner of that cell this vertex is
            VoteFrom(tilemap, pack, tileCatalog, vx,     vy,     BitmaskCalculator.BitCornerSW, ref secondary, ref primary);
            VoteFrom(tilemap, pack, tileCatalog, vx - 1, vy,     BitmaskCalculator.BitCornerSE, ref secondary, ref primary);
            VoteFrom(tilemap, pack, tileCatalog, vx,     vy - 1, BitmaskCalculator.BitCornerNW, ref secondary, ref primary);
            VoteFrom(tilemap, pack, tileCatalog, vx - 1, vy - 1, BitmaskCalculator.BitCornerNE, ref secondary, ref primary);
        }

        private static void VoteFrom(Tilemap tilemap, TilesetRuleset pack, TileCatalog tileCatalog,
                                     int cx, int cy, byte cornerBit, ref int secondary, ref int primary)
        {
            var sprite = (tilemap.GetTile(new Vector3Int(cx, cy, 0)) as Tile)?.sprite;
            if (sprite == null) return;

            var found = AutoBrushPackLookup.FindPackAndSlot(CatalogOf(pack), sprite, tileCatalog);
            if (found.Ruleset != pack) return; // a tile of some other pack says nothing about this one

            if ((found.SlotMask & cornerBit) != 0) secondary++;
            else primary++;
        }

        /// <summary>The catalog the pack lives in. The lookup needs it to reach the other
        /// rulesets and reject a tile that belongs to a different pack. Cached by
        /// TerrainCatalogLoader, so this is a field read rather than a load.</summary>
        private static TerrainCatalog CatalogOf(TilesetRuleset pack) => TerrainCatalogLoader.Load();

        /// <summary>
        /// True when any of <paramref name="corners"/> carries either of
        /// <paramref name="pack"/>'s two terrains. This is what keeps a stroke's one-cell
        /// resolution ring from repainting scenery that only happens to be adjacent.
        /// </summary>
        private static bool TouchesPack(TerrainMap terrainMap, Vector2Int[] corners, TilesetRuleset pack)
        {
            for (int i = 0; i < corners.Length; i++)
            {
                string t = terrainMap.GetTerrain(corners[i]);
                if (string.IsNullOrEmpty(t)) continue;
                if (t == pack.TerrainPrimary || t == pack.TerrainSecondary) return true;
            }
            return false;
        }

        /// <summary>Stable per-cell hash so the same cell always picks the same variant
        /// when a slot has multiple sprites.</summary>
        private static int HashCell(Vector3Int c)
        {
            return unchecked(c.x * 73856093 ^ c.y * 19349663);
        }
    }
}
