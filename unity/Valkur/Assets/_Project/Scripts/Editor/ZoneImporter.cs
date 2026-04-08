using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Editor
{
    /// <summary>
    /// Reads python/data/worlds/base/zones/zones.json and populates the
    /// ZoneManager component in the currently open scene.
    ///
    /// Menu: Valkur > Migration > Import Zones from Python JSON
    ///
    /// If no ZoneManager exists in the scene a new GameObject named
    /// "WorldManager" is created and a ZoneManager component is attached.
    ///
    /// Python zones.json format (dict):
    ///   { "Lobby": [50, 50], "Forest": [0, 50], ... }
    ///   where the value is [gridOffsetX, gridOffsetY] in tiles.
    ///
    /// Maps to Python's zones.json used by ZoneManager + MapManager.
    /// </summary>
    public static class ZoneImporter
    {
        private const string PYTHON_ROOT  = "../../../python";
        private const string ZONES_JSON   = "data/worlds/base/zones/zones.json";

        // ── Entry points ─────────────────────────────────────────────────────────

        [MenuItem("Valkur/Migration/Import Zones from Python JSON")]
        public static void ImportZones()
        {
            string zonesPath = FullPythonPath(ZONES_JSON);
            if (!File.Exists(zonesPath))
            {
                EditorUtility.DisplayDialog("Zone Importer",
                    $"zones.json not found:\n{zonesPath}", "OK");
                return;
            }

            var manager = FindOrCreateZoneManager();
            if (manager == null)
            {
                EditorUtility.DisplayDialog("Zone Importer",
                    "Failed to find or create ZoneManager in the active scene.", "OK");
                return;
            }

            string json = File.ReadAllText(zonesPath);
            var zoneDict = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (zoneDict == null)
            {
                EditorUtility.DisplayDialog("Zone Importer",
                    "Failed to parse zones.json. Expected a JSON object.", "OK");
                return;
            }

            var zoneDefs = new List<ZoneManager.ZoneDefinition>();
            foreach (var kv in zoneDict)
            {
                var offsetList = kv.Value as List<object>;
                if (offsetList == null || offsetList.Count < 2)
                {
                    Debug.LogWarning($"[ZoneImporter] Skipping zone '{kv.Key}': invalid offset format.");
                    continue;
                }

                int ox = Convert.ToInt32(offsetList[0]);
                int oy = Convert.ToInt32(offsetList[1]);

                zoneDefs.Add(new ZoneManager.ZoneDefinition
                {
                    zoneName            = kv.Key,
                    gridOffset          = new Vector2Int(ox, oy),
                    zoneMusic           = null,
                    editableInTileEditor = true,
                });
            }

            manager.ReplaceZones(zoneDefs);
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

            Debug.Log($"[ZoneImporter] Imported {zoneDefs.Count} zones into {manager.gameObject.name}.");
            EditorUtility.DisplayDialog("Zone Importer",
                $"Imported {zoneDefs.Count} zones successfully.\nSave the scene to persist.", "OK");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static ZoneManager FindOrCreateZoneManager()
        {
            var existing = UnityEngine.Object.FindObjectOfType<ZoneManager>();
            if (existing != null)
                return existing;

            // None in scene — create one
            var go = new GameObject("WorldManager");
            var zm = go.AddComponent<ZoneManager>();
            Undo.RegisterCreatedObjectUndo(go, "Create ZoneManager");
            Debug.Log("[ZoneImporter] Created new WorldManager with ZoneManager component.");
            return zm;
        }

        private static string FullPythonPath(string relativeToPythonRoot)
        {
            return Path.GetFullPath(
                Path.Combine(Application.dataPath, PYTHON_ROOT, relativeToPythonRoot));
        }
    }
}
