using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Data.Biomes;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Map Editor biome generator. Paints the Ground tilemap and scatters
    /// buildings across one or every zone according to a chosen
    /// <see cref="BiomeKind"/> recipe. Tile changes persist via
    /// <see cref="TileOverlayPersistence"/>; building spawns are session-only
    /// for this revision (they live until the next biome regeneration or
    /// scene reload).
    /// </summary>
    public partial class MapEditorManager
    {
        // Reserved instance-id space so biome-spawned buildings never collide
        // with the data-driven instances loaded from buildings_instances.json
        // (whose IDs sit far below this base).
        private const int BIOME_INSTANCE_ID_BASE = 1_000_000;

        private static int s_biomeInstanceCounter;

        private BiomeCatalog _biomeCatalog;
        private BuildingLoader _cachedBuildingLoader;

        public string GenerateBiomes(BiomeGenerationRequest req)
        {
            if (zoneManager == null) return "ZoneManager missing.";
            if (worldGridBuilder == null) return "WorldGridBuilder missing.";

            var groundTilemap = worldGridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            if (groundTilemap == null) return "Ground tilemap not built yet.";

            var biomeCatalog = ResolveBiomeCatalog();
            if (biomeCatalog == null) return "Biome catalog unavailable.";

            var tileCatalog = ResolveTileCatalog();
            if (tileCatalog == null) return "Tile catalog empty. Open the Tile Editor (F8) once to populate.";

            var targetZones = CollectTargetZones(req);
            if (targetZones.Count == 0) return "No zones to generate.";

            // Wipe previous biome run so regenerations don't pile up.
            ResolveBuildingLoader()?.ClearGeneratedAbove(BIOME_INSTANCE_ID_BASE);

            var rng = new System.Random(req.seed);
            int totalTiles = 0;
            int totalBuildings = 0;

            foreach (var zone in targetZones)
            {
                BiomeKind kind = req.randomPerZone ? RandomBiome(rng) : req.biome;
                if (!biomeCatalog.TryGet(kind, out var recipe)) continue;

                totalTiles     += PaintZoneTiles(zone, recipe, tileCatalog, groundTilemap, rng);
                totalBuildings += ScatterZoneBuildings(zone, recipe, rng);

                tileEditorManager?.Persistence?.SaveZone(zone.zoneName);
            }

            return $"Biomes: {totalTiles} tiles + {totalBuildings} buildings across {targetZones.Count} zone(s).";
        }

        // ── Resolution helpers ───────────────────────────────────────────────────

        private BiomeCatalog ResolveBiomeCatalog()
        {
            if (_biomeCatalog != null) return _biomeCatalog;
            _biomeCatalog = Resources.Load<BiomeCatalog>("BiomeCatalog");
            if (_biomeCatalog == null)
                _biomeCatalog = BiomeCatalog.BuildDefault();
            return _biomeCatalog;
        }

        private TileCatalog ResolveTileCatalog()
        {
            if (TileRegistry.Instance.IsLoaded && TileRegistry.Instance.Catalog != null)
                return TileRegistry.Instance.Catalog;

            var built = TileCatalog.BuildFromResources();
            if (built != null && built.Entries.Count > 0)
            {
                TileRegistry.Instance.Load(built);
                return built;
            }
            return null;
        }

        private BuildingLoader ResolveBuildingLoader()
        {
            if (_cachedBuildingLoader != null) return _cachedBuildingLoader;
            _cachedBuildingLoader = FindObjectOfType<BuildingLoader>();
            return _cachedBuildingLoader;
        }

        // ── Zone targeting ───────────────────────────────────────────────────────

        private List<ZoneManager.ZoneDefinition> CollectTargetZones(BiomeGenerationRequest req)
        {
            var list = new List<ZoneManager.ZoneDefinition>();
            if (req.selectedZoneOnly && !string.IsNullOrEmpty(req.selectedZoneName))
            {
                if (zoneManager.TryGetZone(req.selectedZoneName, out var z))
                    list.Add(z);
                return list;
            }

            list.AddRange(zoneManager.GetZonesSnapshot());
            return list;
        }

        private static BiomeKind RandomBiome(System.Random rng)
        {
            var values = (BiomeKind[])System.Enum.GetValues(typeof(BiomeKind));
            return values[rng.Next(values.Length)];
        }

        // ── Tile painting ────────────────────────────────────────────────────────

        private int PaintZoneTiles(ZoneManager.ZoneDefinition zone, BiomeRecipe recipe,
            TileCatalog tileCatalog, Tilemap groundTilemap, System.Random rng)
        {
            var baseTiles = tileCatalog.GetTilesForCategory(recipe.baseTileCategory);
            if (baseTiles == null || baseTiles.Count == 0) return 0;

            var variantTiles = string.IsNullOrEmpty(recipe.variantTileCategory)
                ? null
                : tileCatalog.GetTilesForCategory(recipe.variantTileCategory);

            // Pick a representative tile per category (first entry). Designer-driven
            // refinement (e.g. picking a specific named tile) can be added later
            // without touching this loop.
            TileBase baseTile    = baseTiles[0].tile;
            TileBase variantTile = (variantTiles != null && variantTiles.Count > 0)
                ? variantTiles[0].tile : null;

            var rect       = zoneManager.GetZoneRect(zone);
            float scale    = Mathf.Max(0.01f, recipe.variantNoiseScale);
            float seedX    = (float)(rng.NextDouble() * 1000.0);
            float seedY    = (float)(rng.NextDouble() * 1000.0);

            int painted = 0;
            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    TileBase pick = baseTile;
                    if (variantTile != null && recipe.variantNoiseThreshold < 1f)
                    {
                        float n = Mathf.PerlinNoise((x + seedX) * scale, (y + seedY) * scale);
                        if (n > recipe.variantNoiseThreshold)
                            pick = variantTile;
                    }
                    groundTilemap.SetTile(new Vector3Int(x, y, 0), pick);
                    painted++;
                }
            }
            return painted;
        }

        // ── Building scattering ──────────────────────────────────────────────────

        private int ScatterZoneBuildings(ZoneManager.ZoneDefinition zone, BiomeRecipe recipe,
            System.Random rng)
        {
            if (recipe.buildingPathFilters == null || recipe.buildingPathFilters.Length == 0)
                return 0;
            if (recipe.buildingDensityPer100Cells <= 0f) return 0;

            var loader = ResolveBuildingLoader();
            if (loader == null || loader.Catalog == null) return 0;

            var candidates = FilterCandidates(loader.Catalog, recipe.buildingPathFilters);
            if (candidates.Count == 0) return 0;

            var rect = zoneManager.GetZoneRect(zone);
            int area = rect.width * rect.height;
            int target = Mathf.Max(0, Mathf.RoundToInt(area * recipe.buildingDensityPer100Cells / 100f));
            if (target == 0) return 0;

            const float MARGIN_WU = 1f;
            float minSpacingSq = Mathf.Max(0.01f, recipe.minBuildingSpacingWu);
            minSpacingSq *= minSpacingSq;

            var placed = new List<Vector2>(target);
            int spawned = 0;
            int attempts = 0;
            int maxAttempts = target * 12;

            while (spawned < target && attempts < maxAttempts)
            {
                attempts++;
                float wx = rect.xMin + MARGIN_WU + (float)rng.NextDouble() * (rect.width  - 2f * MARGIN_WU);
                float wy = rect.yMin + MARGIN_WU + (float)rng.NextDouble() * (rect.height - 2f * MARGIN_WU);

                if (IsTooClose(placed, wx, wy, minSpacingSq)) continue;

                var template   = candidates[rng.Next(candidates.Count)];
                int instanceId = BIOME_INSTANCE_ID_BASE + (++s_biomeInstanceCounter);

                var bObj = loader.SpawnAtWorldPosition(
                    template.templateId, zone.zoneName,
                    new Vector3(wx, wy, 0f), instanceId);
                if (bObj == null) continue;

                placed.Add(new Vector2(wx, wy));
                spawned++;
            }
            return spawned;
        }

        private static List<BuildingTemplateData> FilterCandidates(BuildingCatalog catalog, string[] filters)
        {
            var result = new List<BuildingTemplateData>();
            for (int i = 0; i < catalog.Templates.Count; i++)
            {
                var t = catalog.Templates[i];
                if (t == null || string.IsNullOrEmpty(t.assetPath)) continue;
                for (int f = 0; f < filters.Length; f++)
                {
                    var filter = filters[f];
                    if (!string.IsNullOrEmpty(filter) &&
                        t.assetPath.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(t);
                        break;
                    }
                }
            }
            return result;
        }

        private static bool IsTooClose(List<Vector2> placed, float x, float y, float minSpacingSq)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                float dx = placed[i].x - x;
                float dy = placed[i].y - y;
                if (dx * dx + dy * dy < minSpacingSq) return true;
            }
            return false;
        }

        public struct BiomeGenerationRequest
        {
            public BiomeKind biome;
            public bool randomPerZone;
            public bool selectedZoneOnly;
            public string selectedZoneName;
            public int seed;
        }
    }
}
