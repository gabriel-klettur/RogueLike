#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor tool that imports zone data from the Python project into Unity StreamingAssets.
    /// Reads zones.json, copies overlay files, collision files, building instances,
    /// spawner instances, particle instances, and light instances.
    ///
    /// Menu: Valkur > Migration > Import World Zones from Python
    /// </summary>
    public static class WorldZoneImporter
    {
        private const string PYTHON_DATA = "python/data";
        private const string PYTHON_WORLDS = "python/data/worlds/base";

        [MenuItem("Valkur/Migration/Import World Zones from Python")]
        public static void ImportWorldZones()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string pythonWorldsDir = Path.Combine(projectRoot, PYTHON_WORLDS);
            string streamingAssets = Application.streamingAssetsPath;

            if (!Directory.Exists(pythonWorldsDir))
            {
                EditorUtility.DisplayDialog("Import Error",
                    $"Python worlds directory not found:\n{pythonWorldsDir}", "OK");
                return;
            }

            int copied = 0;
            int skipped = 0;

            // 1) Copy zone overlays
            string overlaysDir = Path.Combine(pythonWorldsDir, "zones/overlays");
            string mapsDir = Path.Combine(streamingAssets, "Maps");
            EnsureDirectory(mapsDir);
            copied += CopyJsonFiles(overlaysDir, mapsDir, "*.overlay.json", ref skipped);

            // 2) Generate zones_database.json from zones.json
            string zonesJsonPath = Path.Combine(pythonWorldsDir, "zones/zones.json");
            if (File.Exists(zonesJsonPath))
            {
                GenerateZonesDatabase(zonesJsonPath, overlaysDir,
                    Path.Combine(pythonWorldsDir, "collisions"), mapsDir);
                Debug.Log("[WorldZoneImporter] Generated zones_database.json");
            }

            // 3) Copy collision grids
            string collisionsDir = Path.Combine(pythonWorldsDir, "collisions");
            string collisionsDst = Path.Combine(streamingAssets, "Collisions");
            EnsureDirectory(collisionsDst);
            copied += CopyJsonFiles(collisionsDir, collisionsDst, "*.json", ref skipped);

            // 4) Copy building instances (full file with all zones)
            //    SAFETY: Unity's buildings_instances.json is the authoritative source because
            //    buildings are edited in-engine via BuildingsRuntimeEditor. We only copy from
            //    Python if the Unity file does not exist yet; otherwise we skip and warn.
            string buildingsSrc = Path.Combine(pythonWorldsDir, "buildings/buildings_instances.json");
            string buildingsDst = Path.Combine(streamingAssets, "Buildings/buildings_instances.json");
            EnsureDirectory(Path.GetDirectoryName(buildingsDst));
            if (File.Exists(buildingsSrc))
            {
                if (File.Exists(buildingsDst))
                {
                    skipped++;
                    Debug.LogWarning("[WorldZoneImporter] SKIPPED buildings_instances.json — Unity file " +
                                     "already exists and is the authoritative source (edited in-engine).\n" +
                                     "To force-overwrite run: Valkur > Migration > Import Buildings from Python JSON");
                }
                else
                {
                    File.Copy(buildingsSrc, buildingsDst, overwrite: false);
                    BuildingsDataGuard.RefreshBackup();
                    copied++;
                    Debug.Log("[WorldZoneImporter] buildings_instances.json copied from Python (Unity file was absent).");
                }
            }

            // 5) Copy spawner instances
            string spawnersSrc = Path.Combine(pythonWorldsDir, "spawners/spawners_instances.json");
            string spawnersDst = Path.Combine(streamingAssets, "Spawners/spawners_instances.json");
            EnsureDirectory(Path.GetDirectoryName(spawnersDst));
            if (File.Exists(spawnersSrc))
            {
                File.Copy(spawnersSrc, spawnersDst, overwrite: true);
                copied++;
            }

            // 6) Copy particle instances
            string particlesSrc = Path.Combine(pythonWorldsDir, "particles/particles_instances.json");
            string particlesDst = Path.Combine(streamingAssets, "Particles/particles_instances.json");
            EnsureDirectory(Path.GetDirectoryName(particlesDst));
            if (File.Exists(particlesSrc))
            {
                File.Copy(particlesSrc, particlesDst, overwrite: true);
                copied++;
            }

            // 7) Copy light instances
            string pythonData = Path.Combine(projectRoot, PYTHON_DATA);
            string lightsSrc = Path.Combine(pythonData, "light/light_instances.json");
            string lightsDst = Path.Combine(streamingAssets, "Lights/light_instances.json");
            EnsureDirectory(Path.GetDirectoryName(lightsDst));
            if (File.Exists(lightsSrc))
            {
                File.Copy(lightsSrc, lightsDst, overwrite: true);
                copied++;
            }

            AssetDatabase.Refresh();

            string msg = $"World zone import complete.\nFiles copied: {copied}\nSkipped (already up-to-date): {skipped}";
            Debug.Log($"[WorldZoneImporter] {msg}");
            EditorUtility.DisplayDialog("Import Complete", msg, "OK");
        }

        /// <summary>
        /// Generate zones_database.json from Python zones.json, matching overlay and
        /// collision filenames to each zone entry.
        /// </summary>
        private static void GenerateZonesDatabase(string zonesJsonPath, string overlaysDir,
            string collisionsDir, string outputDir)
        {
            string json = File.ReadAllText(zonesJsonPath);
            var raw = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (raw == null) return;

            // Discover available overlay and collision files
            var overlayFiles = new HashSet<string>();
            if (Directory.Exists(overlaysDir))
            {
                foreach (var f in Directory.GetFiles(overlaysDir, "*.overlay.json"))
                    overlayFiles.Add(Path.GetFileName(f));
            }

            var collisionFiles = new HashSet<string>();
            if (Directory.Exists(collisionsDir))
            {
                foreach (var f in Directory.GetFiles(collisionsDir, "*.json"))
                    collisionFiles.Add(Path.GetFileName(f));
            }

            // Build zone entries
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"zone_width_tiles\": 50,");
            sb.AppendLine("  \"zone_height_tiles\": 50,");

            // Compute world origin (minimum offsets)
            int minX = int.MaxValue, minY = int.MaxValue;
            foreach (var kvp in raw)
            {
                var arr = kvp.Value as List<object>;
                if (arr == null || arr.Count < 2) continue;
                int ox = System.Convert.ToInt32(arr[0]);
                int oy = System.Convert.ToInt32(arr[1]);
                if (ox < minX) minX = ox;
                if (oy < minY) minY = oy;
            }

            sb.AppendLine($"  \"world_origin_x\": {minX},");
            sb.AppendLine($"  \"world_origin_y\": {minY},");
            sb.AppendLine("  \"zones\": [");

            int idx = 0;
            int count = raw.Count;
            foreach (var kvp in raw)
            {
                string zoneName = kvp.Key;
                var arr = kvp.Value as List<object>;
                if (arr == null || arr.Count < 2) continue;

                int offX = System.Convert.ToInt32(arr[0]);
                int offY = System.Convert.ToInt32(arr[1]);

                // Match overlay file (try zone name variants)
                string overlayFile = FindMatchingFile(overlayFiles, zoneName, ".overlay.json");
                string collisionFile = FindMatchingFile(collisionFiles, zoneName, ".json");

                string ovStr = overlayFile != null ? $"\"{overlayFile}\"" : "null";
                string colStr = collisionFile != null ? $"\"{collisionFile}\"" : "null";

                string comma = (idx < count - 1) ? "," : "";
                sb.AppendLine($"    {{ \"name\": \"{zoneName}\", \"offset_x\": {offX}, \"offset_y\": {offY}, \"overlay\": {ovStr}, \"collision\": {colStr} }}{comma}");
                idx++;
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            string outPath = Path.Combine(outputDir, "zones_database.json");
            File.WriteAllText(outPath, sb.ToString());
        }

        private static string FindMatchingFile(HashSet<string> files, string zoneName, string suffix)
        {
            // Exact match
            string candidate = zoneName + suffix;
            if (files.Contains(candidate)) return candidate;

            // Try lowercase
            candidate = zoneName.ToLowerInvariant() + suffix;
            foreach (var f in files)
            {
                if (string.Equals(f, candidate, System.StringComparison.OrdinalIgnoreCase))
                    return f;
            }

            return null;
        }

        private static int CopyJsonFiles(string srcDir, string dstDir, string pattern, ref int skipped)
        {
            if (!Directory.Exists(srcDir)) return 0;
            int count = 0;
            foreach (var file in Directory.GetFiles(srcDir, pattern))
            {
                string destFile = Path.Combine(dstDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
                count++;
            }
            return count;
        }

        private static void EnsureDirectory(string dir)
        {
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
#endif
