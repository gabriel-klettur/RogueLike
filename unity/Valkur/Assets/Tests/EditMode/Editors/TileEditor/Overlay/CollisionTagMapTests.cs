using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Overlay
{
    /// <summary>
    /// Pins the contract of <see cref="CollisionTagMap"/>: per-cell tag storage that
    /// determines which visual layer(s) a collision cell applies to. The map is the
    /// foundation of the M1 per-visual-layer collisions feature — every other layer
    /// (serialization, painter, overlay) reads from / writes to this class.
    ///
    /// Invariants pinned here:
    ///   • Missing entries resolve to <see cref="CollisionTagMap.Wildcard"/> (legacy
    ///     "applies to all entities" behaviour preserved by default).
    ///   • Set/Clear are idempotent; invalid tag strings clamp to wildcard rather than
    ///     corrupting the map.
    ///   • BuildMatrix / LoadMatrix round-trip without losing data; HasAnyInRect lets
    ///     serialization skip the field when the zone has never been authored.
    /// </summary>
    [TestFixture]
    public class CollisionTagMapTests
    {
        private CollisionTagMap _map;

        [SetUp]
        public void SetUp() => _map = new CollisionTagMap();

        [Test]
        public void Get_Missing_ReturnsWildcard()
        {
            Assert.AreEqual(CollisionTagMap.Wildcard,
                _map.Get(new Vector2Int(0, 0)),
                "Missing cells must default to the wildcard ('*') so legacy maps load with pre-feature semantics.");
        }

        [Test]
        public void Set_StoresAndReadsBackExact()
        {
            _map.Set(new Vector2Int(3, 7), "4");
            Assert.AreEqual("4", _map.Get(new Vector2Int(3, 7)));
            Assert.AreEqual(1, _map.Count);
        }

        [Test]
        public void Set_EmptyOrNull_ClearsEntry()
        {
            _map.Set(new Vector2Int(1, 1), "2");
            _map.Set(new Vector2Int(1, 1), "");
            Assert.AreEqual(CollisionTagMap.Wildcard, _map.Get(new Vector2Int(1, 1)),
                "Empty string must clear back to the wildcard default, not store an empty literal.");
            Assert.AreEqual(0, _map.Count);
        }

        [Test]
        public void Set_InvalidTag_ClampsToWildcard()
        {
            _map.Set(new Vector2Int(0, 0), "9");      // out of 0..8 range
            _map.Set(new Vector2Int(0, 1), "abc");    // not a digit / wildcard
            _map.Set(new Vector2Int(0, 2), "-1");

            Assert.AreEqual(CollisionTagMap.Wildcard, _map.Get(new Vector2Int(0, 0)));
            Assert.AreEqual(CollisionTagMap.Wildcard, _map.Get(new Vector2Int(0, 1)));
            Assert.AreEqual(CollisionTagMap.Wildcard, _map.Get(new Vector2Int(0, 2)));
        }

        [Test]
        public void IsValidTag_AcceptsWildcardAndDigits0Through8()
        {
            Assert.IsTrue(CollisionTagMap.IsValidTag("*"));
            for (int i = 0; i <= 8; i++)
                Assert.IsTrue(CollisionTagMap.IsValidTag(i.ToString()), $"Index {i} must be a valid tag.");
            Assert.IsFalse(CollisionTagMap.IsValidTag("9"));
            Assert.IsFalse(CollisionTagMap.IsValidTag(""));
            Assert.IsFalse(CollisionTagMap.IsValidTag(null));
            Assert.IsFalse(CollisionTagMap.IsValidTag("10"));
            Assert.IsFalse(CollisionTagMap.IsValidTag("foo"));
        }

        [Test]
        public void ValidTags_IsTenEntriesWildcardFirst()
        {
            Assert.AreEqual(10, CollisionTagMap.ValidTags.Length);
            Assert.AreEqual(CollisionTagMap.Wildcard, CollisionTagMap.ValidTags[0]);
            for (int i = 1; i < 10; i++)
                Assert.AreEqual((i - 1).ToString(), CollisionTagMap.ValidTags[i]);
        }

        [Test]
        public void BuildMatrix_LoadMatrix_RoundTripPreservesValues()
        {
            // Author a recognisable 2x3 pattern.
            _map.Set(new Vector2Int(0, 0), "0");
            _map.Set(new Vector2Int(1, 0), "*");
            _map.Set(new Vector2Int(2, 1), "4");

            string[,] matrix = _map.BuildMatrix(originX: 0, originY: 0, w: 3, h: 2);

            var roundTrip = new CollisionTagMap();
            roundTrip.LoadMatrix(originX: 0, originY: 0, matrix);

            Assert.AreEqual("0", roundTrip.Get(new Vector2Int(0, 0)));
            Assert.AreEqual("*", roundTrip.Get(new Vector2Int(1, 0)));
            Assert.AreEqual("4", roundTrip.Get(new Vector2Int(2, 1)));
            Assert.AreEqual(CollisionTagMap.Wildcard,
                roundTrip.Get(new Vector2Int(0, 1)),
                "Empty matrix slots must NOT introduce explicit wildcard entries on load.");
        }

        [Test]
        public void HasAnyInRect_FalseWhenEmpty_TrueWhenAnyEntry()
        {
            Assert.IsFalse(_map.HasAnyInRect(0, 0, 5, 5),
                "Empty map → HasAnyInRect must be false so serialization can skip the field.");

            _map.Set(new Vector2Int(2, 3), "5");
            Assert.IsTrue(_map.HasAnyInRect(0, 0, 5, 5));
            Assert.IsFalse(_map.HasAnyInRect(10, 10, 5, 5),
                "Entry exists but outside the queried rect — must remain false.");
        }

        [Test]
        public void ClearAll_RemovesEveryEntry()
        {
            _map.Set(new Vector2Int(0, 0), "0");
            _map.Set(new Vector2Int(1, 1), "1");
            _map.ClearAll();
            Assert.AreEqual(0, _map.Count);
            Assert.AreEqual(CollisionTagMap.Wildcard, _map.Get(new Vector2Int(0, 0)));
        }
    }
}
