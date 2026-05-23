using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.World.Layering
{
    /// <summary>
    /// Regression tests for <see cref="VisualLayerPhysicsSetup"/> — the global
    /// Physics2D layer-collision matrix configuration that flips
    /// <c>Player vs WorldL0..WorldL8 = IGNORE</c> at boot. Together with
    /// <see cref="VisualLayerColliderSync"/>'s per-collider <c>includeLayers</c>
    /// override, this matrix configuration is what makes "Player on layer 0
    /// does not collide with collider on layer 7" hold true even when
    /// <c>includeLayers</c> is left at its default (e.g. NPCs that haven't
    /// adopted the per-layer filter yet still inherit the matrix's IGNORE).
    ///
    /// The configure routine is decorated with <see cref="RuntimeInitializeOnLoadMethodAttribute"/>
    /// and is private — we invoke it via reflection. Tests save / restore the
    /// global matrix on Setup/Teardown so the suite's other Physics2D tests
    /// don't observe leaked state.
    /// </summary>
    [TestFixture]
    public class VisualLayerPhysicsSetupTests
    {
        private bool[] _savedIgnoreVsWorld;
        private bool _savedIgnoreVsWorldAll;
        private int _playerLayer;

        [SetUp]
        public void SetUp()
        {
            WorldCollisionLayers.Invalidate();
            _playerLayer = LayerMask.NameToLayer("Player");
            Assert.GreaterOrEqual(_playerLayer, 0, "Physics layer 'Player' must be defined.");

            // Snapshot the matrix so we restore it on teardown.
            _savedIgnoreVsWorld = new bool[WorldCollisionLayers.LayerCount];
            for (int i = 0; i < WorldCollisionLayers.LayerCount; i++)
            {
                int wl = WorldCollisionLayers.GetWorldLayerIndex(i);
                if (wl >= 0)
                    _savedIgnoreVsWorld[i] = Physics2D.GetIgnoreLayerCollision(_playerLayer, wl);
            }
            int worldAll = WorldCollisionLayers.GetWorldAllIndex();
            if (worldAll >= 0)
                _savedIgnoreVsWorldAll = Physics2D.GetIgnoreLayerCollision(_playerLayer, worldAll);

            // Apply Configure() so the asserts below observe a fresh, post-
            // Configure state regardless of who ran last.
            InvokeConfigure();
        }

        [TearDown]
        public void TearDown()
        {
            // Restore the previous matrix state so subsequent fixtures are
            // not poisoned. We always set BACK to the snapshot — even if the
            // snapshot already matched, this is a no-op.
            for (int i = 0; i < WorldCollisionLayers.LayerCount; i++)
            {
                int wl = WorldCollisionLayers.GetWorldLayerIndex(i);
                if (wl >= 0)
                    Physics2D.IgnoreLayerCollision(_playerLayer, wl, _savedIgnoreVsWorld[i]);
            }
            int worldAll = WorldCollisionLayers.GetWorldAllIndex();
            if (worldAll >= 0)
                Physics2D.IgnoreLayerCollision(_playerLayer, worldAll, _savedIgnoreVsWorldAll);
        }

        private static void InvokeConfigure()
        {
            var method = typeof(VisualLayerPhysicsSetup).GetMethod("Configure",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "VisualLayerPhysicsSetup.Configure not found via reflection.");
            method.Invoke(null, null);
        }

        // ── The contract ---------------------------------------------------

        [Test]
        public void Configure_IgnoresPlayerVsEveryWorldLayer()
        {
            // The whole 0..8 range must be IGNORE'd. The per-collider
            // includeLayers override is what re-enables exactly one slot;
            // skipping any layer here would let the player permanently
            // collide with that layer regardless of CurrentVisualLayer.
            for (int i = 0; i < WorldCollisionLayers.LayerCount; i++)
            {
                int wl = WorldCollisionLayers.GetWorldLayerIndex(i);
                Assert.IsTrue(Physics2D.GetIgnoreLayerCollision(_playerLayer, wl),
                    $"Physics2D matrix must ignore Player vs WorldL{i} after Configure().");
            }
        }

        [Test]
        public void Configure_DoesNotIgnorePlayerVsWorldAll()
        {
            // WorldAll is the wildcard slot — every entity must collide with
            // cells stamped to it on every layer. If this gets flipped to
            // IGNORE, wildcard colliders silently stop blocking the player.
            int worldAll = WorldCollisionLayers.GetWorldAllIndex();
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(_playerLayer, worldAll),
                "Physics2D matrix must NOT ignore Player vs WorldAll — " +
                "wildcard colliders must always block the player.");
        }

        [Test]
        public void Configure_IsIdempotent()
        {
            // Domain Reload OFF means the matrix can survive across Play
            // sessions. Re-invoking Configure on top of an already-configured
            // matrix must be a no-op observation-wise — same IGNOREs for the
            // per-layer slots, same NOT-IGNORE for WorldAll.
            InvokeConfigure();
            InvokeConfigure();

            for (int i = 0; i < WorldCollisionLayers.LayerCount; i++)
            {
                int wl = WorldCollisionLayers.GetWorldLayerIndex(i);
                Assert.IsTrue(Physics2D.GetIgnoreLayerCollision(_playerLayer, wl),
                    $"Repeated Configure() must keep Player vs WorldL{i} = ignore.");
            }
            int worldAll = WorldCollisionLayers.GetWorldAllIndex();
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(_playerLayer, worldAll),
                "Repeated Configure() must keep Player vs WorldAll = collide.");
        }
    }
}
