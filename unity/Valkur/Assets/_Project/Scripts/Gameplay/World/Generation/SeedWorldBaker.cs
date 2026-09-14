using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.MapEditor;
using Debug = UnityEngine.Debug;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// Builds a generated world into a map slot: one 50x50 overlay per zone plus the slot's zone
    /// list, in exactly the shape the Map editor writes, so loading it is
    /// <c>MapEditorManager.LoadMapSlot</c> and nothing new.
    ///
    /// <para><b>It never writes the base world.</b> The target is always a named slot, whose
    /// overlays live in their own <c>MapOverrides/&lt;slot&gt;/</c> folder, and <c>default</c> is
    /// refused by <see cref="SeedWorldBakeRequest"/>. A slot that exists and carries no
    /// <see cref="SeedWorldMarker"/> was made by a person and is refused too.</para>
    ///
    /// <para><b>The world is centred on the origin</b>, because the Y-sort budget is symmetric
    /// (<c>SortingConfig.MAX_SAFE_WORLD_Y</c> either side of zero): a 600-tile-tall world fits
    /// only from -300 to +300.</para>
    ///
    /// <para><b>What a zone file carries:</b> <c>Ground</c> (every cell), <c>Collision</c> (only
    /// when the zone has a blocked cell — water, lava and peaks, with the ground's own tile so it
    /// draws nothing new), and <c>terrains</c>, the vertex terrain the Tile editor's auto-brush
    /// continues from.</para>
    /// </summary>
    public static class SeedWorldBaker
    {
        /// <summary>A cell is blocked when at least this many of its corners are water or lava.</summary>
        public const int BlockingCorners = 3;

        /// <summary>Rock above the mountain line by this much is a peak nobody walks over.</summary>
        public const float PeakMargin = 0.08f;

        public enum SlotState
        {
            /// <summary>Nothing on disk under that name.</summary>
            Free,
            /// <summary>A world Seed World built earlier; safe to overwrite.</summary>
            SeedWorld,
            /// <summary>A map somebody made. Never overwritten.</summary>
            Foreign,
        }

        public static SlotState Inspect(SeedWorldBakeRequest request)
        {
            if (request == null) return SlotState.Foreign;
            bool slotFile = File.Exists(request.SlotFilePath);
            bool overlays = Directory.Exists(request.OverridesDirectory)
                            && Directory.GetFiles(request.OverridesDirectory, "*.overlay.json").Length > 0;
            if (!slotFile && !overlays) return SlotState.Free;
            return File.Exists(request.MarkerPath) ? SlotState.SeedWorld : SlotState.Foreign;
        }

        public static SeedWorldBakeResult Bake(WorldGenSettings settings, SeedWorldBakeRequest request,
                                               SeedWorldTilePalette palette)
            => Bake(settings, request, palette, null);

        /// <summary>
        /// As above, with towns built from <paramref name="buildings"/>. A null catalogue still
        /// lays out the towns' streets on the ground; it just puts nothing beside them.
        /// </summary>
        public static SeedWorldBakeResult Bake(WorldGenSettings settings, SeedWorldBakeRequest request,
                                               SeedWorldTilePalette palette, BuildingCatalog buildings)
            => Bake(settings, request, palette, buildings, null);

        /// <summary>
        /// As above, with the population: the starting town's vendors and the hostile camps, as
        /// spawners from <paramref name="spawners"/>. Trees come from <paramref name="buildings"/>.
        /// </summary>
        public static SeedWorldBakeResult Bake(WorldGenSettings settings, SeedWorldBakeRequest request,
                                               SeedWorldTilePalette palette, BuildingCatalog buildings,
                                               SpawnerTemplateCatalog spawners)
        {
            if (settings == null) return SeedWorldBakeResult.Fail("Sin parametros.");
            if (request == null) return SeedWorldBakeResult.Fail("Nombre de mapa no valido (vacio o 'default').");
            if (palette == null || !palette.HasCatalog) return SeedWorldBakeResult.Fail("No hay TerrainCatalog con packs Corner16.");

            var state = Inspect(request);
            if (state == SlotState.Foreign)
                return SeedWorldBakeResult.Fail($"Ya existe un mapa '{request.Slot}' que no creo Seed World. Elige otro nombre.");

            if (WorldDataWriteGuard.Refuse("SeedWorldBaker", request.OverridesDirectory, request.UsesRealRoots))
                return SeedWorldBakeResult.Fail("Escritura rechazada durante un test.");

            var sw = Stopwatch.StartNew();
            var climate = new WorldClimate(settings);
            var s = climate.Settings;
            int z = request.ZoneSize;

            int zonesX = Mathf.CeilToInt((float)s.widthTiles / z);
            int zonesY = Mathf.CeilToInt((float)s.heightTiles / z);
            var origin = new Vector2Int(-(zonesX / 2) * z, -(zonesY / 2) * z);

            // Rivers, towns and the spawn come from the SAME plan the Seed World preview draws;
            // none of them depends on the preview's resolution.
            var preview = WorldGenMap.Generate(climate, 64);
            var rivers = preview.Rivers;
            var grid = WorldTerrainGrid.Build(climate, rivers, preview.Towns, zonesX * z, zonesY * z, palette.Compatible);

            var placements = new List<WorldTownPlacement>();
            var options = SeedWorldTownPalette.Options(buildings);
            foreach (var town in preview.Towns)
                placements.AddRange(WorldTownLots.Place(town, climate, preview.RiverTiles, options));
            int townBuildings = placements.Count;
            placements.AddRange(SeedWorldPopulation.TreePlacements(preview, buildings, new List<WorldTownPlacement>(placements)));

            var result = new SeedWorldBakeResult
            {
                Slot = request.Slot,
                ZonesX = zonesX,
                ZonesY = zonesY,
                Tiles = zonesX * z * zonesY * z,
                Rivers = rivers.Count,
                Towns = preview.Towns.Count,
                Buildings = townBuildings,
                Trees = placements.Count - townBuildings,
                RepairedVertices = grid.RepairedVertices,
                Origin = origin,
            };
            var spawnTile = FindWalkableTile(grid, s, Vector2Int.FloorToInt(preview.SpawnTile), zonesX * z, zonesY * z);
            result.SpawnWorld = new Vector2(origin.x + spawnTile.x + 0.5f, origin.y + spawnTile.y + 0.5f);
            result.GenerateMs = sw.ElapsedMilliseconds;

            sw.Restart();
            try
            {
                Directory.CreateDirectory(request.MapsDirectory);
                Directory.CreateDirectory(request.OverridesDirectory);
                foreach (var old in Directory.GetFiles(request.OverridesDirectory, "*.overlay.json"))
                    File.Delete(old);

                var zones = new List<ZonePersistenceEntry>(zonesX * zonesY);
                var sb = new StringBuilder(1 << 20);
                var labelCounts = new Dictionary<string, int>();
                var zoneNames = new string[zonesX, zonesY];
                for (int zy = 0; zy < zonesY; zy++)
                    for (int zx = 0; zx < zonesX; zx++)
                        zoneNames[zx, zy] = ZoneName(climate, preview.Towns, zx * z, zy * z, z, labelCounts);

                for (int zy = 0; zy < zonesY; zy++)
                    for (int zx = 0; zx < zonesX; zx++)
                    {
                        string name = zoneNames[zx, zy];
                        sb.Clear();
                        WriteZone(sb, grid, palette, s, zx * z, zy * z, z, origin, result);
                        string path = Path.Combine(request.OverridesDirectory, name + ".overlay.json");
                        File.WriteAllText(path, sb.ToString());
                        result.Bytes += sb.Length;

                        zones.Add(new ZonePersistenceEntry
                        {
                            zoneName = name,
                            gridOffsetX = origin.x + zx * z,
                            gridOffsetY = origin.y + zy * z,
                            editableInTileEditor = true,
                        });
                    }

                var slot = new ZonePersistenceFile
                {
                    restrictTileEditingToEditableZones = false,
                    nextZoneIndex = zones.Count + 1,
                    zones = zones,
                    hasLastPlayerPosition = preview.HasSpawn,
                    lastPlayerWorldX = result.SpawnWorld.x,
                    lastPlayerWorldY = result.SpawnWorld.y,
                };
                File.WriteAllText(request.SlotFilePath, JsonUtility.ToJson(slot, true));

                Directory.CreateDirectory(request.BuildingsDirectory);
                string buildingsJson = BuildingsJson(placements, buildings, zoneNames, z);
                File.WriteAllText(request.BuildingsFilePath, buildingsJson);
                result.Bytes += buildingsJson.Length;

                var records = SeedWorldPopulation.SpawnerRecords(preview, spawners, zoneNames, z);
                Directory.CreateDirectory(request.SpawnersDirectory);
                string spawnersJson = Valkur.Gameplay.Spawners.SpawnerInstanceSerializer.Serialize(records);
                File.WriteAllText(request.SpawnersFilePath, spawnersJson);
                result.Bytes += spawnersJson.Length;
                result.Spawners = records.Count;

                var marker = new SeedWorldMarker
                {
                    seed = s.seed,
                    bakedAtUtc = DateTime.UtcNow.ToString("o"),
                    settingsJson = s.ToJson(),
                };
                File.WriteAllText(request.MarkerPath, JsonUtility.ToJson(marker, true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SeedWorldBaker] Write failed for slot '{request.Slot}': {ex.Message}");
                return SeedWorldBakeResult.Fail("Fallo al escribir: " + ex.Message);
            }

            result.WriteMs = sw.ElapsedMilliseconds;
            return result;
        }

        /// <summary>
        /// A zone is named after the biome most of it is — "Bosque 3", "Oceano 12" — because the
        /// name is what the game SHOWS: the zone banner on entry and the minimap's caption. A
        /// grid coordinate there reads as debug text. Numbered per biome so names stay unique, and
        /// water zones get the name too, since a coast zone is still a place the player walks into.
        /// </summary>
        public static string ZoneName(WorldClimate climate, IReadOnlyList<WorldTown> towns,
                                      int baseX, int baseY, int z, Dictionary<string, int> counts)
        {
            // A zone holding a town's plaza is named after the town: that is the place the player
            // will remember, not the grass it stands on.
            if (towns != null)
                foreach (var town in towns)
                    if (town.Center.x >= baseX && town.Center.x < baseX + z &&
                        town.Center.y >= baseY && town.Center.y < baseY + z)
                        return town.IsStart ? "Pueblo inicial" : $"Pueblo {town.Index + 1}";

            const int Samples = 5;
            var votes = new int[WorldBiomeTable.Count];
            float step = (float)z / Samples;
            for (int j = 0; j < Samples; j++)
                for (int i = 0; i < Samples; i++)
                    votes[(int)climate.BiomeAt(baseX + (i + 0.5f) * step, baseY + (j + 0.5f) * step)]++;

            int best = 0;
            for (int b = 1; b < votes.Length; b++)
                if (votes[b] > votes[best]) best = b;

            string label = WorldBiomeTable.GetAt(best).DisplayName;
            counts.TryGetValue(label, out int n);
            counts[label] = ++n;
            return $"{label} {n}";
        }

        /// <summary>Bottom rows of a house's sprite that collide: the walls, not the roof drawn over the street behind.</summary>
        public const float BuildingSolidFraction = 0.45f;

        /// <summary>
        /// The slot's <c>buildings_instances.json</c>, in the shape the Buildings editor writes, so
        /// <c>BuildingLoader</c> spawns the towns and <c>BuildingCollisionLoader</c> gives each an
        /// inline collision grid. Without that grid a template no author painted does not collide
        /// at all, and a generated house would be a picture the player walks through.
        /// </summary>
        private static string BuildingsJson(List<WorldTownPlacement> placements, BuildingCatalog catalog,
                                            string[,] zoneNames, int z)
        {
            var sb = new StringBuilder(placements.Count * 256 + 2);
            sb.Append('[');
            int id = 0;
            foreach (var p in placements)
            {
                var template = catalog != null ? catalog.GetById(p.Option.TemplateId) : null;
                if (template == null) continue;

                int effW = template.originalScale.x, effH = template.originalScale.y;
                int zx = Mathf.Clamp(p.Rect.x / z, 0, zoneNames.GetLength(0) - 1);
                int zy = Mathf.Clamp(p.Rect.y / z, 0, zoneNames.GetLength(1) - 1);

                // BuildingLoader: worldX = gridX + (rel_x + effW/2)/32, worldY = gridY + (z-1) - (rel_y + effH)/32.
                // The sprite's left edge sits on the lot's left edge and its bottom on the lot's bottom.
                int relX = (p.Rect.x - zx * z) * 32;
                int relY = ((zy * z + z - 1) - p.Rect.y) * 32 - effH;

                int cols = Mathf.Max(1, Mathf.CeilToInt(effW / 32f));
                int rows = Mathf.Max(1, Mathf.CeilToInt(effH / 32f));
                int solidRows = SolidRows(p.Option.Kind, rows);
                // A lamp or a tree blocks at its post or trunk, not across the whole width of its light
                // or its canopy — a wood you cannot walk between is a wall.
                int solidCols = p.Option.Kind == WorldTownPieceKind.Lamp || p.Option.Kind == WorldTownPieceKind.Tree ? 1 : cols;
                int solidStart = (cols - solidCols) / 2;

                if (id > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(++id)
                  .Append(",\"template_id\":").Append(template.templateId)
                  .Append(",\"zone\":\"").Append(zoneNames[zx, zy]).Append('"')
                  .Append(",\"rel_x\":").Append(relX)
                  .Append(",\"rel_y\":").Append(relY)
                  .Append(",\"overrides\":{\"collider_scope\":\"CU\",\"collision_override\":{\"width\":").Append(cols)
                  .Append(",\"height\":").Append(rows).Append(",\"collision\":[");
                for (int r = 0; r < rows; r++)
                {
                    if (r > 0) sb.Append(',');
                    sb.Append('[');
                    bool solidRow = r >= rows - solidRows;
                    for (int c = 0; c < cols; c++)
                    {
                        if (c > 0) sb.Append(',');
                        bool solid = solidRow && c >= solidStart && c < solidStart + solidCols;
                        sb.Append(solid ? "\"#\"" : "\".\"");
                    }
                    sb.Append(']');
                }
                sb.Append("]}}}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static int SolidRows(WorldTownPieceKind kind, int rows)
        {
            switch (kind)
            {
                case WorldTownPieceKind.Lamp:
                case WorldTownPieceKind.Stall:
                case WorldTownPieceKind.Tree:
                    return 1;
                case WorldTownPieceKind.Centerpiece:
                    return Mathf.Max(1, rows / 2);
                default:
                    return Mathf.Clamp(Mathf.RoundToInt(rows * BuildingSolidFraction), 1, rows);
            }
        }

        private static void WriteZone(StringBuilder sb, WorldTerrainGrid grid, SeedWorldTilePalette palette,
                                      WorldGenSettings s, int baseX, int baseY, int z, Vector2Int origin,
                                      SeedWorldBakeResult result)
        {
            var ground = new string[z * z];
            var blocked = new bool[z * z];
            bool anyBlocked = false;

            for (int ly = 0; ly < z; ly++)
                for (int lx = 0; lx < z; lx++)
                {
                    int x = baseX + lx, y = baseY + ly;
                    string swT = grid.TerrainAt(x, y);
                    string seT = grid.TerrainAt(x + 1, y);
                    string neT = grid.TerrainAt(x + 1, y + 1);
                    string nwT = grid.TerrainAt(x, y + 1);

                    int worldX = origin.x + x, worldY = origin.y + y;
                    int hash = unchecked(worldX * 73856093 ^ worldY * 19349663);
                    var sprite = palette.Resolve(swT, seT, neT, nwT, hash, out bool hardCut);
                    if (hardCut) result.HardCuts++;
                    if (sprite == null) result.MissingTiles++;

                    int i = ly * z + lx;
                    ground[i] = palette.NameOf(sprite);

                    if (IsBlocked(grid, s, x, y, swT, seT, neT, nwT))
                    {
                        blocked[i] = true;
                        anyBlocked = true;
                        result.BlockedTiles++;
                    }
                }

            sb.Append("{\"layers\":{\"Ground\":");
            AppendRows(sb, ground, null, z);
            if (anyBlocked)
            {
                sb.Append(",\"Collision\":");
                AppendRows(sb, ground, blocked, z);
            }
            sb.Append("},\"terrains\":[");
            for (int row = 0; row <= z; row++)
            {
                if (row > 0) sb.Append(',');
                sb.Append('[');
                int vy = baseY + (z - row);
                for (int col = 0; col <= z; col++)
                {
                    if (col > 0) sb.Append(',');
                    AppendString(sb, grid.TerrainAt(baseX + col, vy));
                }
                sb.Append(']');
            }
            sb.Append("]}");
        }

        /// <summary>Rows top-first, the overlay convention (row 0 is the zone's highest Y).</summary>
        private static void AppendRows(StringBuilder sb, string[] names, bool[] mask, int z)
        {
            sb.Append('[');
            for (int row = 0; row < z; row++)
            {
                if (row > 0) sb.Append(',');
                sb.Append('[');
                int ly = z - 1 - row;
                for (int lx = 0; lx < z; lx++)
                {
                    if (lx > 0) sb.Append(',');
                    int i = ly * z + lx;
                    AppendString(sb, mask == null || mask[i] ? names[i] : string.Empty);
                }
                sb.Append(']');
            }
            sb.Append(']');
        }

        private static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (c == '"' || c == '\\') sb.Append('\\');
                    sb.Append(c);
                }
            }
            sb.Append('"');
        }

        /// <summary>
        /// The nearest tile to <paramref name="start"/> that is not blocked and whose neighbours are
        /// not either, searched in growing rings. The preview picks the spawn at its own coarse
        /// resolution, where one cell can hold a river or a lake shore; landing the player in
        /// water they cannot leave is the one failure a spawn must never have.
        /// </summary>
        private static Vector2Int FindWalkableTile(WorldTerrainGrid grid, WorldGenSettings s, Vector2Int start,
                                                   int widthTiles, int heightTiles)
        {
            const int MaxRadius = 80;
            for (int r = 0; r <= MaxRadius; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = start.x + dx, y = start.y + dy;
                        if (x < 1 || y < 1 || x >= widthTiles - 1 || y >= heightTiles - 1) continue;
                        if (OpenAround(grid, s, x, y)) return new Vector2Int(x, y);
                    }
            return start;
        }

        private static bool OpenAround(WorldTerrainGrid grid, WorldGenSettings s, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = x + dx, cy = y + dy;
                    if (IsBlocked(grid, s, cx, cy, grid.TerrainAt(cx, cy), grid.TerrainAt(cx + 1, cy),
                                  grid.TerrainAt(cx + 1, cy + 1), grid.TerrainAt(cx, cy + 1)))
                        return false;
                }
            return true;
        }

        private static bool IsBlocked(WorldTerrainGrid grid, WorldGenSettings s, int x, int y,
                                      string sw, string se, string ne, string nw)
        {
            int wet = Wet(sw) + Wet(se) + Wet(ne) + Wet(nw);
            if (wet >= BlockingCorners) return true;

            float mean = (grid.ElevationAt(x, y) + grid.ElevationAt(x + 1, y)
                          + grid.ElevationAt(x + 1, y + 1) + grid.ElevationAt(x, y + 1)) * 0.25f;
            return mean >= s.mountainLevel + PeakMargin && (sw == "rock" || sw == WorldTerrainGrid.Stone);
        }

        private static int Wet(string terrain)
            => terrain == WorldTerrainGrid.Water || terrain == WorldTerrainGrid.WaterDeep
               || terrain == WorldTerrainGrid.Lava ? 1 : 0;
    }
}
