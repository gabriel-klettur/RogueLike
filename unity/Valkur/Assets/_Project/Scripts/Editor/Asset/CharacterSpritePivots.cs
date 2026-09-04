using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Where the feet are, per character sprite, for the handful of animations whose canvas
    /// extends BELOW the ground line.
    ///
    /// <para>WHY THIS EXISTS. Every sprite under <c>Art/Characters/</c> is imported with a
    /// pivot of (0.5, 0), and the frame builder relies on that: it ends the canvas exactly at
    /// the ground line so that "pivot at the bottom row" and "pivot on the boots" are the same
    /// statement. The assumption holds for every sheet in the project except the harvest ones.
    /// A dwarf chopping swings the axe THROUGH the boot line into the wood, and with nothing
    /// reserved below that line the blade was sheared flat by the canvas edge — measured on
    /// the shipped sprites, the three strike frames carried 40, 40 and 64 lit pixels in the
    /// bottom row against 19 for the boots alone.</para>
    ///
    /// <para>So those sheets now reserve the overhang, which moves their feet off the bottom
    /// row, which means their pivot can no longer be the constant the rest of the project
    /// uses. The builder measures the reservation and writes it into the manifest as
    /// <c>pivotY</c>; this reads it back.</para>
    ///
    /// <para>IT LIVES IN THE MANIFEST, not in the texture's <c>.meta</c>. The builder rewrites
    /// the manifest for a whole player on every run, so a pivot stored beside the texture
    /// would be silently correct until the next time anyone rebuilt an unrelated sheet, and
    /// then silently wrong. The manifest is the one file that is regenerated in lockstep with
    /// the pixels it describes.</para>
    ///
    /// <para>A sprite with no entry — which is every sprite in the project bar two animations
    /// — reports 0 and imports exactly as it always did.</para>
    /// </summary>
    public static class CharacterSpritePivots
    {
        /// <summary>
        /// Manifests are matched by name rather than listed, so a wave5 or wave6 file is
        /// picked up without editing this. Relative to the repository root.
        /// </summary>
        private const string ManifestDir = "tools/atlas/generated";
        private static readonly Regex ManifestName =
            new Regex(@"^player_frames_manifest.*\.json$", RegexOptions.IgnoreCase);

        // Editor-only cache. Keyed by the sprite's asset path, lowercased with forward
        // slashes, because OnPreprocessTexture is handed paths in Unity's own form and the
        // manifest writes them in the same form — but a case difference on Windows would
        // silently miss and re-introduce the sheared blade with nothing failing.
        private static Dictionary<string, float> _pivots;

        // Reloaded when any manifest's timestamp moves. A rebuild is exactly the moment the
        // numbers change, and it happens while the Editor is open, so a cache that only
        // filled once would serve the previous run's pivots for the rest of the session.
        private static long _stamp;

        /// <summary>
        /// Normalised pivot Y for one character sprite. 0 means "the feet ARE the bottom
        /// row", which is the default and the answer for all but the harvest animations.
        /// </summary>
        public static float PivotYFor(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return 0f;
            EnsureLoaded();
            return _pivots != null && _pivots.TryGetValue(Normalize(assetPath), out float y) ? y : 0f;
        }

        /// <summary>Drop the cache. For tests, and for anything that rebuilds mid-session.</summary>
        public static void Invalidate()
        {
            _pivots = null;
            _stamp = 0;
        }

        private static void EnsureLoaded()
        {
            string dir = ManifestDirectory();
            if (dir == null || !Directory.Exists(dir))
            {
                _pivots = _pivots ?? new Dictionary<string, float>();
                return;
            }

            long stamp = 0;
            var files = new List<string>();
            foreach (string path in Directory.GetFiles(dir, "*.json"))
            {
                if (!ManifestName.IsMatch(Path.GetFileName(path))) continue;
                files.Add(path);
                stamp ^= File.GetLastWriteTimeUtc(path).Ticks;
            }

            if (_pivots != null && stamp == _stamp) return;

            _pivots = new Dictionary<string, float>();
            _stamp = stamp;
            foreach (string path in files) Harvest(path, _pivots);
        }

        /// <summary>
        /// Pull every (sprite path, pivotY) pair out of one manifest.
        ///
        /// <para>Read with a regex rather than <c>JsonUtility</c> on purpose: the manifest is
        /// a nested document with four different shapes that carry sprites (states, attack
        /// variants, cast variants, loadout states), and mirroring all four as serializable
        /// C# classes would be four more places to edit every time the Python side grows a
        /// field. What is needed here is one number and a list of strings.</para>
        /// </summary>
        private static void Harvest(string path, Dictionary<string, float> into)
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CharacterSpritePivots] Could not read '{path}': {ex.Message}");
                return;
            }

            // Each block is "pivotY": <n>, ... "sprites": [ "...", ... ]. Only blocks with a
            // NON-ZERO pivot are recorded: a zero is the default this whole class falls back
            // to, so storing thousands of them would cost memory to say nothing.
            foreach (Match m in Regex.Matches(json,
                "\"pivotY\"\\s*:\\s*(?<p>[0-9.eE+-]+).*?\"sprites\"\\s*:\\s*\\[(?<s>[^\\]]*)\\]",
                RegexOptions.Singleline))
            {
                if (!float.TryParse(m.Groups["p"].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float pivot))
                    continue;
                if (pivot <= 0f) continue;

                foreach (Match sprite in Regex.Matches(m.Groups["s"].Value, "\"(?<v>[^\"]+)\""))
                    into[Normalize(sprite.Groups["v"].Value)] = pivot;
            }
        }

        private static string Normalize(string path)
            => path.Replace('\\', '/').ToLowerInvariant();

        /// <summary>
        /// The repository's <c>tools/atlas/generated</c>. <c>Application.dataPath</c> is
        /// <c>&lt;repo&gt;/unity/Valkur/Assets</c>, so the root is three levels up.
        /// </summary>
        private static string ManifestDirectory()
        {
            try
            {
                var assets = new DirectoryInfo(Application.dataPath);
                var root = assets.Parent?.Parent?.Parent;
                return root == null ? null : Path.Combine(root.FullName, ManifestDir);
            }
            catch { return null; }
        }
    }
}
