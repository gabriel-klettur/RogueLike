#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Phase 2.6 dev tool: creates the WorldConfig + WorldDescriptor assets
    /// for the "proc_demo" procedural dimension. Unlike <see cref="TestWorldAssetCreator"/>
    /// (which ships JSON overlays), this world is fully procedural — no
    /// StreamingAssets directory is needed. The descriptor opts into chunk
    /// streaming and the biome paints two tiles via the noise-split rule.
    ///
    /// Wire the resulting asset into <c>GameplaySceneSetup.initialWorld</c>
    /// to boot a procedurally-streamed scene end-to-end (provider →
    /// painter → ChunkStreamer → Tilemap).
    ///
    /// Idempotent: running the menu item twice overwrites the existing
    /// assets via <see cref="AssetDatabase.SaveAssets"/>.
    /// </summary>
    public static class ProceduralWorldAssetCreator
    {
        private const string AssetDir   = "Assets/_Project/Data/Worlds";
        private const string ConfigPath = AssetDir + "/ProceduralWorldConfig.asset";
        private const string DescPath   = AssetDir + "/ProceduralWorld.asset";

        public const string Slug         = "proc_demo";
        public const string DisplayName  = "Procedural Demo (chunk-streamed)";
        public const string PrimaryTile   = "tileset3_32_96"; // grass-band sprite from grass_dirt set
        public const string SecondaryTile = "tileset3_64_64"; // dirt-band sprite from grass_dirt set
        public const int    ChunkSize    = 32;
        public const long   Seed         = 42L;
        public const int    ActiveRadius = 2;
        public const float  NoiseThreshold = 0.5f;

        [MenuItem("Valkur/World/Create or Refresh Procedural World Assets", priority = 111)]
        public static void CreateOrRefresh()
        {
            EnsureDirectory();

            var config = AssetDatabase.LoadAssetAtPath<WorldConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<WorldConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            SetField(config, "dimensionSlug", Slug);
            SetField(config, "chunkSize",     ChunkSize);
            SetField(config, "tileSize",      1f);
            SetField(config, "seed",          Seed);
            EditorUtility.SetDirty(config);

            var descriptor = AssetDatabase.LoadAssetAtPath<WorldDescriptor>(DescPath);
            if (descriptor == null)
            {
                descriptor = ScriptableObject.CreateInstance<WorldDescriptor>();
                AssetDatabase.CreateAsset(descriptor, DescPath);
            }
            SetField(descriptor, "slug",              Slug);
            SetField(descriptor, "displayName",       DisplayName);
            SetField(descriptor, "config",            config);
            SetField(descriptor, "defaultSpawnTile",  new Vector2Int(0, 0));
            SetField(descriptor, "useChunkStreaming", true);
            SetField(descriptor, "activeRadius",      ActiveRadius);
            SetField(descriptor, "biomeKind",         ProceduralBiomeKind.NoiseSplit);
            SetField(descriptor, "primaryTile",       PrimaryTile);
            SetField(descriptor, "secondaryTile",     SecondaryTile);
            SetField(descriptor, "noiseThreshold",    NoiseThreshold);
            EditorUtility.SetDirty(descriptor);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ProceduralWorldAssetCreator] Procedural world assets at:\n" +
                      $"  - {ConfigPath}\n  - {DescPath}\n" +
                      $"Wire {DescPath} into GameplaySceneSetup.initialWorld to boot " +
                      "a chunk-streamed scene end-to-end (no StreamingAssets needed).");
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
                Debug.LogError($"[ProceduralWorldAssetCreator] Field '{name}' not found on {obj.GetType().Name}.");
                return;
            }
            f.SetValue(obj, value);
        }
    }
}
#endif
