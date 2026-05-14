using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.World.Layering
{
    /// <summary>
    /// Pin the contract of <see cref="LayerJumpMap"/> — the data store every M1.8
    /// consumer reads from (Tile Editor painter, GL overlay, runtime trigger
    /// system, persistence). A regression here cascades into every layer of
    /// the feature, so the suite intentionally targets the failure modes that
    /// would be hardest to debug downstream.
    /// </summary>
    [TestFixture]
    public class LayerJumpMapTests
    {
        private LayerJumpMap _map;

        [SetUp]
        public void SetUp() => _map = new LayerJumpMap();

        [Test]
        public void Get_Missing_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, _map.Get(new Vector2Int(0, 0)),
                "Missing cells must read as empty string (no jump) so the runtime " +
                "trigger never parses garbage into a fake transition.");
        }

        [Test]
        public void Set_ValidTarget_StoresAndReadsBack()
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
            Assert.AreEqual(string.Empty, _map.Get(new Vector2Int(1, 1)));
            Assert.AreEqual(0, _map.Count);
        }

        [Test]
        public void Set_InvalidTarget_RemovesInsteadOfStoringGarbage()
        {
            _map.Set(new Vector2Int(0, 0), "9");      // out of 0..8 range
            _map.Set(new Vector2Int(0, 1), "*");      // wildcard not valid for jumps
            _map.Set(new Vector2Int(0, 2), "abc");
            _map.Set(new Vector2Int(0, 3), "-1");

            Assert.AreEqual(string.Empty, _map.Get(new Vector2Int(0, 0)),
                "Out-of-range target must NOT store — runtime would parse it and crash.");
            Assert.AreEqual(string.Empty, _map.Get(new Vector2Int(0, 1)),
                "Wildcard '*' is for CollisionTagMap; LayerJumpMap rejects it.");
            Assert.AreEqual(string.Empty, _map.Get(new Vector2Int(0, 2)));
            Assert.AreEqual(string.Empty, _map.Get(new Vector2Int(0, 3)));
            Assert.AreEqual(0, _map.Count);
        }

        [Test]
        public void IsValidTarget_AcceptsZeroThroughEight()
        {
            for (int i = 0; i <= 8; i++)
                Assert.IsTrue(LayerJumpMap.IsValidTarget(i.ToString()), $"Index {i} must be valid.");
            Assert.IsFalse(LayerJumpMap.IsValidTarget("9"));
            Assert.IsFalse(LayerJumpMap.IsValidTarget("*"));
            Assert.IsFalse(LayerJumpMap.IsValidTarget(""));
            Assert.IsFalse(LayerJumpMap.IsValidTarget(null));
            Assert.IsFalse(LayerJumpMap.IsValidTarget("10"));
            Assert.IsFalse(LayerJumpMap.IsValidTarget("foo"));
        }

        [Test]
        public void BuildMatrix_LoadMatrix_RoundTripPreservesValues()
        {
            // Author a 2x3 pattern.
            _map.Set(new Vector2Int(0, 0), "0");
            _map.Set(new Vector2Int(1, 0), "4");
            _map.Set(new Vector2Int(2, 1), "8");

            string[,] matrix = _map.BuildMatrix(originX: 0, originY: 0, w: 3, h: 2);

            var roundTrip = new LayerJumpMap();
            roundTrip.LoadMatrix(originX: 0, originY: 0, matrix);

            Assert.AreEqual("0", roundTrip.Get(new Vector2Int(0, 0)));
            Assert.AreEqual("4", roundTrip.Get(new Vector2Int(1, 0)));
            Assert.AreEqual("8", roundTrip.Get(new Vector2Int(2, 1)));
            Assert.AreEqual(string.Empty, roundTrip.Get(new Vector2Int(0, 1)),
                "Empty matrix slots must NOT introduce explicit entries on load.");
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
            Assert.AreEqual(string.Empty, _map.Get(new Vector2Int(0, 0)));
        }
    }
}
