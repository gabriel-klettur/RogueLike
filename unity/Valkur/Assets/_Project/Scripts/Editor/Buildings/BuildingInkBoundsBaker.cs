#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Editor.Buildings
{
    /// <summary>
    /// Measures where each building's ART actually starts and writes it onto the template.
    ///
    /// <para><b>Why this has to be baked.</b> The shadow shear needs the bottom row of INK, and
    /// a sprite only knows its RECT. At runtime the two are not the same question and the answer
    /// is unreachable: every building sprite is packed into <c>buildings.spriteatlas</c>, whose
    /// page is not readable, so <c>Texture2D.GetPixels</c> throws. The fact is a property of the
    /// PNG, it never changes while the PNG does not, and this is the same shape
    /// <c>Valkur &gt; Monsters &gt; Bake Cast Muzzles</c> uses for the same reason.</para>
    ///
    /// <para><b>Why the threshold is SOLID ink and not any ink.</b> Six of the 98 padded sprites
    /// carry a PAINTED shadow, a tuft of grass or a faded tail under the building, at an alpha
    /// the eye barely registers — <c>mariposa_rama_caida</c> has 159 px of it. Measured at
    /// alpha&gt;0 those sprites look like they reach the bottom row and the shear goes on starting
    /// below the building; measured at alpha&gt;48 the foot line lands on the building's real base
    /// and the painted shadow simply sits inside the projected one, which is what it is for.
    /// Trimming the PNG cannot fix those at all — the pixels are wanted.</para>
    ///
    /// <para>Re-runnable and non-destructive to authoring: it writes exactly the two baked
    /// fields, and it does NOT touch <c>projectedShadow</c>, which is a human's override.
    /// Never <c>Undo.RecordObject</c> — that put 193 templates on the global undo stack once and
    /// the first thing that popped it reverted every one of them in memory.</para>
    /// </summary>
    public static class BuildingInkBoundsBaker
    {
        private const string MenuPath   = "Valkur/Buildings/Bake Sprite Ink Bounds";
        private const string MenuReport = "Valkur/Buildings/Bake Sprite Ink Bounds (Report Only)";

        /// <summary>
        /// Alpha above which a pixel counts as the building rather than as something painted on
        /// the ground under it. 48/255 is a fifth: a cast shadow in this art runs 20-40, the
        /// faintest real silhouette edge runs well above 100.
        /// </summary>
        private const byte SolidInkAlpha = 48;

        /// <summary>
        /// Below this fraction the correction is smaller than the art's own anti-aliasing and
        /// writing it only adds churn to the diff. 0.3 % of a 1024 px sprite is three pixels.
        /// </summary>
        private const float NegligibleFraction = 0.003f;

        [MenuItem(MenuPath)]
        public static void Run() => Execute(write: true);

        [MenuItem(MenuReport)]
        public static void Report() => Execute(write: false);

        private static void Execute(bool write)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(BuildingTemplateData));
            var measured = new Dictionary<string, float>();   // asset path -> ink bottom fraction
            var changed  = new List<(BuildingTemplateData tpl, float oldVal, float newVal)>();
            var flat     = new List<BuildingTemplateData>();
            int missing = 0, unreadable = 0;

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var tpl = AssetDatabase.LoadAssetAtPath<BuildingTemplateData>(path);
                    if (tpl == null || string.IsNullOrEmpty(tpl.assetPath)) { missing++; continue; }

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Bake Sprite Ink Bounds",
                            $"{tpl.assetPath}  ({i + 1}/{guids.Length})",
                            (i + 1) / (float)guids.Length))
                        break;

                    if (!measured.TryGetValue(tpl.assetPath, out float fraction))
                    {
                        if (!TryMeasure(tpl.assetPath, out fraction))
                        {
                            unreadable++;
                            continue;
                        }
                        measured[tpl.assetPath] = fraction;
                    }

                    if (BuildingProjectedShadow.IsFlatArt(tpl.assetPath)) flat.Add(tpl);

                    float wanted = fraction < NegligibleFraction ? 0f : fraction;
                    if (Mathf.Abs(tpl.inkBottomNormalized - wanted) < 0.0005f) continue;

                    changed.Add((tpl, tpl.inkBottomNormalized, wanted));
                    if (write)
                    {
                        tpl.inkBottomNormalized = wanted;
                        EditorUtility.SetDirty(tpl);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (write && changed.Count > 0)
            {
                // Only what this tool dirtied. A blanket SaveAssets() writes the WHOLE project,
                // which is the call that once flushed a corrupted in-memory ScriptableObject
                // over good files.
                foreach (var (tpl, _, _) in changed) AssetDatabase.SaveAssetIfDirty(tpl);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[BuildingInkBoundsBaker] {(write ? "Baked" : "Report")} over {guids.Length} templates " +
                          $"({measured.Count} distinct sprites).");
            sb.AppendLine($"  changed: {changed.Count}   no assetPath: {missing}   unreadable PNG: {unreadable}");
            sb.AppendLine($"  drawn in plan (cast no projected shadow): {flat.Count}");
            changed.Sort((a, b) => b.newVal.CompareTo(a.newVal));
            int shown = Mathf.Min(changed.Count, 40);
            for (int i = 0; i < shown; i++)
            {
                var (tpl, oldVal, newVal) = changed[i];
                sb.AppendLine($"    {tpl.assetPath,-60} {oldVal:F4} -> {newVal:F4}  (id {tpl.templateId})");
            }
            if (changed.Count > shown) sb.AppendLine($"    ... and {changed.Count - shown} more");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// The fraction of the PNG's height that is transparent BELOW its lowest row of solid
        /// ink. Read from the file rather than through the Sprite, because the shipped sprite is
        /// atlas-packed and its texture cannot be sampled.
        /// </summary>
        public static bool TryMeasure(string resourcesRelativePath, out float fraction)
        {
            fraction = 0f;
            string file = FindPng(resourcesRelativePath);
            if (file == null) return false;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(file), markNonReadable: false))
                    return false;

                int w = tex.width, h = tex.height;
                if (w <= 0 || h <= 0) return false;

                var pixels = tex.GetPixels32();
                for (int y = 0; y < h; y++)   // GetPixels32 is bottom-up: y = 0 IS the bottom row
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        if (pixels[row + x].a <= SolidInkAlpha) continue;
                        fraction = Mathf.Clamp01(y / (float)h);
                        return true;
                    }
                }
                // Wholly transparent, or wholly below the threshold. Claim no offset rather than
                // the whole height: a bad measurement that moves the foot line to the roof is
                // worse than the padding it was meant to fix.
                return true;
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        private static string FindPng(string resourcesRelativePath)
        {
            const string root = "Assets/_Project/Resources/";
            string direct = root + resourcesRelativePath + ".png";
            if (File.Exists(direct)) return direct;
            foreach (var ext in new[] { ".PNG", ".jpg", ".jpeg" })
            {
                string alt = root + resourcesRelativePath + ext;
                if (File.Exists(alt)) return alt;
            }
            return null;
        }
    }
}
#endif
