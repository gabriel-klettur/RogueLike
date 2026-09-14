using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// Rivers: paths of tiles that start in the highlands and walk downhill to the sea.
    ///
    /// <para><b>Why rivers are DATA and not a per-point rule.</b> Everything in
    /// <see cref="WorldClimate"/> answers one point without looking at its neighbours; a river
    /// cannot, because whether a tile is river depends on where water UPHILL of it went. So the
    /// paths are computed once, stored, and both the preview and the build rasterise the same
    /// list — the preview at its cell resolution, the build per tile. Computing them in tile
    /// space rather than preview space is what keeps that honest: the cost is the rivers'
    /// length, not the map's area, so the preview can afford the real thing.</para>
    ///
    /// <para><b>Edge-connected, never diagonal.</b> A diagonal step leaves two tiles
    /// touching only at a corner, which the Corner16 ground reads as two separate pools and the
    /// collision grid reads as a gap a player can walk through.</para>
    /// </summary>
    public static class WorldRivers
    {
        private const int RiverSalt = 5;

        /// <summary>Source candidates tried per requested river. Most random points are not highland.</summary>
        private const int AttemptsPerRiver = 30;

        /// <summary>A source must sit at least this far (tiles) from every other source.</summary>
        private const int MinSourceSpacing = 24;

        /// <summary>Where between sea level (0) and the mountain line (1) a source may start.</summary>
        private const float SourceHeight = 0.5f;

        /// <summary>A river shorter than this is a puddle, and is dropped.</summary>
        public const int MinLength = 12;

        /// <summary>How far a river may climb out of a pit before it is abandoned, in steps.</summary>
        private const int MaxUphillSteps = 60;

        /// <summary>The widest a river swings off its downhill heading, in radians (about 77 degrees).</summary>
        private const float MaxSwingRadians = 1.35f;

        /// <summary>How fast the swing changes per tile travelled. Lower = longer, lazier bends.</summary>
        private const float MeanderFrequency = 0.08f;

        /// <summary>How far the heading turns towards downhill each step (the rest is kept), so one bump cannot kink the river.</summary>
        private const float HeadingInertia = 0.25f;

        /// <summary>Distance the curve advances per substep, in tiles. Under one so no tile is skipped.</summary>
        private const float SubstepTiles = 0.5f;

        /// <summary>Half the distance the downhill gradient is measured across.</summary>
        private const float GradientSpan = 2f;

        /// <summary>How many times a river may re-enter its own bed before it is judged trapped.</summary>
        private const int MaxStalledTiles = 40;

        /// <summary>
        /// Every river of this world, in the order they were traced. A later river that reaches an
        /// earlier one joins it and stops, the way a tributary does.
        /// </summary>
        public static List<WorldRiver> Generate(WorldClimate climate)
        {
            var rivers = new List<WorldRiver>();
            var s = climate.Settings;
            if (s.riverCount <= 0 || !climate.IsEnabled(WorldBiome.River)) return rivers;

            var rng = new System.Random(WorldSeed.Derive(s.seed, RiverSalt));
            var meander = new FractalNoise2D(WorldSeed.Derive(s.seed, RiverSalt + 1), 2);
            var taken = new HashSet<Vector2Int>();
            var sources = new List<Vector2Int>();
            float sourceFloor = Mathf.Lerp(s.seaLevel, s.mountainLevel, SourceHeight);

            int attempts = s.riverCount * AttemptsPerRiver;
            for (int a = 0; a < attempts && rivers.Count < s.riverCount; a++)
            {
                var source = new Vector2Int(rng.Next(1, s.widthTiles - 1), rng.Next(1, s.heightTiles - 1));
                var sample = climate.Sample(source.x + 0.5f, source.y + 0.5f);
                if (sample.Elevation < sourceFloor) continue;
                if (IsWater(climate.Classify(sample))) continue;
                if (TooClose(source, sources)) continue;

                var path = Trace(climate, meander, source, taken);
                if (path == null) continue;

                sources.Add(source);
                for (int i = 0; i < path.Count; i++) taken.Add(path[i]);
                rivers.Add(new WorldRiver(path));
            }

            return rivers;
        }

        private static List<Vector2Int> Trace(WorldClimate climate, FractalNoise2D meander,
                                              Vector2Int source, HashSet<Vector2Int> taken)
        {
            var s = climate.Settings;
            var path = new List<Vector2Int> { source };
            var visited = new HashSet<Vector2Int> { source };

            // The river is traced as a CURVE in continuous space and rasterised to tiles as it goes.
            // Choosing among the four neighbours directly draws ruler lines: whenever the wanted
            // direction is within 45 degrees of an axis the same step wins every time — measured on
            // the first two versions, straight runs of 52 to 70 tiles. A curve crossing the grid at
            // any angle produces the staircase a river on a tile map actually looks like.
            var pos = new Vector2(source.x + 0.5f, source.y + 0.5f);
            var last = source;
            float lastHeight = climate.Sample(pos.x, pos.y).Elevation;
            Vector2 heading = Downhill(climate, pos);
            if (heading.sqrMagnitude < 1e-8f) heading = Vector2.down;
            heading.Normalize();

            float phase = (source.x * 0.37f + source.y * 0.61f) % 97f;
            float travelled = 0f;
            int uphill = 0, stalled = 0;
            int maxSubsteps = (s.widthTiles + s.heightTiles) * 4;

            for (int step = 0; step < maxSubsteps; step++)
            {
                var down = Downhill(climate, pos);
                if (down.sqrMagnitude > 1e-10f) heading = Vector2.Lerp(heading, down.normalized, HeadingInertia).normalized;

                float swing = (meander.Sample(travelled * MeanderFrequency + phase, phase) - 0.5f) * 2f * MaxSwingRadians;
                float cos = Mathf.Cos(swing), sin = Mathf.Sin(swing);
                var dir = new Vector2(heading.x * cos - heading.y * sin, heading.x * sin + heading.y * cos);

                pos += dir * SubstepTiles;
                travelled += SubstepTiles;

                var tile = new Vector2Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y));
                if (tile == last) continue;
                if (tile.x < 0 || tile.y < 0 || tile.x >= s.widthTiles || tile.y >= s.heightTiles) return null;

                // A diagonal jump would join two tiles only at a corner; walk it through the lower
                // of the two tiles that share an edge with both.
                if (tile.x != last.x && tile.y != last.y)
                {
                    var viaX = new Vector2Int(tile.x, last.y);
                    var viaY = new Vector2Int(last.x, tile.y);
                    var via = climate.Sample(viaX.x + 0.5f, viaX.y + 0.5f).Elevation
                              <= climate.Sample(viaY.x + 0.5f, viaY.y + 0.5f).Elevation ? viaX : viaY;
                    int endVia = Enter(climate, via, path, visited, taken, ref lastHeight, ref uphill, ref stalled);
                    if (endVia != 0) return endVia > 0 && path.Count >= MinLength ? path : null;
                }

                int end = Enter(climate, tile, path, visited, taken, ref lastHeight, ref uphill, ref stalled);
                if (end != 0) return end > 0 && path.Count >= MinLength ? path : null;
                last = tile;
            }

            return null;
        }

        /// <summary>1 = reached water or another river, -1 = abandon, 0 = keep going.</summary>
        private static int Enter(WorldClimate climate, Vector2Int tile, List<Vector2Int> path,
                                 HashSet<Vector2Int> visited, HashSet<Vector2Int> taken,
                                 ref float lastHeight, ref int uphill, ref int stalled)
        {
            if (visited.Contains(tile))
            {
                // Circling back over its own bed: a basin with no way out.
                return ++stalled > MaxStalledTiles ? -1 : 0;
            }

            if (taken.Contains(tile) || IsWater(climate.BiomeAt(tile.x + 0.5f, tile.y + 0.5f)))
            {
                path.Add(tile);
                return 1;
            }

            float h = climate.Sample(tile.x + 0.5f, tile.y + 0.5f).Elevation;
            if (h > lastHeight && ++uphill > MaxUphillSteps) return -1;

            visited.Add(tile);
            path.Add(tile);
            lastHeight = h;
            return 0;
        }

        /// <summary>The downhill direction at a point, by central differences two tiles apart.</summary>
        private static Vector2 Downhill(WorldClimate climate, Vector2 p)
        {
            float ex = climate.Sample(p.x + GradientSpan, p.y).Elevation - climate.Sample(p.x - GradientSpan, p.y).Elevation;
            float ey = climate.Sample(p.x, p.y + GradientSpan).Elevation - climate.Sample(p.x, p.y - GradientSpan).Elevation;
            return new Vector2(-ex, -ey);
        }

        private static bool TooClose(Vector2Int p, List<Vector2Int> sources)
        {
            int limit = MinSourceSpacing * MinSourceSpacing;
            for (int i = 0; i < sources.Count; i++)
                if ((sources[i] - p).sqrMagnitude < limit) return true;
            return false;
        }

        private static bool IsWater(WorldBiome biome)
            => biome == WorldBiome.Ocean || biome == WorldBiome.DeepOcean || biome == WorldBiome.River;
    }
}
