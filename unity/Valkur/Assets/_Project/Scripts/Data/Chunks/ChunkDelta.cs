using System;
using System.Collections.Generic;
using Valkur.Core.Coordinates;

namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Diff of player-driven edits applied on top of a procedural
    /// <see cref="ChunkData"/> baseline. The persistence story is:
    ///
    ///   - The baseline is regenerated on demand by the biome — it is a
    ///     pure function of (worldSeed, coord, biome.Version) so it
    ///     never needs to be serialised.
    ///   - Only the diff travels: through disk (one delta blob per chunk)
    ///     and, in Phase 4, through the network (replicated to clients
    ///     on chunk enter-of-interest).
    ///
    /// A virgin chunk that the player has never touched is represented by
    /// an empty delta — zero bytes after MessagePack/LZ4 in Phase 2.5.
    ///
    /// Versioning: when the biome's Version field bumps, every existing
    /// delta keyed against the previous version is suspect. Phase 2 does
    /// the conservative thing — discard the delta and warn — but a Phase
    /// 3 migration tool can reconcile by replaying edits onto the new
    /// baseline offline.
    /// </summary>
    [Serializable]
    public sealed class ChunkDelta
    {
        /// <summary>Address of the chunk this delta applies to.</summary>
        public ChunkCoord Coord;

        /// <summary>Biome that produced the baseline this delta diffs
        /// against. Pairs with <see cref="BiomeVersion"/> to detect a
        /// stale baseline after a generation-rules change.</summary>
        public string BiomeId;

        /// <summary>Version of the biome at the time the delta was
        /// captured. Compare to <c>biome.Version</c> at load time;
        /// mismatch -> baseline shifted -> this delta may need
        /// migration.</summary>
        public int BiomeVersion;

        /// <summary>Per-tile edits relative to the baseline. Stored as a
        /// flat list so the wire format is dense — a typical delta has
        /// 1-100 entries, far cheaper than the full chunk.</summary>
        public List<TileEdit> Tiles = new List<TileEdit>();

        public ChunkDelta() { }

        public ChunkDelta(ChunkCoord coord, string biomeId, int biomeVersion)
        {
            Coord = coord;
            BiomeId = biomeId ?? string.Empty;
            BiomeVersion = biomeVersion;
        }

        /// <summary>True iff there are no edits — repository
        /// implementations skip persisting empty deltas.</summary>
        public bool IsEmpty => Tiles == null || Tiles.Count == 0;

        /// <summary>Apply this delta's edits onto a baseline ChunkData
        /// in-place. Out-of-bounds edits are dropped with a warning.</summary>
        public void ApplyTo(ChunkData baseline, Action<string> warn = null)
        {
            if (baseline == null || Tiles == null) return;
            for (int i = 0; i < Tiles.Count; i++)
            {
                var e = Tiles[i];
                if (e.Layer < 0 || e.Layer >= baseline.Layers.Length)
                {
                    warn?.Invoke($"Delta at {Coord}: layer {e.Layer} out of range, skipped.");
                    continue;
                }
                if (e.LocalX < 0 || e.LocalX >= baseline.Size ||
                    e.LocalY < 0 || e.LocalY >= baseline.Size)
                {
                    warn?.Invoke($"Delta at {Coord}: ({e.LocalX},{e.LocalY}) out of range, skipped.");
                    continue;
                }
                baseline.Set(e.Layer, e.LocalX, e.LocalY, e.NewTileId);
            }
        }

        /// <summary>Append (or overwrite) a single edit. Multiple edits
        /// to the same cell collapse to the most recent one — keeps the
        /// delta size O(distinct edited cells), not O(edit ops).</summary>
        public void Add(TileEdit edit)
        {
            for (int i = 0; i < Tiles.Count; i++)
            {
                var existing = Tiles[i];
                if (existing.Layer == edit.Layer &&
                    existing.LocalX == edit.LocalX &&
                    existing.LocalY == edit.LocalY)
                {
                    Tiles[i] = edit;
                    return;
                }
            }
            Tiles.Add(edit);
        }

        /// <summary>Compute the diff between a procedural baseline and a
        /// modified copy. Iterates every cell and records the cells where
        /// the modified buffer differs. Used by save flows: regenerate
        /// the baseline, compare, persist only what changed.</summary>
        public static ChunkDelta DiffFrom(ChunkData baseline, ChunkData modified,
                                          string biomeId, int biomeVersion)
        {
            if (baseline == null || modified == null) return null;
            if (baseline.Size != modified.Size)
                throw new ArgumentException("Baseline and modified chunks must share Size.");
            var delta = new ChunkDelta(modified.Coord, biomeId, biomeVersion);
            int layers = System.Math.Min(baseline.Layers.Length, modified.Layers.Length);
            for (int l = 0; l < layers; l++)
            {
                var a = baseline.Layers[l];
                var b = modified.Layers[l];
                if (a == null || b == null) continue;
                for (int y = 0; y < modified.Size; y++)
                for (int x = 0; x < modified.Size; x++)
                {
                    int idx = y * modified.Size + x;
                    if (a[idx] != b[idx])
                        delta.Tiles.Add(new TileEdit(l, x, y, b[idx]));
                }
            }
            return delta;
        }
    }

    /// <summary>One tile mutation inside a <see cref="ChunkDelta"/>.</summary>
    [Serializable]
    public struct TileEdit
    {
        public int    Layer;
        public int    LocalX;
        public int    LocalY;
        public ushort NewTileId;

        public TileEdit(int layer, int localX, int localY, ushort newTileId)
        {
            Layer = layer; LocalX = localX; LocalY = localY; NewTileId = newTileId;
        }
    }
}
