using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Guards the two-direction rig that <c>tools/atlas/wave3/build_player_frames.py</c> bakes
    /// and <c>PlayerFramesImporter</c> binds for dwarf, barbarian and elven.
    ///
    /// The bug these exist for: the staged art faces WEST, and it was first read as facing
    /// east. The authored frames therefore landed in the east buckets and every one of the
    /// three characters faced AWAY from the cursor — while every still frame, every contact
    /// sheet and every count in the manifest looked perfect, because the mapping was
    /// internally consistent and only disagreed with the art. `Direction.East` is +X
    /// (<c>DirectionalAnimator.FrameLogic</c> resolves 0 degrees to East), so the east buckets
    /// must hold the right-facing copy.
    ///
    /// What is mechanically checkable is the CONTRACT, not the art direction: the two halves
    /// must be genuine mirrors of each other, each bucket must be filled from exactly one
    /// half, and the halves must be split east/west the way the generator declares. Which way
    /// the source art points is a judgement a human makes once by looking — see the
    /// "Player character pipeline" section of CLAUDE.md, and re-measure it rather than
    /// assuming it when a new wave is staged.
    /// </summary>
    public class PlayerTwoDirectionRigTests
    {
        private const string PlayerCatalog = "Assets/_Project/Data/Catalogs/Players";

        /// <summary>The players built by wave3. mague and valkyrie are still 8-directional.</summary>
        private static readonly string[] TwoDirectionPlayers = { "dwarf", "barbarian", "elven" };

        /// <summary>
        /// Bucket order as <c>DirectionalAnimator.BuildEightDirectionalSet</c> slices a linear
        /// list, paired with the facing suffix each one must carry.
        /// </summary>
        private static readonly (string direction, char facing)[] BucketFacing =
        {
            ("south",     'e'),
            ("southEast", 'e'),
            ("east",      'e'),
            ("northEast", 'e'),
            ("north",     'e'),
            ("northWest", 'w'),
            ("west",      'w'),
            ("southWest", 'w'),
        };

        private static readonly string[] StateFields =
        {
            "idleSheets", "walkSheets", "chaseSheets", "castSheets",
            "attackSheets", "damageSheets", "deathSheets",
        };

        private static PlayerDefinition Load(string key)
        {
            var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>($"{PlayerCatalog}/{key}.asset");
            Assert.IsNotNull(def, $"PlayerDefinition '{key}.asset' should exist.");
            Assert.IsNotNull(def.assetConfig, $"'{key}' assetConfig must not be null.");
            return def;
        }

        private static IEnumerable<(string player, string field, List<Sprite> frames)> PopulatedSheets()
        {
            foreach (string key in TwoDirectionPlayers)
            {
                var def = Load(key);
                foreach (string field in StateFields)
                {
                    var fi = typeof(EntityAssetConfig).GetField(field);
                    var list = fi.GetValue(def.assetConfig) as List<Sprite>;
                    if (list == null || list.Count == 0) continue;   // empty falls back; not this test's business
                    yield return (key, field, list);
                }
            }
        }

        [Test]
        public void EveryState_FillsTheEastBucketsWithEastFramesAndTheWestBucketsWithWest()
        {
            var failures = new List<string>();

            foreach ((string player, string field, List<Sprite> frames) in PopulatedSheets())
            {
                if (frames.Count % 8 != 0)
                {
                    failures.Add($"{player}.{field}: {frames.Count} frames is not 8 x framesPerDirection");
                    continue;
                }

                int perDirection = frames.Count / 8;
                for (int bucket = 0; bucket < BucketFacing.Length; bucket++)
                {
                    (string direction, char facing) = BucketFacing[bucket];
                    for (int i = 0; i < perDirection; i++)
                    {
                        Sprite sprite = frames[bucket * perDirection + i];
                        if (sprite == null)
                        {
                            failures.Add($"{player}.{field} {direction}[{i}]: null sprite");
                            continue;
                        }

                        // Names end "<state>_<facing><index>", e.g. dwarf_unarmed_idle_e3.
                        string name = sprite.name;
                        int digits = 0;
                        while (digits < name.Length && char.IsDigit(name[name.Length - 1 - digits])) digits++;
                        if (digits == 0 || digits >= name.Length)
                        {
                            failures.Add($"{player}.{field} {direction}[{i}]: '{name}' has no frame index");
                            continue;
                        }

                        char actual = name[name.Length - 1 - digits];
                        if (actual != facing)
                        {
                            failures.Add($"{player}.{field} {direction}[{i}]: '{name}' is a '{actual}' " +
                                         $"frame but this bucket must be '{facing}'");
                        }
                    }
                }
            }

            Assert.That(failures, Is.Empty,
                "Two-direction buckets are crossed. Direction.East is +X, so the east half of the " +
                "rig must hold the right-facing copy; the staged art faces WEST, so east is the " +
                "MIRRORED half. Crossing them makes the character face away from the cursor while " +
                "every individual frame still looks correct.\n  " + string.Join("\n  ", failures));
        }

        [Test]
        public void EveryFrame_HasAnEastCopyThatIsTheHorizontalMirrorOfItsWestCopy()
        {
            var failures = new List<string>();
            var checkedPairs = new HashSet<string>();

            foreach ((string player, string field, List<Sprite> frames) in PopulatedSheets())
            {
                foreach (Sprite sprite in frames)
                {
                    if (sprite == null) continue;

                    string path = AssetDatabase.GetAssetPath(sprite);
                    if (string.IsNullOrEmpty(path) || !checkedPairs.Add(path)) continue;

                    string twin = TwinPath(path);
                    if (twin == null)
                    {
                        failures.Add($"{path}: name does not end in _<e|w><index>");
                        continue;
                    }

                    var a = ReadPixels(path);
                    var b = ReadPixels(twin);
                    if (a == null || b == null)
                    {
                        failures.Add($"{path}: could not read it or its twin '{twin}'");
                        continue;
                    }

                    if (a.width != b.width || a.height != b.height)
                    {
                        failures.Add($"{path}: {a.width}x{a.height} but twin is {b.width}x{b.height}");
                        continue;
                    }

                    if (!IsHorizontalMirror(a, b))
                        failures.Add($"{path} is not the horizontal mirror of '{twin}'");
                }
            }

            Assert.That(failures, Is.Empty,
                "A two-direction rig is two mirrored halves of ONE animation. A pair that is not a " +
                "mirror means the halves drifted apart - a re-slice that only rewrote one of them, " +
                "or a hand-edit to a generated file.\n  " + string.Join("\n  ", failures));
        }

        /// <summary>Path of the opposite-facing frame: <c>..._e3.png</c> to <c>..._w3.png</c>.</summary>
        private static string TwinPath(string path)
        {
            int dot = path.LastIndexOf('.');
            if (dot <= 0) return null;

            int digits = 0;
            while (dot - 1 - digits >= 0 && char.IsDigit(path[dot - 1 - digits])) digits++;
            if (digits == 0) return null;

            int facingIndex = dot - 1 - digits;
            if (facingIndex < 0) return null;

            char facing = path[facingIndex];
            char flipped = facing == 'e' ? 'w' : facing == 'w' ? 'e' : '\0';
            if (flipped == '\0') return null;

            return path.Substring(0, facingIndex) + flipped + path.Substring(facingIndex + 1);
        }

        private static Texture2D ReadPixels(string path)
        {
            // The shipped importer leaves these unreadable, so decode the PNG bytes directly
            // rather than mutating a shipped .meta just to run a test.
            byte[] bytes;
            try { bytes = System.IO.File.ReadAllBytes(path); }
            catch { return null; }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return texture.LoadImage(bytes) ? texture : null;
        }

        private static bool IsHorizontalMirror(Texture2D a, Texture2D b)
        {
            Color32[] pa = a.GetPixels32();
            Color32[] pb = b.GetPixels32();
            int w = a.width, h = a.height;

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    Color32 left = pa[row + x];
                    Color32 right = pb[row + (w - 1 - x)];
                    // Both halves come out of one LANCZOS resample, so they are bit-identical
                    // under the flip; a small tolerance keeps this from becoming a codec test.
                    if (Mathf.Abs(left.a - right.a) > 2) return false;
                    if (left.a < 8) continue;   // fully transparent: RGB is meaningless
                    if (Mathf.Abs(left.r - right.r) > 2 ||
                        Mathf.Abs(left.g - right.g) > 2 ||
                        Mathf.Abs(left.b - right.b) > 2) return false;
                }
            }
            return true;
        }
    }
}
