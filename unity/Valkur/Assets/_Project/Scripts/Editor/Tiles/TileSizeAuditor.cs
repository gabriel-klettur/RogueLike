using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Valkur.Editor.Tiles
{
    /// <summary>
    /// In-editor counterpart of <c>python/scripts/audit_tile_sizes.py</c>.
    ///
    /// Scans every PNG under <c>Assets/_Project/Resources/Tiles</c> and reports
    /// which sprites violate the canonical tile size policy (≤64x64 px @ PPU=32).
    /// Oversized tiles cause catastrophic visual bleeding (one cell rendering
    /// as N×N units), e.g. the historical "sand patch" overlap bug.
    ///
    /// Menu:
    ///   Valkur > Tiles > Audit Sizes  (read-only, prints to Console)
    /// </summary>
    public static class TileSizeAuditor
    {
        private const int TILE_PPU              = 32;
        private const int TILE_MAX_ALLOWED_SIZE = 64;
        private const string TILES_ROOT         = "Assets/_Project/Resources/Tiles";

        private struct Violation
        {
            public string Path;
            public int    Width;
            public int    Height;
            public int    Ppu;
            public string Reason;
        }

        [MenuItem("Valkur/Tiles/Audit Sizes")]
        public static void Audit()
        {
            if (!AssetDatabase.IsValidFolder(TILES_ROOT))
            {
                Debug.LogError($"[TileSizeAuditor] Folder not found: {TILES_ROOT}");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TILES_ROOT });
            int scanned = 0;
            var violations = new List<Violation>(capacity: 16);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                // Skip backup / source folders — exempt from strict tile policy.
                if (path.Contains("/_backups/") || path.Contains("/_raw/") || path.Contains("/_source/"))
                    continue;

                scanned++;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                Vector2Int size = GetSourceSize(importer);
                int max = Mathf.Max(size.x, size.y);
                int ppu = Mathf.RoundToInt(importer.spritePixelsPerUnit);

                if (max > TILE_MAX_ALLOWED_SIZE)
                {
                    violations.Add(new Violation
                    {
                        Path = path, Width = size.x, Height = size.y, Ppu = ppu,
                        Reason = $"oversized (>{TILE_MAX_ALLOWED_SIZE}px)"
                    });
                }
                else if (size.x != size.y)
                {
                    violations.Add(new Violation
                    {
                        Path = path, Width = size.x, Height = size.y, Ppu = ppu,
                        Reason = "non-square"
                    });
                }
                else if (ppu != TILE_PPU)
                {
                    violations.Add(new Violation
                    {
                        Path = path, Width = size.x, Height = size.y, Ppu = ppu,
                        Reason = $"ppu mismatch (expected {TILE_PPU})"
                    });
                }
            }

            if (violations.Count == 0)
            {
                Debug.Log($"<color=#4ade80>[TileSizeAuditor] OK</color> — " +
                          $"{scanned} tiles scanned, all conform to {TILE_PPU}px @ PPU={TILE_PPU}.");
                return;
            }

            // Group by reason for readability
            string report = $"[TileSizeAuditor] {violations.Count} violation(s) found in {scanned} tiles:\n";
            foreach (var grp in violations.GroupBy(v => v.Reason))
            {
                report += $"\n  -- {grp.Key} ({grp.Count()}) --\n";
                foreach (var v in grp.OrderBy(x => x.Path))
                {
                    report += $"    {v.Width,4}x{v.Height,-4}  ppu={v.Ppu,-5}  {v.Path}\n";
                }
            }
            report += "\nFix with: python python/scripts/audit_tile_sizes.py --fix";
            Debug.LogError(report);
        }

        [MenuItem("Valkur/Tiles/Reimport All Tiles")]
        public static void ReimportAll()
        {
            AssetDatabase.ImportAsset(TILES_ROOT, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log($"[TileSizeAuditor] Reimported all tiles under {TILES_ROOT}.");
        }

        private static Vector2Int GetSourceSize(TextureImporter importer)
        {
            int w = 0, h = 0;
            var mi = typeof(TextureImporter).GetMethod(
                "GetWidthAndHeight",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (mi != null)
            {
                object[] args = new object[] { w, h };
                mi.Invoke(importer, args);
                w = (int)args[0];
                h = (int)args[1];
            }
            return new Vector2Int(w, h);
        }
    }
}
