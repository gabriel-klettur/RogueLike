using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// Where towns are, and the shape of their streets. Pure and deterministic.
    ///
    /// <para><b>Sites, the way Minecraft places structures:</b> seeded candidate points with a
    /// minimum spacing, each accepted only if the ground under it is walkable land. The first town
    /// is the STARTING town, searched outward from the centre of the world, and the run begins
    /// on its main street instead of in a field.</para>
    ///
    /// <para><b>Streets are a plaza, four main arms and seeded side streets</b> — the "start piece
    /// plus connectors" idea of a jigsaw village, with the connectors fixed rather than drawn from
    /// a pool. Every street tile is clipped to walkable ground, so a street that meets a river or
    /// a cliff simply stops there.</para>
    /// </summary>
    public static class WorldTowns
    {
        private const int TownSalt = 7;

        public const int PlazaSize = 8;
        public const int MainStreetWidth = 3;
        public const int SideStreetWidth = 2;

        /// <summary>Distance along a main arm between possible side streets.</summary>
        public const int SideStreetSpacing = 8;

        /// <summary>Distance from the plaza's edge to the first possible side street.</summary>
        public const int FirstSideStreet = 6;

        /// <summary>Fraction of a town's footprint that must be walkable land for the site to be accepted.</summary>
        public const float MinWalkableFraction = 0.9f;

        /// <summary>
        /// The size of a built zone, in tiles — what every loader in the project assumes. Towns are
        /// centred on a zone's centre (plus a little jitter) so a town never straddles a zone
        /// border: the border is where the zone banner fires and the zone's NAME changes, and the
        /// first bake put one straight through the starting town's plaza, which announced
        /// "Montana 4" to a player standing beside the fountain.
        /// </summary>
        public const int ZoneSize = 50;

        /// <summary>Candidate points tried per requested town.</summary>
        private const int AttemptsPerTown = 40;

        /// <summary>
        /// The starting town is searched outward from the CENTRE of the world, never from a spawn
        /// point a preview computed: the preview's spawn depends on its resolution, and a town plan
        /// that moved with the preview resolution would put the starting town somewhere else in the
        /// build than in the picture.
        /// </summary>
        public static List<WorldTown> Plan(WorldClimate climate, HashSet<Vector2Int> riverTiles)
        {
            var towns = new List<WorldTown>();
            var s = climate.Settings;
            if (s.townCount <= 0) return towns;

            var rng = new System.Random(WorldSeed.Derive(s.seed, TownSalt));
            int r = s.townRadius;
            int spacing = r * 3;

            if (s.startingTown)
            {
                var start = FindZoneCentredSite(climate, riverTiles, new Vector2Int(s.widthTiles / 2, s.heightTiles / 2), r);
                if (start.HasValue) towns.Add(BuildTown(climate, riverTiles, towns.Count, start.Value, r, true, rng));
            }

            int attempts = s.townCount * AttemptsPerTown;
            for (int a = 0; a < attempts && towns.Count < s.townCount; a++)
            {
                var c = ZoneCentreOf(new Vector2Int(rng.Next(0, s.widthTiles), rng.Next(0, s.heightTiles)));
                c += Jitter(rng, r);
                if (c.x < r + 2 || c.y < r + 2 || c.x > s.widthTiles - r - 2 || c.y > s.heightTiles - r - 2) continue;
                if (TooClose(c, towns, spacing)) continue;
                if (!SiteIsWalkable(climate, riverTiles, c, r)) continue;
                towns.Add(BuildTown(climate, riverTiles, towns.Count, c, r, false, rng));
            }

            return towns;
        }

        /// <summary>
        /// Where a run begins in a starting town: on the south main street, two tiles below the
        /// plaza. The plaza's centre holds a fountain and its corners hold stalls and lamps; a
        /// street tile is the one place guaranteed to hold no building.
        /// </summary>
        public static Vector2Int SpawnTileOf(WorldTown town)
            => new Vector2Int(town.Center.x, town.Plaza.yMin - 2);

        /// <summary>
        /// Street tiles for the town's residents to stand on, spread around the plaza: the tiles
        /// between <paramref name="minDistance"/> and <paramref name="maxDistance"/> of the centre,
        /// taken at even steps of angle so two vendors never share a corner of town.
        /// </summary>
        public static List<Vector2Int> ResidentTiles(WorldTown town, int count, int minDistance = 6, int maxDistance = 11)
        {
            var ring = new List<Vector2Int>();
            foreach (var t in town.StreetTiles)
            {
                float d = Vector2Int.Distance(t, town.Center);
                if (d >= minDistance && d <= maxDistance && !town.Plaza.Contains(t)) ring.Add(t);
            }
            ring.Sort((a, b) =>
            {
                int byAngle = Angle(a, town.Center).CompareTo(Angle(b, town.Center));
                if (byAngle != 0) return byAngle;
                int byX = a.x.CompareTo(b.x);
                return byX != 0 ? byX : a.y.CompareTo(b.y);
            });

            var picked = new List<Vector2Int>();
            if (ring.Count == 0 || count <= 0) return picked;
            for (int i = 0; i < count && i < ring.Count; i++)
                picked.Add(ring[(int)((long)i * ring.Count / Mathf.Min(count, ring.Count))]);
            return picked;
        }

        private static float Angle(Vector2Int t, Vector2Int c) => Mathf.Atan2(t.y - c.y, t.x - c.x);

        /// <summary>True when the tile is ground a street or a house may stand on.</summary>
        public static bool IsBuildable(WorldClimate climate, HashSet<Vector2Int> riverTiles, Vector2Int tile)
        {
            var s = climate.Settings;
            if (tile.x < 0 || tile.y < 0 || tile.x >= s.widthTiles || tile.y >= s.heightTiles) return false;
            if (riverTiles != null && riverTiles.Contains(tile)) return false;
            var kind = WorldBiomeTable.Get(climate.BiomeAt(tile.x + 0.5f, tile.y + 0.5f)).Kind;
            return kind == WorldBiomeKind.Land || kind == WorldBiomeKind.Shore || kind == WorldBiomeKind.Rare;
        }

        private static Vector2Int ZoneCentreOf(Vector2Int tile)
            => new Vector2Int(Floor(tile.x, ZoneSize) + ZoneSize / 2, Floor(tile.y, ZoneSize) + ZoneSize / 2);

        private static int Floor(int v, int step) => (v >= 0 ? v / step : (v - step + 1) / step) * step;

        /// <summary>Up to the slack a zone leaves around a town, so towns do not all sit dead centre.</summary>
        private static Vector2Int Jitter(System.Random rng, int r)
        {
            int slack = System.Math.Max(0, ZoneSize / 2 - r - 2);
            return new Vector2Int(rng.Next(-slack, slack + 1), rng.Next(-slack, slack + 1));
        }

        /// <summary>
        /// Zone centres in growing rings around <paramref name="start"/>; the plain tile search only
        /// when no zone centre is suitable (a town too big for a zone, or a world of islands).
        /// </summary>
        private static Vector2Int? FindZoneCentredSite(WorldClimate climate, HashSet<Vector2Int> riverTiles, Vector2Int start, int r)
        {
            var s = climate.Settings;
            var first = ZoneCentreOf(start);
            const int MaxZoneRings = 6;
            for (int ring = 0; ring <= MaxZoneRings; ring++)
                for (int dy = -ring; dy <= ring; dy++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue;
                        var c = first + new Vector2Int(dx * ZoneSize, dy * ZoneSize);
                        if (c.x < r + 2 || c.y < r + 2 || c.x > s.widthTiles - r - 2 || c.y > s.heightTiles - r - 2) continue;
                        if (SiteIsWalkable(climate, riverTiles, c, r)) return c;
                    }
            return FindSiteNear(climate, riverTiles, start, r);
        }

        private static Vector2Int? FindSiteNear(WorldClimate climate, HashSet<Vector2Int> riverTiles, Vector2Int start, int r)
        {
            const int Step = 6, MaxRings = 30;
            for (int ring = 0; ring <= MaxRings; ring++)
                for (int dy = -ring; dy <= ring; dy++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue;
                        var c = start + new Vector2Int(dx * Step, dy * Step);
                        var s = climate.Settings;
                        if (c.x < r + 2 || c.y < r + 2 || c.x > s.widthTiles - r - 2 || c.y > s.heightTiles - r - 2) continue;
                        if (SiteIsWalkable(climate, riverTiles, c, r)) return c;
                    }
            return null;
        }

        private static bool SiteIsWalkable(WorldClimate climate, HashSet<Vector2Int> riverTiles, Vector2Int c, int r)
        {
            if (!IsBuildable(climate, riverTiles, c)) return false;
            const int Samples = 7;
            int ok = 0, total = 0;
            for (int j = 0; j < Samples; j++)
                for (int i = 0; i < Samples; i++)
                {
                    var t = c + new Vector2Int(-r + (2 * r * i) / (Samples - 1), -r + (2 * r * j) / (Samples - 1));
                    if ((t - c).sqrMagnitude > r * r) continue;
                    total++;
                    if (IsBuildable(climate, riverTiles, t)) ok++;
                }
            return total > 0 && ok >= total * MinWalkableFraction;
        }

        private static bool TooClose(Vector2Int c, List<WorldTown> towns, int spacing)
        {
            for (int i = 0; i < towns.Count; i++)
                if ((towns[i].Center - c).sqrMagnitude < spacing * spacing) return true;
            return false;
        }

        private static WorldTown BuildTown(WorldClimate climate, HashSet<Vector2Int> riverTiles, int index,
                                           Vector2Int c, int r, bool isStart, System.Random rng)
        {
            int half = PlazaSize / 2;
            var plaza = new RectInt(c.x - half, c.y - half, PlazaSize, PlazaSize);
            var town = new WorldTown(index, c, r, isStart, plaza);

            int mw = MainStreetWidth / 2; // 1 for width 3
            var east = new RectInt(plaza.xMax, c.y - mw, r - half, MainStreetWidth);
            var west = new RectInt(c.x - r, c.y - mw, r - half, MainStreetWidth);
            var north = new RectInt(c.x - mw, plaza.yMax, MainStreetWidth, r - half);
            var south = new RectInt(c.x - mw, c.y - r, MainStreetWidth, r - half);
            town.Streets.Add(east);
            town.Streets.Add(west);
            town.Streets.Add(north);
            town.Streets.Add(south);

            // Side streets branch perpendicular off each arm, on either side, by coin.
            for (int d = half + FirstSideStreet; d < r - 4; d += SideStreetSpacing)
            {
                AddSide(town, rng, r, horizontalArm: true, at: c.x + d, c);
                AddSide(town, rng, r, horizontalArm: true, at: c.x - d - SideStreetWidth, c);
                AddSide(town, rng, r, horizontalArm: false, at: c.y + d, c);
                AddSide(town, rng, r, horizontalArm: false, at: c.y - d - SideStreetWidth, c);
            }

            Rasterise(town, plaza, climate, riverTiles);
            foreach (var street in town.Streets) Rasterise(town, street, climate, riverTiles);
            return town;
        }

        private static void AddSide(WorldTown town, System.Random rng, int r, bool horizontalArm, int at, Vector2Int c)
        {
            int mw = MainStreetWidth / 2;
            int maxLen = Mathf.Max(4, (int)(r * 0.7f));
            // Each side of the arm is its own coin, so a town is not a symmetric cross of crosses.
            if (rng.NextDouble() < 0.6)
            {
                int len = rng.Next(5, maxLen);
                town.Streets.Add(horizontalArm
                    ? new RectInt(at, c.y + mw + 1, SideStreetWidth, len)
                    : new RectInt(c.x + mw + 1, at, len, SideStreetWidth));
            }
            if (rng.NextDouble() < 0.6)
            {
                int len = rng.Next(5, maxLen);
                town.Streets.Add(horizontalArm
                    ? new RectInt(at, c.y - mw - len, SideStreetWidth, len)
                    : new RectInt(c.x - mw - len, at, len, SideStreetWidth));
            }
        }

        private static void Rasterise(WorldTown town, RectInt rect, WorldClimate climate, HashSet<Vector2Int> riverTiles)
        {
            for (int y = rect.yMin; y < rect.yMax; y++)
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    var t = new Vector2Int(x, y);
                    if (!town.Contains(t, 1)) continue;
                    if (!IsBuildable(climate, riverTiles, t)) continue;
                    town.StreetTiles.Add(t);
                }
        }
    }
}
