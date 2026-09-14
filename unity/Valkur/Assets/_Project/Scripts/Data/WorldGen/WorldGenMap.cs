using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// The world sampled on a regular grid of cells — the Seed World preview's data, and the
    /// statistics and spawn point it reports.
    ///
    /// <para><b>Every cell is ONE call to <see cref="WorldClimate.Sample"/> +
    /// <see cref="WorldClimate.Classify"/> at that cell's centre, in world tiles.</b> At full
    /// resolution (one cell per tile) this IS the world; below it, the preview shows exactly the
    /// tiles it lands on rather than an approximation of them.</para>
    /// </summary>
    public sealed class WorldGenMap
    {
        /// <summary>
        /// The preview's longest side, in cells. The cost is cells, not tiles, so a 2048-wide world
        /// previews as fast as a 320-wide one; the Seed World editor reports the measured time.
        /// </summary>
        public const int DefaultMaxSide = 320;

        public readonly int Columns;
        public readonly int Rows;

        /// <summary>World tiles per cell, on both axes.</summary>
        public readonly float TilesPerCell;

        public readonly byte[] Biomes;
        public readonly float[] Elevation;
        public readonly float[] Temperature;
        public readonly float[] Humidity;
        public readonly float[] Rarity;

        /// <summary>Cells per biome, indexed by <see cref="WorldBiome"/>.</summary>
        public readonly int[] BiomeCounts;

        public readonly WorldClimate Climate;

        /// <summary>The rivers of this world, traced in TILE space — the same list the build rasterises.</summary>
        public IReadOnlyList<WorldRiver> Rivers { get; private set; } = new List<WorldRiver>();

        /// <summary>Every river tile at the profile's width. Shared with the town planner so a street never crosses water.</summary>
        public HashSet<Vector2Int> RiverTiles { get; private set; } = new HashSet<Vector2Int>();

        /// <summary>The towns of this world — the same plan the build fills with buildings.</summary>
        public IReadOnlyList<WorldTown> Towns { get; private set; } = new List<WorldTown>();

        /// <summary>Per cell: 0 nothing, 1 inside a town, 2 street or plaza. Drawn over the biome layer.</summary>
        public readonly byte[] TownMask;

        /// <summary>Where a new run would start, in world tiles. Always on walkable land when any exists.</summary>
        public Vector2 SpawnTile { get; private set; }

        public bool HasSpawn { get; private set; }

        public int CellCount => Columns * Rows;

        private WorldGenMap(WorldClimate climate, int columns, int rows, float tilesPerCell)
        {
            Climate = climate;
            Columns = columns;
            Rows = rows;
            TilesPerCell = tilesPerCell;

            int n = columns * rows;
            Biomes = new byte[n];
            Elevation = new float[n];
            Temperature = new float[n];
            Humidity = new float[n];
            Rarity = new float[n];
            TownMask = new byte[n];
            BiomeCounts = new int[WorldBiomeTable.Count];
        }

        public static WorldGenMap Generate(WorldGenSettings settings, int maxSide = DefaultMaxSide)
            => Generate(new WorldClimate(settings), maxSide);

        public static WorldGenMap Generate(WorldClimate climate, int maxSide = DefaultMaxSide)
        {
            if (climate == null) throw new ArgumentNullException(nameof(climate));
            var s = climate.Settings;
            maxSide = Mathf.Max(8, maxSide);

            int longest = Mathf.Max(s.widthTiles, s.heightTiles);
            float tilesPerCell = Mathf.Max(1f, (float)longest / maxSide);

            int columns = Mathf.Max(1, Mathf.CeilToInt(s.widthTiles / tilesPerCell));
            int rows = Mathf.Max(1, Mathf.CeilToInt(s.heightTiles / tilesPerCell));

            var map = new WorldGenMap(climate, columns, rows, tilesPerCell);
            map.Fill();
            map.PaintRivers();
            map.FindSpawn();
            map.PlanTowns();
            return map;
        }

        public int Index(int column, int row) => row * Columns + column;

        /// <summary>The world-tile centre of a cell, clamped inside the map.</summary>
        public Vector2 CellCentre(int column, int row)
        {
            var s = Climate.Settings;
            float x = Mathf.Min((column + 0.5f) * TilesPerCell, s.widthTiles - 0.5f);
            float y = Mathf.Min((row + 0.5f) * TilesPerCell, s.heightTiles - 0.5f);
            return new Vector2(x, y);
        }

        public WorldBiome BiomeOf(int column, int row) => (WorldBiome)Biomes[Index(column, row)];

        public float Fraction(WorldBiome biome)
        {
            int n = CellCount;
            return n > 0 ? (float)BiomeCounts[(int)biome] / n : 0f;
        }

        /// <summary>Every cell that is neither water nor highland.</summary>
        public float LandFraction
        {
            get
            {
                int land = 0;
                for (int i = 0; i < BiomeCounts.Length; i++)
                    if (IsWalkableKind(WorldBiomeTable.GetAt(i).Kind)) land += BiomeCounts[i];
                int n = CellCount;
                return n > 0 ? (float)land / n : 0f;
            }
        }

        private void Fill()
        {
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Columns; col++)
                {
                    var p = CellCentre(col, row);
                    var sample = Climate.Sample(p.x, p.y);
                    var biome = Climate.Classify(sample);

                    int i = Index(col, row);
                    Biomes[i] = (byte)biome;
                    Elevation[i] = sample.Elevation;
                    Temperature[i] = sample.Temperature;
                    Humidity[i] = sample.Humidity;
                    Rarity[i] = sample.Rarity;
                    BiomeCounts[(int)biome]++;
                }
            }
        }

        /// <summary>
        /// Marks every cell a river tile falls in. A cell is river when ANY tile inside it is,
        /// which overstates a one-tile river at low preview resolution — deliberately: the
        /// alternative (majority) makes every river under ~3 tiles wide invisible in a preview
        /// of a large world, and a preview that hides rivers the build will carve is the worse lie.
        /// Water cells stay water: a river reaching the sea is the sea there.
        /// </summary>
        private void PaintRivers()
        {
            var s = Climate.Settings;
            Rivers = WorldRivers.Generate(Climate);
            var tiles = WorldRiverRaster.Tiles(Rivers, s.riverWidth, s.widthTiles, s.heightTiles);
            RiverTiles = tiles;

            foreach (var t in tiles)
            {
                int col = Mathf.Min(Columns - 1, Mathf.FloorToInt(t.x / TilesPerCell));
                int row = Mathf.Min(Rows - 1, Mathf.FloorToInt(t.y / TilesPerCell));
                int i = Index(col, row);
                var current = (WorldBiome)Biomes[i];
                if (current == WorldBiome.River || current == WorldBiome.Ocean || current == WorldBiome.DeepOcean) continue;

                BiomeCounts[(int)current]--;
                BiomeCounts[(int)WorldBiome.River]++;
                Biomes[i] = (byte)WorldBiome.River;
            }
        }

        /// <summary>
        /// Plans the towns and, when the first is the starting town, moves the spawn to its plaza:
        /// a run should begin somewhere people live.
        /// </summary>
        private void PlanTowns()
        {
            Towns = WorldTowns.Plan(Climate, RiverTiles);

            foreach (var town in Towns)
            {
                int r = town.Radius;
                for (int y = town.Center.y - r; y <= town.Center.y + r; y++)
                    for (int x = town.Center.x - r; x <= town.Center.x + r; x++)
                    {
                        var t = new Vector2Int(x, y);
                        if (!town.Contains(t)) continue;
                        MarkTown(t, town.StreetTiles.Contains(t) ? (byte)2 : (byte)1);
                    }

                if (town.IsStart)
                {
                    var spawn = WorldTowns.SpawnTileOf(town);
                    SpawnTile = new Vector2(spawn.x + 0.5f, spawn.y + 0.5f);
                    HasSpawn = true;
                }
            }
        }

        private void MarkTown(Vector2Int tile, byte value)
        {
            if (tile.x < 0 || tile.y < 0) return;
            int col = Mathf.FloorToInt(tile.x / TilesPerCell);
            int row = Mathf.FloorToInt(tile.y / TilesPerCell);
            if (col >= Columns || row >= Rows) return;
            int i = Index(col, row);
            if (TownMask[i] < value) TownMask[i] = value;
        }

        private static bool IsWalkableKind(WorldBiomeKind kind)
            => kind == WorldBiomeKind.Land || kind == WorldBiomeKind.Shore || kind == WorldBiomeKind.Rare;

        /// <summary>
        /// The walkable cell nearest the centre, with gentle biomes preferred. A run that begins in
        /// a volcano or on a corrupted patch is a run that begins by dying, and one that begins
        /// on the map's edge wastes the map.
        /// </summary>
        private void FindSpawn()
        {
            var s = Climate.Settings;
            var centre = new Vector2(s.widthTiles * 0.5f, s.heightTiles * 0.5f);
            float bestScore = float.MaxValue;
            int bestCol = -1, bestRow = -1;

            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Columns; col++)
                {
                    var info = WorldBiomeTable.GetAt(Biomes[Index(col, row)]);
                    if (!IsWalkableKind(info.Kind)) continue;

                    float score = (CellCentre(col, row) - centre).sqrMagnitude;
                    if (!IsGentle(info.Biome)) score *= 4f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestCol = col;
                        bestRow = row;
                    }
                }
            }

            HasSpawn = bestCol >= 0;
            SpawnTile = HasSpawn ? CellCentre(bestCol, bestRow) : centre;
        }

        private static bool IsGentle(WorldBiome biome)
            => biome == WorldBiome.Plains || biome == WorldBiome.Forest || biome == WorldBiome.AutumnForest;
    }
}
