using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.World.Layering
{
    /// <summary>
    /// Regression tests for <see cref="VisualLayerColliderSync"/> — the entity-side
    /// piece of the M2 per-visual-layer collision pipeline. The component listens
    /// to the sibling <see cref="VisualLayerOccupant"/>'s layer changes and
    /// rewrites every child <see cref="Collider2D.includeLayers"/> mask so the
    /// Physics2D solver only considers contacts against the matching
    /// <c>WorldL{N}</c> sub-tilemap (+ the always-on <c>WorldAll</c> slot).
    ///
    /// THE contract this fixture guards is the user-facing question:
    ///   <i>"If the Player is on layer 0 and runs into a collider on layer 7,
    ///   they must not collide."</i>
    /// Expressed in the actual data: when CurrentVisualLayer = 0, the player's
    /// Collider2D.includeLayers MUST NOT contain the bit for the WorldL7
    /// physics layer. The smoke test <see cref="PlayerOnLayer0_CannotCollideWithLayer7Collider"/>
    /// asserts exactly that, and the rest of the fixture pins the supporting
    /// invariants (mask matches every layer transition; child colliders are
    /// re-scanned on demand; etc.).
    /// </summary>
    [TestFixture]
    public class VisualLayerColliderSyncTests
    {
        private GameObject _host;
        private VisualLayerOccupant _occupant;
        private VisualLayerColliderSync _sync;
        private BoxCollider2D _collider;

        [SetUp]
        public void SetUp()
        {
            // Re-resolve the layer cache so test order can't poison the lookup.
            WorldCollisionLayers.Invalidate();

            _host = new GameObject("ColliderSyncHost");
            _occupant = _host.AddComponent<VisualLayerOccupant>();
            _collider = _host.AddComponent<BoxCollider2D>();
            _sync = _host.AddComponent<VisualLayerColliderSync>();

            // EditMode does not invoke Awake/OnEnable on AddComponent reliably.
            // VisualLayerSortingSyncTests already proves this is the canonical
            // workaround for layer-sync components — same trick here so the
            // OnLayerChanged subscription is hot AND the initial mask snap is
            // performed before any [Test] body runs.
            ForceLifecycle(_sync);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        private static void ForceLifecycle(VisualLayerColliderSync sync)
        {
            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(VisualLayerColliderSync).GetMethod("Awake", Flags)?.Invoke(sync, null);
            typeof(VisualLayerColliderSync).GetMethod("OnEnable", Flags)?.Invoke(sync, null);
        }

        // ── Initial state at OnEnable -------------------------------------

        [Test]
        public void OnEnable_AppliesLayer0Mask_ByDefault()
        {
            // The occupant defaults to layer 0; the sync must snap the
            // includeLayers mask to "WorldL0 + WorldAll" before the first
            // physics tick — otherwise the player would briefly collide with
            // every painted layer at scene start.
            int expected = WorldCollisionLayers.IncludeMaskFor(0);
            Assert.AreEqual(expected, _collider.includeLayers.value,
                "OnEnable must snap includeLayers to the current visual layer's mask.");
        }

        // ── The user's question, asserted directly ------------------------

        [Test]
        public void PlayerOnLayer0_CannotCollideWithLayer7Collider()
        {
            // Direct assertion of the gameplay contract: an entity on visual
            // layer 0 must NEVER have the WorldL7 bit set in its includeLayers,
            // because that bit is the ONLY thing that would re-enable
            // collisions against cells stamped to WorldL7 (the global Physics2D
            // matrix already ignores Player vs WorldL7 via VisualLayerPhysicsSetup).
            int worldL7 = WorldCollisionLayers.GetWorldLayerIndex(7);
            Assert.GreaterOrEqual(worldL7, 0, "WorldL7 must be defined in TagManager.");

            int includeLayers = _collider.includeLayers.value;
            Assert.AreEqual(0, includeLayers & (1 << worldL7),
                "Player on visual layer 0 must NOT have the WorldL7 bit in includeLayers " +
                "— that would silently re-enable collisions against layer-7 colliders.");
        }

        [Test]
        public void PlayerOnLayer7_CannotCollideWithLayer0Collider()
        {
            // Symmetric to the above: once the player is on layer 7, layer-0
            // colliders must be excluded. Otherwise an elevated player would
            // still be blocked by ground-floor walls.
            _occupant.SetVisualLayer(7);

            int worldL0 = WorldCollisionLayers.GetWorldLayerIndex(0);
            int includeLayers = _collider.includeLayers.value;
            Assert.AreEqual(0, includeLayers & (1 << worldL0),
                "Player on visual layer 7 must NOT have the WorldL0 bit in includeLayers.");
        }

        // ── Mask updates on every transition ------------------------------

        [Test]
        public void LayerTransition_UpdatesIncludeLayers_OnEveryLayer()
        {
            // Walk through 0..8 and verify the mask follows exactly. A failure
            // here means the OnLayerChanged subscription is dead or the
            // ApplyIncludeLayers path no-ops for some layer (regression in
            // the OnEnable→event-subscribe sequence).
            for (int target = 0; target <= VisualLayerOccupant.MaxLayer; target++)
            {
                _occupant.SetVisualLayer(target);
                int expected = WorldCollisionLayers.IncludeMaskFor(target);
                Assert.AreEqual(expected, _collider.includeLayers.value,
                    $"includeLayers must match IncludeMaskFor({target}) after SetVisualLayer({target}).");
            }
        }

        [Test]
        public void OnlyTwoBitsSetAtAnyTime()
        {
            // No matter what layer the player is on, includeLayers must hold
            // exactly one WorldL{N} bit + one WorldAll bit. If a transition
            // ever leaves a stale bit set, the player would collide with two
            // different layers' painted cells simultaneously.
            for (int target = 0; target <= VisualLayerOccupant.MaxLayer; target++)
            {
                _occupant.SetVisualLayer(target);
                int mask = _collider.includeLayers.value;
                Assert.AreEqual(2, CountSetBits(mask),
                    $"includeLayers on visual layer {target} should have exactly 2 bits " +
                    $"set (the layer + WorldAll); got mask 0x{mask:X}.");
            }
        }

        // ── Child collider handling ---------------------------------------

        [Test]
        public void ChildCollider_PresentAtAwake_ReceivesMask()
        {
            // Awake captures the collider list via GetComponentsInChildren<true>,
            // so a collider that already exists on a child at scene-build time
            // must be filtered exactly like the root collider.
            var child = new GameObject("Child");
            child.transform.SetParent(_host.transform, false);
            var childCollider = child.AddComponent<CircleCollider2D>();

            // Re-build with the child present. (Awake-time scan would have
            // missed it otherwise.)
            _sync.RefreshColliderList();

            int expected = WorldCollisionLayers.IncludeMaskFor(0);
            Assert.AreEqual(expected, childCollider.includeLayers.value,
                "Child colliders must be filtered by VisualLayerColliderSync after RefreshColliderList.");

            _occupant.SetVisualLayer(5);
            expected = WorldCollisionLayers.IncludeMaskFor(5);
            Assert.AreEqual(expected, childCollider.includeLayers.value,
                "Child colliders must follow subsequent layer transitions.");
        }

        // ── Helpers -------------------------------------------------------

        private static int CountSetBits(int v)
        {
            int n = 0;
            while (v != 0) { n += v & 1; v = (int)((uint)v >> 1); }
            return n;
        }
    }
}
