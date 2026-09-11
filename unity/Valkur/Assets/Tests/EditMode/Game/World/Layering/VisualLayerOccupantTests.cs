using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.World.Layering
{
    /// <summary>
    /// Pin the public contract of <see cref="VisualLayerOccupant"/>: clamped range,
    /// no-op semantics on identical writes, single OnLayerChanged fire per real
    /// transition, enum/int convertibility. The component is the cornerstone of
    /// M2's per-layer collision filtering — a regression here would silently break
    /// the entire gameplay layer model.
    /// </summary>
    [TestFixture]
    public class VisualLayerOccupantTests
    {
        private GameObject _host;
        private VisualLayerOccupant _occ;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("OccupantHost");
            _occ = _host.AddComponent<VisualLayerOccupant>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_host);

        [Test]
        public void Default_IsGroundLayerZero()
        {
            Assert.AreEqual(0, _occ.CurrentVisualLayer);
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.Ground, _occ.CurrentLayer);
            Assert.AreEqual("Ground", _occ.LayerName);
        }

        [Test]
        public void SetVisualLayer_ValidIndex_UpdatesAndFiresEventOnce()
        {
            int fireCount = 0;
            int oldSeen = -1, newSeen = -1;
            _occ.OnLayerChanged += (o, n) => { fireCount++; oldSeen = o; newSeen = n; };

            _occ.SetVisualLayer(4);

            Assert.AreEqual(1, fireCount, "Single real transition must fire OnLayerChanged exactly once.");
            Assert.AreEqual(0, oldSeen);
            Assert.AreEqual(4, newSeen);
            Assert.AreEqual(4, _occ.CurrentVisualLayer);
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.WallsBottom, _occ.CurrentLayer);
        }

        [Test]
        public void SetVisualLayer_SameValue_IsNoOpAndDoesNotFire()
        {
            _occ.SetVisualLayer(3);

            int fireCount = 0;
            _occ.OnLayerChanged += (_, _) => fireCount++;

            _occ.SetVisualLayer(3);
            Assert.AreEqual(0, fireCount,
                "Setting the same value must not re-fire the event — listeners rely on this to drive expensive recomputations only on real transitions.");
            Assert.AreEqual(3, _occ.CurrentVisualLayer);
        }

        [TestCase(-5)]
        [TestCase(-1)]
        public void SetVisualLayer_BelowRange_ClampsToZero(int input)
        {
            _occ.SetVisualLayer(input);
            Assert.AreEqual(VisualLayerOccupant.MinLayer, _occ.CurrentVisualLayer,
                "Out-of-range input " + input + " must clamp so authoring bugs never produce undefined layers.");
        }

        [Test]
        public void SetVisualLayer_AboveRange_ClampsToTheTopLayer()
        {
            // Derived: this was TestCase(9, 8) and TestCase(100, 8), and 9 became a real layer
            // the day the ladder grew — a case pinned to the old size reports a correct build
            // as broken while no longer testing the clamp at all.
            foreach (int input in new[] { VisualLayerOccupant.MaxLayer + 1, 100 })
            {
                _occ.SetVisualLayer(0);
                _occ.SetVisualLayer(input);
                Assert.AreEqual(VisualLayerOccupant.MaxLayer, _occ.CurrentVisualLayer,
                    "Out-of-range input " + input + " must clamp to the top layer.");
            }
        }

        [Test]
        public void SetVisualLayer_OutOfRangeMatchingClamp_DoesNotFireWhenAtBoundary()
        {
            // Already at 0; setting -5 clamps to 0 → no transition → no event.
            int fireCount = 0;
            _occ.OnLayerChanged += (_, _) => fireCount++;
            _occ.SetVisualLayer(-5);
            Assert.AreEqual(0, fireCount,
                "Clamped value equal to current value must still be treated as a no-op.");
        }

        [Test]
        public void SetVisualLayer_EnumOverload_DelegatesToIntPath()
        {
            int newSeen = -1;
            _occ.OnLayerChanged += (_, n) => newSeen = n;

            _occ.SetVisualLayer(TilemapLayerSetup.TilemapLayer.WallsTop);
            Assert.AreEqual(6, _occ.CurrentVisualLayer);
            Assert.AreEqual(6, newSeen);
        }
    }
}
