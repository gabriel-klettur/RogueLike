using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data.WorldGen;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// Everything about a generated world that is decided ONCE, before any tile exists: the
    /// climate, the plan (rivers, towns, camps, spawn), the zone grid, the zone names and where
    /// the world sits. The bake and the live world both start here, which is what makes a zone
    /// generated on demand the same zone the bake would have written.
    ///
    /// <para><b>The world is centred on the origin</b>, because the Y-sort budget is symmetric
    /// (<c>SortingConfig.MAX_SAFE_WORLD_Y</c> either side of zero): a 600-tile-tall world fits
    /// only from -300 to +300.</para>
    /// </summary>
    public sealed class SeedWorldPlan
    {
        /// <summary>How far from the planned spawn a walkable tile is searched for, in tiles.</summary>
        public const int SpawnSearchRadius = 80;

        public readonly WorldClimate Climate;
        public readonly WorldGenMap Map;
        public readonly int ZoneSize;
        public readonly int ZonesX;
        public readonly int ZonesY;

        /// <summary>World tile of the plan's (0, 0).</summary>
        public readonly Vector2Int Origin;

        /// <summary>Zone names indexed [zx, zy]; the names the slot file and every loader use.</summary>
        public readonly string[,] ZoneNames;

        /// <summary>The spawn, as a plan tile the player can walk off.</summary>
        public readonly Vector2Int SpawnTile;

        public WorldGenSettings Settings => Climate.Settings;
        public int BuiltTilesW => ZonesX * ZoneSize;
        public int BuiltTilesH => ZonesY * ZoneSize;
        public Vector2 SpawnWorld => new Vector2(Origin.x + SpawnTile.x + 0.5f, Origin.y + SpawnTile.y + 0.5f);

        private SeedWorldPlan(WorldClimate climate, WorldGenMap map, int zoneSize, Func<string, string, bool> compatible)
        {
            Climate = climate;
            Map = map;
            ZoneSize = zoneSize;
            var s = climate.Settings;
            ZonesX = Mathf.CeilToInt((float)s.widthTiles / zoneSize);
            ZonesY = Mathf.CeilToInt((float)s.heightTiles / zoneSize);
            Origin = new Vector2Int(-(ZonesX / 2) * zoneSize, -(ZonesY / 2) * zoneSize);

            ZoneNames = new string[ZonesX, ZonesY];
            var labelCounts = new Dictionary<string, int>();
            for (int zy = 0; zy < ZonesY; zy++)
                for (int zx = 0; zx < ZonesX; zx++)
                    ZoneNames[zx, zy] = SeedWorldBaker.ZoneName(climate, map.Towns, zx * zoneSize, zy * zoneSize, zoneSize, labelCounts);

            SpawnTile = FindSpawn(compatible);
        }

        /// <summary>
        /// Rivers, towns and the spawn come from the SAME plan the Seed World preview draws; none
        /// of them depends on the preview's resolution, so a coarse 64-cell map is enough.
        /// </summary>
        public static SeedWorldPlan Create(WorldGenSettings settings, int zoneSize, Func<string, string, bool> compatible)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (compatible == null) throw new ArgumentNullException(nameof(compatible));
            var climate = new WorldClimate(settings);
            return new SeedWorldPlan(climate, WorldGenMap.Generate(climate, 64), Mathf.Max(1, zoneSize), compatible);
        }

        /// <summary>The ground of a tile rectangle, identical to the same tiles of a whole-world build.</summary>
        public WorldTerrainGrid BuildRegion(int x0, int y0, int tilesW, int tilesH, Func<string, string, bool> compatible)
            => WorldTerrainGrid.BuildRegion(Climate, Map.RiverTiles, Map.Towns, BuiltTilesW, BuiltTilesH,
                                            x0, y0, tilesW, tilesH, WorldTerrainGrid.RegionMargin, compatible);

        /// <summary>The ground of the whole built area.</summary>
        public WorldTerrainGrid BuildAll(Func<string, string, bool> compatible)
            => WorldTerrainGrid.Build(Climate, Map.Rivers, Map.Towns, BuiltTilesW, BuiltTilesH, compatible);

        /// <summary>The zone holding a plan tile, or false outside the built area.</summary>
        public bool TryZoneOfTile(Vector2Int tile, out Vector2Int zone)
        {
            zone = new Vector2Int(Mathf.FloorToInt((float)tile.x / ZoneSize), Mathf.FloorToInt((float)tile.y / ZoneSize));
            return zone.x >= 0 && zone.y >= 0 && zone.x < ZonesX && zone.y < ZonesY;
        }

        /// <summary>The zone holding a WORLD position, or false outside the built area.</summary>
        public bool TryZoneOfWorld(Vector2 world, out Vector2Int zone)
            => TryZoneOfTile(new Vector2Int(Mathf.FloorToInt(world.x) - Origin.x, Mathf.FloorToInt(world.y) - Origin.y), out zone);

        /// <summary>World tile of a zone's bottom-left cell: the zone's <c>gridOffset</c>.</summary>
        public Vector2Int ZoneOffset(int zx, int zy) => new Vector2Int(Origin.x + zx * ZoneSize, Origin.y + zy * ZoneSize);

        private Vector2Int FindSpawn(Func<string, string, bool> compatible)
        {
            var start = Vector2Int.FloorToInt(Map.SpawnTile);
            int r = SpawnSearchRadius + 2;
            int x0 = Mathf.Clamp(start.x - r, 0, BuiltTilesW), y0 = Mathf.Clamp(start.y - r, 0, BuiltTilesH);
            int x1 = Mathf.Clamp(start.x + r, 0, BuiltTilesW), y1 = Mathf.Clamp(start.y + r, 0, BuiltTilesH);
            var grid = BuildRegion(x0, y0, x1 - x0, y1 - y0, compatible);
            return SeedWorldZoneBuilder.FindWalkableTile(grid, Settings, start, x0, y0, x1, y1, SpawnSearchRadius);
        }
    }
}
