#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Phase 1.5 dev tool: creates the WorldConfig + WorldDescriptor assets
    /// for the secondary "test_world" demo dimension. The companion
    /// StreamingAssets/Worlds/test_world/Maps/ directory ships JSON files
    /// (zones_database.json + test_zone.overlay.json) that paint a single
    /// 50x50 zone fully covered by the <c>dungeon_floor</c> tile.
    ///
    /// Wired to the menu so a designer can rebuild the assets after
    /// editing this script. Idempotent: running it twice overwrites the
    /// existing assets via <see cref="AssetDatabase.SaveAssets"/>.
    /// </summary>
    public static class TestWorldAssetCreator
    {
        private const string AssetDir   = "Assets/_Project/Data/Worlds";
        private const string ConfigPath = AssetDir + "/TestWorldConfig.asset";
        private const string DescPath   = AssetDir + "/TestWorld.asset";

        public const string TestWorldSlug = "test_world";
        public const string TestWorldDisplayName = "Test World (single dungeon zone)";

        [MenuItem("Valkur/World/Create or Refresh Test World Assets", priority = 110)]
        public static void CreateOrRefresh()
        {
            EnsureDirectory();

            var config = AssetDatabase.LoadAssetAtPath<WorldConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<WorldConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            // Slug + chunk size set via reflection to avoid coupling to a
            // setter that does not exist on the asset.
            SetField(config, "dimensionSlug", TestWorldSlug);
            SetField(config, "chunkSize",     50);
            SetField(config, "tileSize",      1f);
            SetField(config, "seed",          -1L);
            EditorUtility.SetDirty(config);

            var descriptor = AssetDatabase.LoadAssetAtPath<WorldDescriptor>(DescPath);
            if (descriptor == null)
            {
                descriptor = ScriptableObject.CreateInstance<WorldDescriptor>();
                AssetDatabase.CreateAsset(descriptor, DescPath);
            }
            SetField(descriptor, "slug",            TestWorldSlug);
            SetField(descriptor, "displayName",     TestWorldDisplayName);
            SetField(descriptor, "config",          config);
            SetField(descriptor, "defaultSpawnTile", new Vector2Int(25, 25));
            EditorUtility.SetDirty(descriptor);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[TestWorldAssetCreator] Test world assets at:\n" +
                      $"  - {ConfigPath}\n  - {DescPath}\n" +
                      $"StreamingAssets/Worlds/{TestWorldSlug}/Maps must contain " +
                      "zones_database.json + test_zone.overlay.json (already shipped).");
        }

        private static void EnsureDirectory()
        {
            if (!Directory.Exists(AssetDir))
            {
                Directory.CreateDirectory(AssetDir);
                AssetDatabase.Refresh();
            }
        }

        private static void SetField(object obj, string name, object value)
        {
            var f = obj.GetType().GetField(name,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f == null)
            {
                Debug.LogError($"[TestWorldAssetCreator] Field '{name}' not found on {obj.GetType().Name}.");
                return;
            }
            f.SetValue(obj, value);
        }
    }
}
#endif
