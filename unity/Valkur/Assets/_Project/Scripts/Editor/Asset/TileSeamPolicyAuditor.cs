using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Scans every tile texture under <c>Assets/_Project/Resources/Tiles/</c>
    /// and reports any that violate the seam-safe import policy enforced by
    /// <see cref="ValkurAssetPostprocessor"/>:
    ///   * <c>filterMode == Point</c>            (no bilinear blur)
    ///   * <c>wrapMode  == Clamp</c>             (no UV-wrap seam)
    ///   * <c>mipmapEnabled == false</c>         (no down-sample blur)
    ///   * <c>textureCompression == Uncompressed</c>
    ///   * <c>spriteMeshType == FullRect</c>     (no Tight-mesh gap)
    ///   * <c>spriteExtrude   &gt;= 1</c>        (atlas-safe edge padding)
    ///
    /// Menu: <c>Valkur &gt; Tiles &gt; Audit Seam Policy</c>.
    ///
    /// Pure read-only; no asset is modified. To FIX offenders, run
    /// <c>Valkur &gt; Tiles &gt; Force Reimport Tiles</c> (handled by
    /// <see cref="TileReimporter"/>) — that path applies the seam-safe
    /// configuration to every tile in one batch.
    /// </summary>
    public static class TileSeamPolicyAuditor
    {
        private const string TILES_ROOT = "Assets/_Project/Resources/Tiles";
        private const int MAX_OFFENDERS_LOGGED = 20;

        [MenuItem("Valkur/Tiles/Audit Seam Policy")]
        public static void AuditSeamPolicy()
        {
            var offenders = CollectOffenders();
            if (offenders.Count == 0)
            {
                Debug.Log($"[TileSeamPolicyAuditor] OK — every tile under {TILES_ROOT} satisfies the seam-safe import policy.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[TileSeamPolicyAuditor] {offenders.Count} tile(s) violate the seam-safe import policy.");
            sb.AppendLine($"Run `Valkur > Tiles > Force Reimport Tiles` to fix.");
            sb.AppendLine();
            int shown = 0;
            foreach (var v in offenders)
            {
                sb.Append("  • ").Append(v.AssetPath).Append("  →  ").AppendLine(v.Reason);
                shown++;
                if (shown >= MAX_OFFENDERS_LOGGED)
                {
                    sb.AppendLine($"  … and {offenders.Count - shown} more.");
                    break;
                }
            }
            Debug.LogWarning(sb.ToString());
        }

        public readonly struct Violation
        {
            public readonly string AssetPath;
            public readonly string Reason;
            public Violation(string path, string reason) { AssetPath = path; Reason = reason; }
        }

        /// <summary>
        /// Returns every tile texture that does not match the seam-safe
        /// policy. Public so EditMode tests can run the audit as a
        /// regression guard without depending on the menu item.
        /// </summary>
        public static List<Violation> CollectOffenders()
        {
            var offenders = new List<Violation>();
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TILES_ROOT });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                string reason = ValidateTileImporter(importer);
                if (reason != null)
                    offenders.Add(new Violation(path, reason));
            }
            return offenders;
        }

        /// <summary>
        /// Returns null when the importer passes, or a one-line reason when it
        /// fails. Single source of truth for "what makes a tile seam-safe".
        /// </summary>
        public static string ValidateTileImporter(TextureImporter importer)
        {
            if (importer.filterMode != FilterMode.Point)
                return $"filterMode is {importer.filterMode}, expected Point";
            if (importer.wrapMode != TextureWrapMode.Clamp)
                return $"wrapMode is {importer.wrapMode}, expected Clamp";
            if (importer.mipmapEnabled)
                return "mipmaps are enabled, expected disabled";
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                return $"textureCompression is {importer.textureCompression}, expected Uncompressed";

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (settings.spriteMeshType != SpriteMeshType.FullRect)
                return $"spriteMeshType is {settings.spriteMeshType}, expected FullRect (Tight leaves sub-pixel gaps between tiles → blue seam)";
            if (settings.spriteExtrude < 1)
                return $"spriteExtrude is {settings.spriteExtrude}, expected >= 1";
            return null;
        }
    }
}
