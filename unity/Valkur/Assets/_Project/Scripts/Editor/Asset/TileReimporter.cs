using UnityEditor;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Forces re-import of tile and NPC textures, directly setting
    /// PPU, pivot, filter, and compression via the TextureImporter API.
    /// Does NOT rely on ValkurAssetPostprocessor callbacks.
    /// </summary>
    public static class TileReimporter
    {
        private const int TILE_PPU = 32;
        private const int NPC_PPU = 64;
        private const int CHARACTER_PPU = 64;
        private const int BUILDING_PPU = 32;

        [MenuItem("Valkur/Tiles/Force Reimport Tiles")]
        private static void ReimportTiles()
        {
            ReimportFolder("Assets/_Project/Resources/Tiles", "Tile", TILE_PPU, new Vector2(0.5f, 0.5f));
        }

        [MenuItem("Valkur/Tiles/Force Reimport NPCs")]
        private static void ReimportNPCs()
        {
            ReimportFolder("Assets/_Project/Art/NPC", "NPC", NPC_PPU, new Vector2(0.5f, 0f));
        }

        [MenuItem("Valkur/Tiles/Force Reimport All Game Art")]
        private static void ReimportAll()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                ReimportTiles();
                ReimportNPCs();
                ReimportFolder("Assets/_Project/Art/Characters", "Character", CHARACTER_PPU, new Vector2(0.5f, 0f));
                ReimportFolder("Assets/_Project/Art/Buildings", "Building", BUILDING_PPU, new Vector2(0.5f, 0f));
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        private static void ReimportFolder(string folder, string label, int ppu, Vector2 pivot)
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            if (guids.Length == 0)
            {
                Debug.LogWarning($"[TileReimporter] No textures found in {folder}");
                return;
            }

            int count = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = ppu;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                // Per-folder compression policy (mirrors ValkurAssetPostprocessor):
                //   * Buildings → CompressedHQ (~8× memory savings, no visible
                //     quality loss at game zoom).
                //   * Everything else → Uncompressed (pixel-perfect tile/character art).
                bool useCompressedHQ = path.Contains("/Buildings/");
                importer.textureCompression = useCompressedHQ
                    ? TextureImporterCompression.CompressedHQ
                    : TextureImporterCompression.Uncompressed;
                // Mirrors ValkurAssetPostprocessor.OnPreprocessTexture: Clamp
                // wrap + FullRect mesh + 1 px extrude are the canonical
                // tile-safe settings that prevent the Camera-backgroundColor
                // seam between adjacent tiles. Keep this block in lock-step
                // with the postprocessor — if it ever drifts, the manual
                // reimporter will reintroduce the Tight-mesh seam bug.
                importer.wrapMode = TextureWrapMode.Clamp;

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = pivot;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteExtrude = 1;
                importer.SetTextureSettings(settings);

                importer.SaveAndReimport();
                count++;
            }

            Debug.Log($"[TileReimporter] Force-reimported {count} {label} textures from {folder} (PPU={ppu}, pivot={pivot})");
        }
    }
}
