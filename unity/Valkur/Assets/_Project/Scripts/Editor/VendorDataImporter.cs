using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor tool to import vendor data from Python JSON files into Unity ScriptableObjects.
    /// Sources:
    ///   - python/data/vendors/registry/vendors.json → VendorConfigDefinition SOs
    ///   - python/data/vendors/economy/groups/*.json → EconomyGroupDefinition SOs
    ///
    /// Menu: Valkur > Vendors > Import Economy Groups / Import Vendor Registry
    /// </summary>
    public static partial class VendorDataImporter
    {
        private const string ECONOMY_GROUPS_PATH = "python/data/vendors/economy/groups";
        private const string VENDORS_REGISTRY_PATH = "python/data/vendors/registry/vendors.json";
        private const string ECONOMY_OUTPUT = "Assets/_Project/Data/Vendor/EconomyGroups";
        private const string VENDOR_OUTPUT = "Assets/_Project/Data/Vendor/Configs";

        [MenuItem("Valkur/Vendors/Import Economy Groups from Python JSON")]
        public static void ImportEconomyGroups()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string groupsDir = Path.Combine(projectRoot, ECONOMY_GROUPS_PATH);

            if (!Directory.Exists(groupsDir))
            {
                Debug.LogWarning($"[VendorDataImporter] Economy groups directory not found: {groupsDir}");
                return;
            }

            EnsureDirectory(ECONOMY_OUTPUT);

            int count = 0;
            foreach (string file in Directory.GetFiles(groupsDir, "*.json"))
            {
                string json = File.ReadAllText(file);
                string groupKey = Path.GetFileNameWithoutExtension(file);
                var so = ImportEconomyGroup(groupKey, json);
                if (so != null) count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[VendorDataImporter] Imported {count} economy groups.");
        }

        [MenuItem("Valkur/Vendors/Import Vendor Registry from Python JSON")]
        public static void ImportVendorRegistry()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string registryFile = Path.Combine(projectRoot, VENDORS_REGISTRY_PATH);

            if (!File.Exists(registryFile))
            {
                Debug.LogWarning($"[VendorDataImporter] Vendor registry not found: {registryFile}");
                return;
            }

            EnsureDirectory(VENDOR_OUTPUT);

            string json = File.ReadAllText(registryFile);
            var root = ParseJsonDict(json);
            if (!root.TryGetValue("vendors", out object vendorsObj))
            {
                Debug.LogWarning("[VendorDataImporter] No 'vendors' key in registry JSON.");
                return;
            }

            int count = 0;
            if (vendorsObj is Dictionary<string, object> vendors)
            {
                foreach (var kvp in vendors)
                {
                    string vendorKey = kvp.Key;
                    if (kvp.Value is Dictionary<string, object> entry)
                    {
                        var so = ImportVendorConfig(vendorKey, entry);
                        if (so != null) count++;
                    }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[VendorDataImporter] Imported {count} vendor configs.");
        }

        [MenuItem("Valkur/Vendors/Copy Collision Data to StreamingAssets")]
        public static void CopyCollisionData()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));

            string[] sources =
            {
                "python/data/buildings/buildings_collisions_by_image.json",
                "python/data/worlds/base/buildings/buildings_collisions_by_building_instance_id.json",
                "python/data/worlds/base/buildings/buildings_collisions_by_spawn_id.json"
            };

            string destDir = Path.Combine(Application.streamingAssetsPath, "Buildings");
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            int count = 0;
            foreach (string srcRel in sources)
            {
                string srcFull = Path.Combine(projectRoot, srcRel);
                if (!File.Exists(srcFull))
                {
                    Debug.LogWarning($"[VendorDataImporter] Source not found: {srcFull}");
                    continue;
                }
                string destFile = Path.Combine(destDir, Path.GetFileName(srcRel));
                File.Copy(srcFull, destFile, true);
                count++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[VendorDataImporter] Copied {count} collision data files to StreamingAssets/Buildings/.");
        }

        // ImportEconomyGroup, ParseMarginEntry, ImportVendorConfig,
        // EnsureDirectory, ParseFloat, ParseInt, ParseJsonDict → VendorDataImporter.Parsers.cs
    }
}
