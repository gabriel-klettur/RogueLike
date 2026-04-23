using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    public static partial class BuildingImporter
    {
        // ── Template import ──────────────────────────────────────────────────────────

        private static void ImportTemplates(bool dryRun, MigrationReport report)
        {
            string templatesPath = FullPythonPath(TEMPLATES_JSON);
            if (!File.Exists(templatesPath))
            {
                report.AddError("buildings_templates.json", "-", $"File not found: {templatesPath}");
                return;
            }

            string json = File.ReadAllText(templatesPath);
            var rawList = MiniJson.Deserialize(json) as List<object>;
            if (rawList == null)
            {
                report.AddError("buildings_templates.json", "-", "Failed to parse JSON array.");
                return;
            }

            // Ensure output folders
            if (!dryRun)
            {
                EnsureFolder("Assets/_Project/Data/Catalogs", "Buildings");
                EnsureFolder("Assets/_Project/Resources",     "Buildings");
            }

            // Load or create the catalog
            BuildingCatalog catalog = LoadOrCreateCatalog(dryRun);

            int imported = 0;
            foreach (var item in rawList)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null) continue;

                int    id         = GetInt(dict, "id");
                bool   solid      = GetBool(dict, "solid", true);
                float  splitRatio = GetFloat(dict, "split_ratio", 0.5f);
                string scope      = GetString(dict, "collider_scope", "CG");
                string idlePath   = "";   // e.g. "assets/buildings/vegetation/tree_1.png"

                if (dict.TryGetValue("assets", out var assetsRaw) &&
                    assetsRaw is Dictionary<string, object> assets)
                {
                    idlePath = GetString(assets, "idle", "");
                }

                Vector2Int origScale = Vector2Int.zero;
                if (dict.TryGetValue("original_scale", out var scaleRaw) &&
                    scaleRaw is List<object> scaleList && scaleList.Count >= 2)
                {
                    origScale = new Vector2Int(
                        Convert.ToInt32(scaleList[0]),
                        Convert.ToInt32(scaleList[1]));
                }

                if (string.IsNullOrEmpty(idlePath))
                {
                    report.AddWarning("buildings_templates.json", $"id={id}", "Missing 'assets.idle' path — skipped.");
                    continue;
                }

                // Map Python asset path → Unity Resources path
                // "assets/buildings/vegetation/tree_1.png" → "Buildings/vegetation/tree_1"
                string resourcesRelPath = PythonAssetPathToResourcesPath(idlePath); // e.g. "Buildings/vegetation/tree_1"
                string unityAssetPath   = $"{RESOURCES_DIR}/{ResourcesPathToRelative(resourcesRelPath)}.png";
                string pythonSrcPath    = FullPythonAssetPath(idlePath);

                // Copy sprite if needed
                if (!dryRun)
                {
                    if (!CopyBuildingSprite(pythonSrcPath, unityAssetPath, report, id))
                        continue;
                }
                else if (!File.Exists(pythonSrcPath))
                {
                    report.AddWarning("buildings_templates.json", $"id={id}",
                        $"Source sprite not found: {pythonSrcPath}");
                }

                if (dryRun)
                {
                    report.AddOk("buildings_templates.json", $"id={id}",
                        $"Would create BuildingTemplate SO with assetPath='{resourcesRelPath}'");
                    imported++;
                    continue;
                }

                // Create or update the ScriptableObject
                string soPath = $"{SO_OUTPUT_DIR}/BuildingTemplate_{id}.asset";
                BuildingTemplateData so = AssetDatabase.LoadAssetAtPath<BuildingTemplateData>(soPath);
                if (so == null)
                {
                    so = ScriptableObject.CreateInstance<BuildingTemplateData>();
                    AssetDatabase.CreateAsset(so, soPath);
                }

                so.templateId    = id;
                so.assetPath     = resourcesRelPath;
                so.solid         = solid;
                so.splitRatio    = splitRatio;
                so.colliderScope = scope;
                so.originalScale = origScale;
                so.sourceImagePath = idlePath;

                // Assign preview sprite (the freshly imported asset)
                AssetDatabase.ImportAsset(unityAssetPath, ImportAssetOptions.ForceSynchronousImport);
                var previewSprite = AssetDatabase.LoadAssetAtPath<Sprite>(unityAssetPath);
                if (previewSprite != null)
                    so.previewSprite = previewSprite;
                else
                    report.AddWarning("buildings_templates.json", $"id={id}",
                        $"Sprite not loaded from {unityAssetPath} — preview will be blank.");

                EditorUtility.SetDirty(so);

                if (catalog != null)
                    catalog.UpsertTemplate(so);

                report.AddOk("buildings_templates.json", $"id={id}",
                    $"Template SO created/updated at {soPath}");
                imported++;
            }

            if (!dryRun && catalog != null)
            {
                EditorUtility.SetDirty(catalog);
                report.AddOk("BuildingCatalog", "-", $"Catalog updated with {imported} templates.");
            }

            Debug.Log($"[BuildingImporter] {imported}/{rawList.Count} templates processed.");
        }

        // ── Instance copy ────────────────────────────────────────────────────────────

        private static void CopyInstances(bool dryRun, MigrationReport report)
        {
            string src = FullPythonPath(INSTANCES_JSON_WORLD);
            if (!File.Exists(src))
            {
                report.AddWarning("buildings_instances.json", "-",
                    $"Source not found: {src} — skipping instances copy.");
                return;
            }

            string streamingDir = Path.Combine(Application.streamingAssetsPath, STREAMING_DIR_NAME);
            string destPath     = Path.Combine(streamingDir, "buildings_instances.json");

            if (dryRun)
            {
                report.AddOk("buildings_instances.json", "-",
                    $"Would copy all-zone instances JSON to {destPath}");
                return;
            }

            // SAFETY: Unity's buildings_instances.json is the authoritative source.
            // The importer only writes if the Unity file does not already exist.
            // If it exists, log a warning and skip — use BuildingsRuntimeEditor to edit in-engine.
            if (File.Exists(destPath))
            {
                report.AddWarning("buildings_instances.json", "-",
                    $"SKIPPED: Unity file already exists at {destPath}. " +
                    "It is the authoritative source (edited in-engine via BuildingsRuntimeEditor). " +
                    "Delete the file manually and re-run if you really want to import from Python.");
                return;
            }

            if (!Directory.Exists(streamingDir))
                Directory.CreateDirectory(streamingDir);

            // Read world instances, keep all zones, clean up, write.
            string rawJson = File.ReadAllText(src);
            var allInstances = MiniJson.Deserialize(rawJson) as List<object>;
            if (allInstances == null)
            {
                report.AddError("buildings_instances.json", "-", "Failed to parse world instances JSON.");
                return;
            }

            var cleanedInstances = new List<object>();
            foreach (var item in allInstances)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null) continue;

                // Strip Unity-irrelevant overrides (z_bottom, z_top, z)
                if (dict.TryGetValue("overrides", out var ovRaw) &&
                    ovRaw is Dictionary<string, object> overrides)
                {
                    overrides.Remove("z_bottom");
                    overrides.Remove("z_top");
                    overrides.Remove("z");
                }

                cleanedInstances.Add(dict);
            }

            string outJson = MiniJson.Serialize(cleanedInstances);
            File.WriteAllText(destPath, outJson);
            BuildingsDataGuard.RefreshBackup();
            AssetDatabase.Refresh();
            report.AddOk("buildings_instances.json", "-",
                $"Copied {cleanedInstances.Count} building instances (all zones) to {destPath}");
        }
    }
}
