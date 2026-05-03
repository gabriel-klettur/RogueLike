using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Editor
{
    /// <summary>
    /// Imports building data from the Python project into Unity.
    ///
    /// Menu: Valkur > Migration > Import Buildings from Python JSON
    ///
    /// What it does:
    ///  1. Reads python/data/buildings/buildings_templates.json → creates BuildingTemplateData SOs
    ///  2. Copies building sprites from python/assets/buildings/ → Resources/Buildings/
    ///     (sanitizes filenames: spaces in Python names become '_' in Unity)
    ///  3. Creates / refreshes BuildingCatalog.asset
    ///  4. Generates StreamingAssets/Buildings/buildings_instances.json from
    ///     python/data/worlds/base/buildings/buildings_instances.json,
    ///     filtering to zone=Lobby and normalising zone casing.
    ///
    /// Maps to Python's buildings_templates.json + worlds/base/buildings/buildings_instances.json.
    /// </summary>
    public static partial class BuildingImporter
    {
        // ── Paths (relative to Application.dataPath = ".../Valkur/Assets") ──────────
        private const string PYTHON_ROOT          = "../../../python";          // python/ at repo root
        private const string TEMPLATES_JSON       = "data/buildings/buildings_templates.json";
        // Per-world instances (lobby lives in worlds/base)
        private const string INSTANCES_JSON_WORLD = "data/worlds/base/buildings/buildings_instances.json";
        private const string PYTHON_ASSETS_ROOT   = "assets/buildings";        // python/assets/buildings/

        private const string SO_OUTPUT_DIR        = "Assets/_Project/Data/Catalogs/Buildings";
        private const string CATALOG_ASSET_PATH   = "Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset";
        private const string RESOURCES_DIR        = "Assets/_Project/Resources/Buildings";// physical
        private const string STREAMING_DIR_NAME   = "Buildings";               // under StreamingAssets

        // ── Entry points ─────────────────────────────────────────────────────────────

        [MenuItem("Valkur/Migration/Import Buildings from Python JSON")]
        public static void ImportBuildings() => ImportBuildings(dryRun: false);

        [MenuItem("Valkur/Migration/Import Buildings from Python JSON (Dry-Run)")]
        public static void ImportBuildingsDryRun() => ImportBuildings(dryRun: true);

        /// <summary>
        /// Adds a BuildingLoader GameObject to the active scene (if none exists)
        /// and wires it to the BuildingCatalog at CATALOG_ASSET_PATH.
        /// Run AFTER "Import Buildings from Python JSON".
        /// </summary>
        [MenuItem("Valkur/Migration/Setup BuildingLoader in Scene")]
        public static void SetupBuildingLoaderInScene()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(CATALOG_ASSET_PATH);
            if (catalog == null)
            {
                EditorUtility.DisplayDialog("Setup BuildingLoader",
                    $"BuildingCatalog not found at {CATALOG_ASSET_PATH}.\n" +
                    "Run 'Import Buildings from Python JSON' first.", "OK");
                return;
            }

            // Re-use existing or create new
            var existing = UnityEngine.Object.FindObjectOfType<BuildingLoader>();
            if (existing != null)
            {
                Debug.Log("[BuildingImporter] BuildingLoader already in scene.");
                EditorUtility.DisplayDialog("Setup BuildingLoader",
                    $"BuildingLoader already exists on '{existing.gameObject.name}'.\n" +
                    "Nothing changed.", "OK");
                return;
            }

            var go = new GameObject("BuildingLoader");
            var loader = go.AddComponent<BuildingLoader>();

            // Wire catalog via SerializedObject so Undo works
            var so = new SerializedObject(loader);
            so.FindProperty("_catalog").objectReferenceValue = catalog;
            so.ApplyModifiedProperties();

            Undo.RegisterCreatedObjectUndo(go, "Create BuildingLoader");
            EditorUtility.SetDirty(go);
            EditorSceneManager.MarkSceneDirty(go.scene);

            Debug.Log("[BuildingImporter] Created BuildingLoader and wired BuildingCatalog.");
            EditorUtility.DisplayDialog("Setup BuildingLoader",
                "BuildingLoader added to scene and catalog wired.\nSave the scene to persist.", "OK");
        }

        public static MigrationReport ImportBuildings(bool dryRun)
        {
            var report = new MigrationReport();
            string label = dryRun ? "DRY-RUN" : "IMPORT";

            try
            {
                ImportTemplates(dryRun, report);
                CopyInstances(dryRun, report);
            }
            catch (Exception ex)
            {
                report.AddError("BuildingImporter", "-", $"Unhandled exception: {ex.Message}");
            }

            if (!dryRun)
                AssetDatabase.SaveAssets();

            report.PrintToConsole($"Buildings ({label})");
            return report;
        }

    }
}
