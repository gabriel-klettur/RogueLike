using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine.U2D;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Packing settings on the sprite atlases, which are where tile seams actually get decided.
    ///
    /// <see cref="TileSeamPolicyTests"/> already pins the source PNGs — Point filtering, no
    /// mipmaps, Uncompressed, FullRect, extrude at least 1 — and CameraOrthoSnapTests pins the
    /// camera arithmetic that keeps one texel on one screen pixel. Between them the invariant
    /// looked covered. It was not: at runtime a packed sprite is sampled from the ATLAS
    /// texture, and the atlas carries its own filter mode, its own mipmap flag and its own
    /// padding. Those override the source. A tile PNG can be flawless and still bleed if the
    /// atlas it lands in is set to Bilinear, or generates mips, or packs with zero padding.
    ///
    /// Nothing guarded that, so every one of these is a single Inspector checkbox away from
    /// putting black seams back between every tile at every zoom level — silently, with the
    /// whole suite still green.
    /// </summary>
    [TestFixture]
    public class SpriteAtlasSeamPolicyTests
    {
        private const string ATLAS_DIR = "Assets/_Project/SpriteAtlases";

        /// <summary>
        /// Below 2, a single bilinear tap or one mip level reaches into the neighbouring
        /// sprite. Two texels of gutter is the standard minimum for packed pixel art.
        /// </summary>
        private const int MIN_PADDING = 2;

        /// <summary>
        /// Atlases exempt from the Point-filtering rule, with the reason.
        ///
        /// UI is the only legitimate one: it is laid out in canvas space at arbitrary
        /// non-integer scales, so Point sampling makes panel chrome and icons crawl. Nothing
        /// in it is sampled by a tilemap, so it cannot contribute a world seam.
        /// </summary>
        private static readonly Dictionary<string, string> PointFilterExemptions =
            new Dictionary<string, string>
            {
                ["ui"] = "Canvas-space art is scaled non-integrally; Point sampling makes it crawl.",
            };

        /// <summary>
        /// Atlases exempt from alpha dilation, with the reason.
        ///
        /// `players` is NOT a considered exception — it is recorded here because that is how
        /// the project found it, and flipping it is a visual change that belongs to whoever
        /// owns the character art. Without dilation the transparent gutter stays black, so
        /// wherever a character sprite is sampled below 1:1 the edge averages toward black and
        /// the silhouette picks up a dark fringe. Worth turning on and looking at.
        /// </summary>
        private static readonly Dictionary<string, string> AlphaDilationExemptions =
            new Dictionary<string, string>
            {
                ["players"] = "Currently off. Suspected oversight rather than a decision — see the " +
                              "note on this list before adding another entry.",
            };

        private static IEnumerable<TestCaseData> Atlases()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:SpriteAtlas", new[] { ATLAS_DIR }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                yield return new TestCaseData(path)
                    .SetName($"Atlas({System.IO.Path.GetFileNameWithoutExtension(path)})");
            }
        }

        private static SpriteAtlas Load(string path)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            Assert.IsNotNull(atlas, $"{path} did not load as a SpriteAtlas.");
            return atlas;
        }

        private static string Name(string path) => System.IO.Path.GetFileNameWithoutExtension(path);

        [Test]
        public void EveryAtlasLivesInTheOneAtlasFolder()
        {
            // A second atlas over the same folder makes Unity log "matches more than one
            // built-in atlases" once per sprite and ships the art twice — and which atlas wins
            // at runtime is then undefined, so the filter mode this fixture checks stops being
            // the one that applies.
            var strays = AssetDatabase.FindAssets("t:SpriteAtlas")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.StartsWith(ATLAS_DIR + "/"))
                .ToList();

            Assert.IsEmpty(strays, "Atlases outside " + ATLAS_DIR + ":\n  " + string.Join("\n  ", strays));
        }

        [Test]
        public void ThereAreAtlasesToCheck()
        {
            Assert.IsNotEmpty(Atlases().ToList(),
                $"No SpriteAtlas found under {ATLAS_DIR} — every case below would pass vacuously.");
        }

        // ── Packing ──────────────────────────────────────────────────────────────

        [TestCaseSource(nameof(Atlases))]
        public void Padding_IsAtLeastTwoTexels(string path)
        {
            Assert.GreaterOrEqual(Load(path).GetPackingSettings().padding, MIN_PADDING,
                $"{Name(path)}: with less gutter than this, sampling at a sprite's edge reaches " +
                "into whatever was packed next to it. On tiles that is a coloured or black line " +
                "at every cell boundary.");
        }

        [TestCaseSource(nameof(Atlases))]
        public void TightPacking_IsOff(string path)
        {
            Assert.IsFalse(Load(path).GetPackingSettings().enableTightPacking,
                $"{Name(path)}: tight packing fits sprites to their alpha outline, so the " +
                "rectangular quad a Tilemap draws samples outside the sprite along its edges.");
        }

        [TestCaseSource(nameof(Atlases))]
        public void Rotation_IsOff(string path)
        {
            Assert.IsFalse(Load(path).GetPackingSettings().enableRotation,
                $"{Name(path)}: a rotated sprite is resampled off the pixel grid, which is the " +
                "one thing the whole pixel-perfect chain exists to prevent.");
        }

        [TestCaseSource(nameof(Atlases))]
        public void AlphaDilation_IsOn(string path)
        {
            string name = Name(path);
            bool on = Load(path).GetPackingSettings().enableAlphaDilation;

            if (AlphaDilationExemptions.TryGetValue(name, out string why))
            {
                // Ratchets both ways: turning it on is an improvement, and this line should be
                // deleted when it happens rather than left behind as a stale exemption.
                Assert.IsFalse(on,
                    $"{name} now has alpha dilation on. Remove it from AlphaDilationExemptions.");
                return;
            }

            Assert.IsTrue(on,
                $"{name}: without dilation the padding stays black under the transparent pixels, " +
                "so any sampling at an edge averages toward black and the sprite gains a dark " +
                $"fringe. If this atlas genuinely should not have it, add it to " +
                "AlphaDilationExemptions with the reason.");
        }

        // ── Texture ──────────────────────────────────────────────────────────────

        [TestCaseSource(nameof(Atlases))]
        public void FilterMode_IsPoint(string path)
        {
            string name = Name(path);
            var filter = Load(path).GetTextureSettings().filterMode;

            if (PointFilterExemptions.TryGetValue(name, out string why))
            {
                Assert.AreNotEqual(UnityEngine.FilterMode.Point, filter,
                    $"{name} is now Point-filtered. Remove it from PointFilterExemptions — the " +
                    "exemption existed for this reason: " + why);
                return;
            }

            Assert.AreEqual(UnityEngine.FilterMode.Point, filter,
                $"{name}: the atlas filter mode overrides the source PNG's at runtime, so a " +
                "Point-imported tile packed into a Bilinear atlas is sampled bilinearly anyway " +
                "and blurs across its own edge.");
        }

        [TestCaseSource(nameof(Atlases))]
        public void Mipmaps_AreOff(string path)
        {
            Assert.IsFalse(Load(path).GetTextureSettings().generateMipMaps,
                $"{Name(path)}: at any zoom below 1:1 a mip level averages the padding into the " +
                "edge texels. That is the mechanism that turns a clean tile into one with a " +
                "dark border, and it only shows at some zoom levels, which makes it painful to " +
                "track down.");
        }
    }
}
