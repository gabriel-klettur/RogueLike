using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Valkur.Editor
{
    /// <summary>
    /// Tile-atlas menu helpers.
    ///
    /// History — why this class no longer builds its own atlas: it used to write
    /// <c>Assets/_Project/Art/Tiles/Atlas_Tiles.spriteatlas</c> from
    /// <c>Resources/Tiles</c>, which is the exact folder the <c>env-tiles</c>
    /// group of <see cref="SpriteAtlasBuilder"/> already packs. Two atlases
    /// claiming the same 3077 textures made Unity log
    /// "Sprite X matches more than one built-in atlases" once per sprite —
    /// 3077 console warnings on a plain project load — and shipped the tile
    /// atlas twice in the build.
    ///
    /// <see cref="SpriteAtlasBuilder"/> is now the single owner of atlas
    /// creation and packing settings, and every atlas lives under
    /// <c>_Project/SpriteAtlases/</c> as the project convention requires. The
    /// menu items here are kept — they are the ones in muscle memory — but
    /// Build delegates, and Validate inspects the canonical asset.
    /// </summary>
    public static class TileAtlasBuilder
    {
        /// <summary>The canonical tile atlas, owned by <see cref="SpriteAtlasBuilder"/>.</summary>
        private const string ATLAS_PATH = "Assets/_Project/SpriteAtlases/env-tiles.spriteatlas";

        /// <summary>Source folder for runtime tiles (loaded by TileCatalog.BuildFromResources at boot).</summary>
        private const string TILES_FOLDER = "Assets/_Project/Resources/Tiles";

        [MenuItem("Valkur/Tiles/Build Tile Atlas")]
        public static void BuildTileAtlas()
        {
            // Delegate rather than build a second atlas: one owner for packing
            // settings means the tile atlas can never drift into a duplicate again.
            Debug.Log("[TileAtlasBuilder] Tiles are packed by the 'env-tiles' group — " +
                      "delegating to SpriteAtlasBuilder so all atlases keep one owner.");
            SpriteAtlasBuilder.BuildAll();
            ValidateTileAtlas();
        }

        [MenuItem("Valkur/Tiles/Validate Tile Atlas")]
        public static void ValidateTileAtlas()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(ATLAS_PATH);
            if (atlas == null)
            {
                Debug.LogError($"[TileAtlasBuilder] Atlas not found at {ATLAS_PATH}. " +
                               "Run Valkur > Assets > Build Sprite Atlases first.");
                return;
            }

            var packables = atlas.GetPackables();
            int packableCount = packables != null ? packables.Length : 0;
            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { TILES_FOLDER });

            Debug.Log($"[TileAtlasBuilder] Atlas: {ATLAS_PATH}\n" +
                      $"  Packable sources: {packableCount}\n" +
                      $"  Sprites in {TILES_FOLDER}: {guids.Length}\n" +
                      $"  Packed sprites: {atlas.spriteCount}");

            if (atlas.spriteCount == 0 && guids.Length > 0)
                Debug.LogWarning("[TileAtlasBuilder] Atlas has 0 packed sprites. " +
                                 "Enter Play Mode or build to trigger packing.");

            // Guard against the duplicate-atlas regression this class used to cause.
            int duplicates = CountAtlasesPacking(TILES_FOLDER);
            if (duplicates > 1)
                Debug.LogError($"[TileAtlasBuilder] {duplicates} SpriteAtlas assets pack " +
                               $"'{TILES_FOLDER}'. Unity will warn once per sprite and ship the " +
                               "atlas more than once — keep exactly one.");
        }

        /// <summary>How many SpriteAtlas assets in the project list <paramref name="folderPath"/> as a packable.</summary>
        private static int CountAtlasesPacking(string folderPath)
        {
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:SpriteAtlas"))
            {
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (atlas == null) continue;
                foreach (var packable in atlas.GetPackables())
                {
                    if (packable == null) continue;
                    if (AssetDatabase.GetAssetPath(packable) == folderPath) { count++; break; }
                }
            }
            return count;
        }
    }
}
