using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode
{
    /// <summary>
    /// Regression tests for the buildings-editor "Show / Hide Colliders" toggle.
    ///
    /// The toggle is implemented by attaching <see cref="BuildingColliderDebugOverlay"/>
    /// to every BuildingObject and calling <see cref="BuildingColliderDebugOverlay.SetVisible"/>.
    /// These tests lock down the contract the editor relies on:
    ///   - root <c>BoxCollider2D</c> footprint produces a visual,
    ///   - hide deactivates every visual host and zeroes the count,
    ///   - repeated toggles do not duplicate or leak visuals,
    ///   - tiles added after Show are picked up on the next sync,
    ///   - disabled colliders are skipped.
    /// </summary>
    [TestFixture]
    public class BuildingColliderDebugOverlayToggleTests
    {
        private const string VisualPrefix = "_ColliderDebug_";

        [SetUp]
        public void SetUp()
        {
            // Avoid noise from material/renderer leak warnings in EditMode.
            LogAssert.ignoreFailingMessages = true;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Basic Show / Hide
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void SetVisible_True_RootColliderOnly_ActivatesOneVisualHost()
        {
            var go = NewBuilding(rootSize: new Vector2(4f, 2f));
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();

            overlay.SetVisible(true);

            Assert.AreEqual(1, overlay.CurrentVisualCount,
                "Root BoxCollider2D must be visualised by the overlay.");
            Assert.AreEqual(1, CountActiveVisualHosts(go.transform),
                "Exactly one debug visual host must be active under the building.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetVisible_False_DeactivatesAllVisualHosts_AndZeroesCount()
        {
            var go = NewBuilding(rootSize: new Vector2(4f, 2f));
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();

            overlay.SetVisible(true);
            overlay.SetVisible(false);

            Assert.AreEqual(0, overlay.CurrentVisualCount,
                "CurrentVisualCount must reset to 0 after Hide.");
            Assert.AreEqual(0, CountActiveVisualHosts(go.transform),
                "No debug visual host may remain active after Hide.");

            Object.DestroyImmediate(go);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Multiple toggle cycles
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void SetVisible_TogglesShowHideShow_ReactivatesVisuals()
        {
            var go = NewBuilding(rootSize: new Vector2(2f, 2f));
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();

            overlay.SetVisible(true);
            Assert.AreEqual(1, overlay.CurrentVisualCount, "First Show must produce 1 visual.");
            Assert.AreEqual(1, CountActiveVisualHosts(go.transform));

            overlay.SetVisible(false);
            Assert.AreEqual(0, overlay.CurrentVisualCount, "Hide must zero the count.");
            Assert.AreEqual(0, CountActiveVisualHosts(go.transform));

            overlay.SetVisible(true);
            Assert.AreEqual(1, overlay.CurrentVisualCount,
                "Second Show must rebuild/reactivate the visual.");
            Assert.AreEqual(1, CountActiveVisualHosts(go.transform),
                "Second Show must reactivate exactly one visual host.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetVisible_RepeatedShow_DoesNotDuplicateVisuals()
        {
            var go = NewBuilding(rootSize: new Vector2(2f, 2f));
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();

            overlay.SetVisible(true);
            overlay.SetVisible(true);
            overlay.SetVisible(true);

            Assert.AreEqual(1, overlay.CurrentVisualCount,
                "Calling Show repeatedly must not duplicate visuals.");
            Assert.AreEqual(1, CountVisualHosts(go.transform),
                "Only one debug visual host should exist for one collider.");

            Object.DestroyImmediate(go);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Mixed root + tile colliders
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void SetVisible_RootEnabledPlusTwoTiles_ProducesThreeVisuals()
        {
            var go = NewBuilding(rootSize: new Vector2(4f, 2f));
            AddTileCollider(go, "CollTile_0_0", new Vector2(1f, 1f));
            AddTileCollider(go, "CollTile_0_1", new Vector2(1f, 1f));

            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
            overlay.SetVisible(true);

            Assert.AreEqual(3, overlay.CurrentVisualCount,
                "Root + 2 tile colliders must yield 3 visuals.");
            Assert.AreEqual(3, CountActiveVisualHosts(go.transform));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetVisible_AfterAddingNewTile_PicksUpNewColliderOnNextSync()
        {
            var go = NewBuilding(rootSize: new Vector2(2f, 2f));
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();

            overlay.SetVisible(true);
            Assert.AreEqual(1, overlay.CurrentVisualCount);

            // Simulate the brush painting a new tile while the overlay is visible.
            AddTileCollider(go, "CollTile_0_0", new Vector2(1f, 1f));
            overlay.SetVisible(true); // forces re-sync without flipping state

            Assert.AreEqual(2, overlay.CurrentVisualCount,
                "Newly added tile collider must be visualised on the next sync.");
            Assert.AreEqual(2, CountActiveVisualHosts(go.transform));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetVisible_SkipsDisabledColliders()
        {
            var go = NewBuilding(rootSize: new Vector2(2f, 2f));
            // Disable root, add one enabled tile.
            go.GetComponent<BoxCollider2D>().enabled = false;
            AddTileCollider(go, "CollTile_0_0", new Vector2(1f, 1f));

            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
            overlay.SetVisible(true);

            Assert.AreEqual(1, overlay.CurrentVisualCount,
                "Disabled root collider must not produce a visual; only the enabled tile counts.");

            Object.DestroyImmediate(go);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  No collider building edge case
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void SetVisible_NoColliders_ProducesNoVisuals()
        {
            var go = new GameObject("EmptyBuilding");
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();

            overlay.SetVisible(true);

            Assert.AreEqual(0, overlay.CurrentVisualCount,
                "A building with no colliders must not produce any visuals.");
            Assert.AreEqual(0, CountVisualHosts(go.transform));

            Object.DestroyImmediate(go);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Strict child filter (regression: must ignore stray child colliders)
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void SetVisible_IgnoresUnrelatedChildColliders()
        {
            // Root + one CollTile + one stranger (e.g. a pickup or NPC trigger
            // that briefly entered the building hierarchy). The overlay must
            // visualise root + CollTile only, never the stranger.
            var go = NewBuilding(rootSize: new Vector2(2f, 2f));
            AddTileCollider(go, "CollTile_0_0", new Vector2(1f, 1f));
            AddTileCollider(go, "RandomChildCollider", new Vector2(0.5f, 0.5f));

            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
            overlay.SetVisible(true);

            Assert.AreEqual(2, overlay.CurrentVisualCount,
                "Only the root and CollTile_* children must be visualised; unrelated child colliders must be ignored.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetVisible_IgnoresPooledCollTileChildren()
        {
            // Pooled tiles use the "_PooledCollTile_" prefix and represent
            // grid cells the user erased — they must never be visualised even
            // when active in the hierarchy.
            var go = NewBuilding(rootSize: new Vector2(2f, 2f));
            AddTileCollider(go, "CollTile_0_0", new Vector2(1f, 1f));
            AddTileCollider(go, "_PooledCollTile_1_1", new Vector2(1f, 1f));

            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
            overlay.SetVisible(true);

            Assert.AreEqual(2, overlay.CurrentVisualCount,
                "Pooled CollTile children must be excluded from the overlay; root + active CollTile only.");

            Object.DestroyImmediate(go);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────────────────────────────

        private static GameObject NewBuilding(Vector2 rootSize)
        {
            var go = new GameObject("Building");
            var box = go.AddComponent<BoxCollider2D>();
            box.size = rootSize;
            return go;
        }

        private static void AddTileCollider(GameObject parent, string name, Vector2 size)
        {
            var tile = new GameObject(name);
            tile.transform.SetParent(parent.transform, worldPositionStays: false);
            tile.AddComponent<BoxCollider2D>().size = size;
        }

        private static int CountVisualHosts(Transform parent)
        {
            int n = 0;
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name.StartsWith(VisualPrefix)) n++;
            return n;
        }

        private static int CountActiveVisualHosts(Transform parent)
        {
            int n = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name.StartsWith(VisualPrefix) && c.gameObject.activeSelf) n++;
            }
            return n;
        }
    }
}
