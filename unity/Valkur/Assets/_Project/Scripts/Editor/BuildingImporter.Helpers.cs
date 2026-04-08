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
        // ── Helpers ──────────────────────────────────────────────────────────────────

        private static bool CopyBuildingSprite(
            string srcPath, string destUnityPath, MigrationReport report, int templateId)
        {
            if (!File.Exists(srcPath))
            {
                report.AddWarning("buildings_templates.json", $"id={templateId}",
                    $"Source sprite not found: {srcPath}");
                return false;
            }

            string destFull = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", destUnityPath));

            string destDir = Path.GetDirectoryName(destFull);
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(srcPath, destFull, overwrite: true);
            return true;
        }

        private static BuildingCatalog LoadOrCreateCatalog(bool dryRun)
        {
            if (dryRun) return null;

            var existing = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(CATALOG_ASSET_PATH);
            if (existing != null) return existing;

            var catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
            AssetDatabase.CreateAsset(catalog, CATALOG_ASSET_PATH);
            return catalog;
        }

        private static void EnsureFolder(string parentPath, string folderName)
        {
            string fullPath = $"{parentPath}/{folderName}";
            if (!AssetDatabase.IsValidFolder(fullPath))
                AssetDatabase.CreateFolder(parentPath, folderName);
        }

        /// <summary>
        /// "assets/buildings/vegetation/tree_1.png" → "Buildings/vegetation/tree_1"
        /// (Resources-relative path without extension).
        /// Spaces in filenames are replaced with '_' to match the sanitized copies in Resources.
        /// </summary>
        private static string PythonAssetPathToResourcesPath(string pythonPath)
        {
            // Strip leading "assets/buildings/" prefix
            const string prefix = "assets/buildings/";
            string relative = pythonPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? pythonPath.Substring(prefix.Length)
                : pythonPath;

            // Remove extension
            string withoutExt = Path.ChangeExtension(relative, null);
            // Normalize path separators
            withoutExt = withoutExt.Replace('\\', '/');
            // Sanitize filename: spaces → underscore (matches the copy step)
            withoutExt = SanitizeAssetPath(withoutExt);
            return "Buildings/" + withoutExt;
        }

        /// <summary>
        /// Replaces spaces with underscores in every path segment filename portion.
        /// Directory separators are preserved.
        /// </summary>
        private static string SanitizeAssetPath(string path)
        {
            // Replace space with underscore throughout the entire relative path.
            return path.Replace(' ', '_');
        }

        /// <summary>
        /// "Buildings/vegetation/tree_1" → "vegetation/tree_1"
        /// </summary>
        private static string ResourcesPathToRelative(string resourcesPath)
        {
            const string prefix = "Buildings/";
            return resourcesPath.StartsWith(prefix) ? resourcesPath.Substring(prefix.Length) : resourcesPath;
        }

        private static string FullPythonPath(string relativeToPythonRoot)
        {
            return Path.GetFullPath(
                Path.Combine(Application.dataPath, PYTHON_ROOT, relativeToPythonRoot));
        }

        private static string FullPythonAssetPath(string assetPathFromProjectRoot)
        {
            // e.g. "assets/buildings/vegetation/tree_1.png" → full path
            return Path.GetFullPath(
                Path.Combine(Application.dataPath, PYTHON_ROOT, assetPathFromProjectRoot));
        }

        // ── JSON helpers ─────────────────────────────────────────────────────────────

        private static int GetInt(Dictionary<string, object> d, string key, int fallback = 0)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToInt32(v);
            return fallback;
        }

        private static float GetFloat(Dictionary<string, object> d, string key, float fallback = 0f)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToSingle(v);
            return fallback;
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool fallback = false)
        {
            if (d.TryGetValue(key, out var v) && v is bool b) return b;
            if (d.TryGetValue(key, out var v2) && v2 != null)
                return Convert.ToBoolean(v2);
            return fallback;
        }

        private static string GetString(Dictionary<string, object> d, string key, string fallback = "")
        {
            if (d.TryGetValue(key, out var v) && v is string s) return s;
            return fallback;
        }
    }
}
