using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Pixels-per-unit for the characters that are NOT baked at the shared 115 px, read from
    /// the frame manifest's <c>characterPpu</c> map.
    ///
    /// <para>WHY THIS EXISTS. Every sprite under <c>Art/Characters/</c> imports at
    /// <c>PLAYER_CHARACTER_PPU</c> (64), and the frame builder bakes every character to
    /// <c>TARGET_BODY_PX</c> (115) so that 115 / 64 = 1.797 world units is the height every
    /// melee range, projectile offset and camera lead in the project was tuned against. One
    /// constant, one height, and the whole roster interchangeable — which is exactly right
    /// for five characters drawn at the same scale.</para>
    ///
    /// <para>It stops being right the moment a character must be TALLER or must keep MORE
    /// DETAIL, because those are two different properties riding on one number. The pixel
    /// height is the only lever on quality — the vampire's source cells are 331-681 px tall,
    /// so 115 px throws away three to five times linear, and measured on her idle the face,
    /// the choker and the gold filigree are simply gone. The RATIO of pixel height to PPU is
    /// the world height. Raise the pixel budget alone and the character grows; raise the PPU
    /// alone and she shrinks. Only the pair says "taller AND sharper".</para>
    ///
    /// <para>So the builder declares both per character and this reads the PPU half back.
    /// The vampire ships 256 px at PPU 96 = 2.667 world units, 1.48x the dwarf, at 4.9x the
    /// texels.</para>
    ///
    /// <para>IT LIVES IN THE MANIFEST, not in the texture's <c>.meta</c>, for the reason
    /// <see cref="CharacterSpritePivots"/> records: the builder rewrites the manifest for a
    /// whole player on every run, so a value stored beside the texture would be silently
    /// correct until somebody rebuilt an unrelated sheet and then silently wrong. Here that
    /// matters more than it does for a pivot — a stale PPU does not shear a blade, it
    /// resizes the character and everything <c>EntityColliderConfigurator</c> derives from
    /// <c>renderer.bounds</c>.</para>
    ///
    /// <para>MATCHING IS BY FOLDER, not by sprite. <c>characterPpu</c> is a flat
    /// <c>playerKey -&gt; ppu</c> map and a sprite belongs to a character when its path sits
    /// under <c>Art/Characters/&lt;playerKey&gt;/</c>. Listing 180 sprite paths per character
    /// would work and would be wrong in one specific way: a frame written to disk but not yet
    /// named in the manifest — which is every frame, during the window between the Python run
    /// and the next manifest read — would import at the default PPU and come back the wrong
    /// size, with nothing failing.</para>
    ///
    /// <para>A character with no entry — which is every character but the vampire — reports 0
    /// and imports exactly as it always did.</para>
    /// </summary>
    public static class CharacterSpritePpu
    {
        /// <summary>
        /// Manifests are matched by name rather than listed, so a wave5 or wave6 file is
        /// picked up without editing this. Relative to the repository root.
        /// </summary>
        private const string ManifestDir = "tools/atlas/generated";

        private static readonly Regex ManifestName =
            new Regex(@"^player_frames_manifest.*\.json$", RegexOptions.IgnoreCase);

        /// <summary>
        /// The prefix a sprite must carry to belong to a character folder. Lowercased,
        /// forward slashes — the same normalisation <see cref="Normalize"/> applies, because
        /// <c>OnPreprocessTexture</c> is handed Unity's own path form and a case difference
        /// on Windows would silently miss.
        /// </summary>
        private const string CharacterRoot = "assets/_project/art/characters/";

        // Editor-only cache, keyed by lowercased playerKey.
        private static Dictionary<string, float> _ppuByPlayer;

        // Reloaded when any manifest's timestamp moves. A rebuild is exactly the moment the
        // numbers change, and it happens while the Editor is open, so a cache that only
        // filled once would serve the previous run's values for the rest of the session.
        private static long _stamp;

        /// <summary>
        /// Pixels-per-unit for one character sprite, or 0 when the character has no entry
        /// and should import at the shared default.
        /// </summary>
        public static float PpuFor(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return 0f;
            EnsureLoaded();
            if (_ppuByPlayer == null || _ppuByPlayer.Count == 0) return 0f;

            string key = PlayerKeyOf(assetPath);
            return key != null && _ppuByPlayer.TryGetValue(key, out float ppu) ? ppu : 0f;
        }

        /// <summary>Drop the cache. For tests, and for anything that rebuilds mid-session.</summary>
        public static void Invalidate()
        {
            _ppuByPlayer = null;
            _stamp = 0;
        }

        /// <summary>
        /// The character folder a sprite sits in, or null when the path is not under
        /// <c>Art/Characters/</c> at all. The segment immediately after the root is the
        /// <c>playerKey</c>; everything below it is the animation state folder.
        /// </summary>
        private static string PlayerKeyOf(string assetPath)
        {
            string path = Normalize(assetPath);
            int start = path.IndexOf(CharacterRoot, StringComparison.Ordinal);
            if (start < 0) return null;

            start += CharacterRoot.Length;
            int end = path.IndexOf('/', start);
            // A file sitting directly in Art/Characters/ belongs to no character.
            return end > start ? path.Substring(start, end - start) : null;
        }

        private static void EnsureLoaded()
        {
            string dir = ManifestDirectory();
            if (dir == null || !Directory.Exists(dir))
            {
                _ppuByPlayer = _ppuByPlayer ?? new Dictionary<string, float>();
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

            if (_ppuByPlayer != null && stamp == _stamp) return;

            _ppuByPlayer = new Dictionary<string, float>();
            _stamp = stamp;
            foreach (string path in files) Harvest(path, _ppuByPlayer);
        }

        /// <summary>
        /// Pull the <c>characterPpu</c> map out of one manifest.
        ///
        /// <para>Read with a regex rather than <c>JsonUtility</c> for the reason
        /// <see cref="CharacterSpritePivots"/> gives — the manifest is a nested document the
        /// Python side grows fields on, and mirroring it as serializable C# classes would be
        /// another place to edit every time it does. What is needed here is one small flat
        /// object.</para>
        /// </summary>
        private static void Harvest(string path, Dictionary<string, float> into)
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CharacterSpritePpu] Could not read '{path}': {ex.Message}");
                return;
            }

            // "characterPpu": { "vampire": 96 }. The body stops at the first '}' because the
            // map is flat by construction — the builder writes playerKey -> number and
            // nothing else, which is also why this block is top-level rather than a field
            // inside each player entry: those are read positionally and a nested object
            // would bind to whichever neighbour happened to be adjacent.
            Match block = Regex.Match(json, "\"characterPpu\"\\s*:\\s*\\{(?<body>[^}]*)\\}",
                                      RegexOptions.Singleline);
            if (!block.Success) return;

            foreach (Match pair in Regex.Matches(block.Groups["body"].Value,
                                                 "\"(?<k>[^\"]+)\"\\s*:\\s*(?<v>[0-9.eE+-]+)"))
            {
                if (!float.TryParse(pair.Groups["v"].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float ppu))
                    continue;
                // A non-positive PPU would make every sprite in that folder infinitely large.
                // Refuse it loudly rather than shipping it: the manifest is generated, so a
                // zero here means the generator is broken, not that a designer chose one.
                if (ppu <= 0f)
                {
                    Debug.LogWarning($"[CharacterSpritePpu] '{pair.Groups["k"].Value}' declares a " +
                                     $"non-positive PPU ({ppu}) in '{Path.GetFileName(path)}'. " +
                                     "Ignoring it; that character imports at the default.");
                    continue;
                }
                into[pair.Groups["k"].Value.ToLowerInvariant()] = ppu;
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
