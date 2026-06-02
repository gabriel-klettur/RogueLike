using UnityEditor;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// One-shot menu to apply the new <see cref="TextureImporterCompression.CompressedHQ"/>
    /// policy to every PNG under <c>Resources/Buildings/</c>. Use after editing
    /// <see cref="ValkurAssetPostprocessor"/>'s per-folder compression rule —
    /// changing the postprocessor only affects newly-imported assets, so the
    /// ~95 existing Building PNGs need an explicit reimport to flip from
    /// Uncompressed → CompressedHQ.
    ///
    /// Background: Building source PNGs are 1024×1536 RGBA. Under the old
    /// "Uncompressed everywhere" policy each one consumed ~6 MB of VRAM.
    /// With 100+ building instances in the world this alone reached ~600 MB
    /// VRAM, which (combined with the synchronous Resources.Load chain in
    /// <see cref="World.BuildingLoader"/>) is the most likely cause of the
    /// 10-15 s boot freeze and the sustained 28-32 FPS reported in #perf.
    /// CompressedHQ (BC7/DXT5) reduces this to ~75 MB total VRAM with no
    /// visible quality loss at the zoom range Buildings are viewed at.
    ///
    /// Menu: <c>Valkur &gt; Optimize &gt; Compress Building Textures</c>.
    /// </summary>
    public static class BuildingCompressionOptimizer
    {
        private static readonly string[] BuildingFolders =
        {
            "Assets/_Project/Resources/Buildings",
        };

        private static readonly string[] AllPlatforms =
        {
            "Standalone", "WebGL", "Android", "iPhone",
        };

        [MenuItem("Valkur/Optimize/Compress Building Textures")]
        public static void CompressBuildings()
        {
            int totalConverted = 0;
            int totalSkipped   = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var folder in BuildingFolders)
                {
                    if (!AssetDatabase.IsValidFolder(folder)) continue;

                    var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
                    foreach (var guid in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (ConvertOne(path)) totalConverted++;
                        else totalSkipped++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log(
                $"[BuildingCompressionOptimizer] Done. {totalConverted} texture(s) " +
                $"flipped to CompressedHQ (Standalone/WebGL/Android/iPhone). " +
                $"{totalSkipped} skipped (no TextureImporter).");
        }

        [MenuItem("Valkur/Optimize/Audit Building Textures")]
        public static void AuditBuildings()
        {
            int uncompressedCount = 0;
            int compressedCount   = 0;
            long uncompressedBytes = 0;

            foreach (var folder in BuildingFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null) continue;

                    var ps = importer.GetPlatformTextureSettings("Standalone");
                    bool isUncompressed = ps != null && ps.overridden &&
                        ps.textureCompression == TextureImporterCompression.Uncompressed;

                    if (isUncompressed)
                    {
                        uncompressedCount++;
                        var fi = new System.IO.FileInfo(path);
                        if (fi.Exists) uncompressedBytes += fi.Length;
                    }
                    else compressedCount++;
                }
            }

            Debug.Log(
                $"[BuildingCompressionOptimizer] Audit — " +
                $"Uncompressed: {uncompressedCount} (source PNG total: " +
                $"{uncompressedBytes / (1024 * 1024)} MB), " +
                $"Compressed: {compressedCount}. " +
                $"Run 'Compress Building Textures' to flip the Uncompressed ones.");
        }

        private static bool ConvertOne(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return false;

            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            foreach (var platform in AllPlatforms)
            {
                var ps = importer.GetPlatformTextureSettings(platform);
                if (ps == null) continue;
                if (ps.overridden &&
                    ps.textureCompression == TextureImporterCompression.CompressedHQ)
                    continue;
                ps.overridden         = true;
                ps.textureCompression = TextureImporterCompression.CompressedHQ;
                ps.format             = TextureImporterFormat.Automatic;
                importer.SetPlatformTextureSettings(ps);
            }
            importer.SaveAndReimport();
            return true;
        }
    }
}
