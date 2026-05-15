using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Game.Editors.TileEditor.Tags
{
    /// <summary>
    /// M1.10 multi-tag coverage for <see cref="CollisionTagMap"/>. Pins the new
    /// canonicalisation pipeline + mask⇄tag converters that let a single cell
    /// authorise collisions on TWO or more visual layers simultaneously.
    ///
    /// Invariants pinned:
    ///   • <see cref="CollisionTagMap.Canonicalize"/> sorts + dedupes + collapses
    ///     the full set ("0..8") to <see cref="CollisionTagMap.Wildcard"/>.
    ///   • Garbage segments (non-digits, two-char numbers like "10", chars > 8)
    ///     yield <c>null</c> so <see cref="CollisionTagMap.Set"/> can fall back
    ///     to <see cref="CollisionTagMap.Wildcard"/>.
    ///   • <see cref="CollisionTagMap.LayerMaskFromTag"/> and
    ///     <see cref="CollisionTagMap.TagFromLayerMask"/> form a stable
    ///     mask ↔ canonical-string round-trip.
    ///   • <see cref="CollisionTagMap.EnumerateLayers"/> yields ascending indices.
    ///   • The legacy "missing tag ⇒ wildcard" semantic is preserved (empty
    ///     string maps to <see cref="CollisionTagMap.FullLayerMask"/>).
    /// </summary>
    [TestFixture]
    public class CollisionTagMapMultiTagTests
    {
        // ── Canonicalize ─────────────────────────────────────────────────────

        [TestCase("*",   "*")]
        [TestCase("0",   "0")]
        [TestCase("5",   "5")]
        [TestCase("0,2,5", "0,2,5")]
        [TestCase("5,2,0", "0,2,5")] // sort
        [TestCase("5,5,2", "2,5")]   // dedupe
        [TestCase(" 0 , 2 , 5 ", "0,2,5")] // whitespace tolerated
        public void Canonicalize_NormalisesValidInput(string input, string expected)
        {
            Assert.AreEqual(expected, CollisionTagMap.Canonicalize(input));
        }

        [Test]
        public void Canonicalize_AllNineDigits_CollapsesToWildcard()
        {
            // Every single bit set must collapse to "*" — the canonical
            // shortcut. Storing "0,1,2,3,4,5,6,7,8" verbatim would defeat
            // the WorldAll fast-path in the physics baker.
            Assert.AreEqual(CollisionTagMap.Wildcard,
                CollisionTagMap.Canonicalize("0,1,2,3,4,5,6,7,8"));
        }

        [TestCase("garbage")]
        [TestCase("0,9")]       // 9 is out of the 0..8 enum range
        [TestCase("10")]        // two-digit segment is rejected
        [TestCase("0,a")]
        public void Canonicalize_InvalidInput_ReturnsNull(string raw)
        {
            Assert.IsNull(CollisionTagMap.Canonicalize(raw),
                $"Canonicalize must return null on '{raw}'; Set() then falls back to Wildcard.");
        }

        [Test]
        public void Canonicalize_StrayCommas_AreTolerated()
        {
            // The comma-skip loop steps over consecutive commas / whitespace
            // without complaint — empty segments don't corrupt the mask.
            Assert.AreEqual("0,2", CollisionTagMap.Canonicalize("0,,2"));
            Assert.AreEqual("0,2", CollisionTagMap.Canonicalize(",0,2,"));
            Assert.AreEqual("0,2", CollisionTagMap.Canonicalize("0 , , 2"));
        }

        [Test]
        public void Canonicalize_EmptyString_ReturnsNull()
        {
            Assert.IsNull(CollisionTagMap.Canonicalize(""));
            Assert.IsNull(CollisionTagMap.Canonicalize(null));
        }

        // ── LayerMaskFromTag ─────────────────────────────────────────────────

        [Test]
        public void LayerMaskFromTag_Wildcard_IsFullMask()
        {
            Assert.AreEqual(CollisionTagMap.FullLayerMask,
                CollisionTagMap.LayerMaskFromTag("*"));
        }

        [Test]
        public void LayerMaskFromTag_SingleDigit_ReturnsSingleBit()
        {
            Assert.AreEqual(0b000000001, CollisionTagMap.LayerMaskFromTag("0"));
            Assert.AreEqual(0b000000100, CollisionTagMap.LayerMaskFromTag("2"));
            Assert.AreEqual(0b100000000, CollisionTagMap.LayerMaskFromTag("8"));
        }

        [Test]
        public void LayerMaskFromTag_Csv_ReturnsCombinedBits()
        {
            Assert.AreEqual(0b000100101, CollisionTagMap.LayerMaskFromTag("0,2,5"));
            Assert.AreEqual(0b100000001, CollisionTagMap.LayerMaskFromTag("0,8"));
        }

        [Test]
        public void LayerMaskFromTag_EmptyOrGarbage_FallsBackToFullMask()
        {
            // Legacy fallback: missing tag must read as wildcard so pre-M1.10
            // maps continue to behave as "applies to everyone".
            Assert.AreEqual(CollisionTagMap.FullLayerMask, CollisionTagMap.LayerMaskFromTag(""));
            Assert.AreEqual(CollisionTagMap.FullLayerMask, CollisionTagMap.LayerMaskFromTag(null));
            Assert.AreEqual(CollisionTagMap.FullLayerMask, CollisionTagMap.LayerMaskFromTag("garbage"));
        }

        // ── TagFromLayerMask ─────────────────────────────────────────────────

        [Test]
        public void TagFromLayerMask_FullMask_ReturnsWildcard()
        {
            Assert.AreEqual(CollisionTagMap.Wildcard,
                CollisionTagMap.TagFromLayerMask(CollisionTagMap.FullLayerMask));
        }

        [Test]
        public void TagFromLayerMask_ZeroMask_ReturnsEmptyString()
        {
            // Zero = no layers = "no collider applies to anything". The picker
            // surface treats this as "draw disabled until at least one bit is set".
            Assert.AreEqual(string.Empty, CollisionTagMap.TagFromLayerMask(0));
        }

        [Test]
        public void TagFromLayerMask_PartialMask_EmitsSortedCsv()
        {
            Assert.AreEqual("0,2,5", CollisionTagMap.TagFromLayerMask(0b000100101));
            Assert.AreEqual("0,8",   CollisionTagMap.TagFromLayerMask(0b100000001));
            Assert.AreEqual("3",     CollisionTagMap.TagFromLayerMask(0b000001000));
        }

        [Test]
        public void TagFromLayerMask_HighBitsAboveNine_AreIgnored()
        {
            // Caller passes a raw int — we silently trim bits above the 9-layer
            // window so future callers can OR with sentinel bits without
            // contaminating the canonical form.
            int polluted = CollisionTagMap.FullLayerMask | (1 << 9) | (1 << 30);
            Assert.AreEqual(CollisionTagMap.Wildcard,
                CollisionTagMap.TagFromLayerMask(polluted));
        }

        // ── Round-trip ───────────────────────────────────────────────────────

        [TestCase("*")]
        [TestCase("0")]
        [TestCase("8")]
        [TestCase("0,2,5")]
        [TestCase("3,4")]
        [TestCase("0,1,2,3")]
        public void MaskTagRoundTrip_CanonicalForms_AreStable(string canonical)
        {
            int mask = CollisionTagMap.LayerMaskFromTag(canonical);
            string back = CollisionTagMap.TagFromLayerMask(mask);
            Assert.AreEqual(canonical, back,
                $"Canonical '{canonical}' must round-trip via mask without drift.");

            int again = CollisionTagMap.LayerMaskFromTag(back);
            Assert.AreEqual(mask, again,
                "Re-deriving the mask from the round-tripped tag must match the original.");
        }

        // ── EnumerateLayers ──────────────────────────────────────────────────

        [Test]
        public void EnumerateLayers_Csv_YieldsAscendingIndices()
        {
            var actual = CollisionTagMap.EnumerateLayers("0,2,5").ToArray();
            CollectionAssert.AreEqual(new[] { 0, 2, 5 }, actual);
        }

        [Test]
        public void EnumerateLayers_Wildcard_YieldsAllNine()
        {
            var actual = CollisionTagMap.EnumerateLayers("*").ToArray();
            CollectionAssert.AreEqual(Enumerable.Range(0, 9).ToArray(), actual);
        }

        [Test]
        public void EnumerateLayers_Empty_YieldsAllNine()
        {
            // Empty == legacy wildcard fallback — same semantic as missing entry.
            var actual = CollisionTagMap.EnumerateLayers("").ToArray();
            CollectionAssert.AreEqual(Enumerable.Range(0, 9).ToArray(), actual);
        }

        // ── Set canonicalises before storing ─────────────────────────────────

        [Test]
        public void Set_RawCsv_StoresCanonicalForm()
        {
            var map = new CollisionTagMap();
            map.Set(new Vector2Int(5, 5), "5,2,0,2");

            Assert.AreEqual("0,2,5", map.Get(new Vector2Int(5, 5)),
                "Set must sort + dedupe before storing.");
        }

        [Test]
        public void Set_AllNineDigits_CollapsesToWildcard()
        {
            var map = new CollisionTagMap();
            map.Set(new Vector2Int(5, 5), "0,1,2,3,4,5,6,7,8");

            Assert.AreEqual(CollisionTagMap.Wildcard, map.Get(new Vector2Int(5, 5)));
        }

        [Test]
        public void Set_GarbageTag_FallsBackToWildcard()
        {
            var map = new CollisionTagMap();
            map.Set(new Vector2Int(5, 5), "garbage");
            Assert.AreEqual(CollisionTagMap.Wildcard, map.Get(new Vector2Int(5, 5)));
        }

        [Test]
        public void IsValidTag_AcceptsCanonicalAndRawCsv()
        {
            Assert.IsTrue(CollisionTagMap.IsValidTag("*"));
            Assert.IsTrue(CollisionTagMap.IsValidTag("4"));
            Assert.IsTrue(CollisionTagMap.IsValidTag("0,2,5"));
            Assert.IsTrue(CollisionTagMap.IsValidTag("5,2,0"));      // raw → canonicalisable
            Assert.IsFalse(CollisionTagMap.IsValidTag("9"));
            Assert.IsFalse(CollisionTagMap.IsValidTag("garbage"));
            Assert.IsFalse(CollisionTagMap.IsValidTag(""));
            Assert.IsFalse(CollisionTagMap.IsValidTag(null));
        }
    }
}
