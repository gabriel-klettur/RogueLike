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
        //  Authoring mode (single source of truth = caller-supplied cell rects)
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void SetAuthoringCells_RendersOneVisualPerSuppliedRect()
        {
            // A building with NO BoxCollider2D — the authoring cells are the
            // only thing the overlay should render. Proves the authoring path
            // is fully decoupled from the BoxCollider2D enumeration.
            var go = new GameObject("Building");
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
            var cells = new[]
            {
                new Rect(0f, 0f, 1f, 1f),
                new Rect(1f, 0f, 1f, 1f),
                new Rect(0f, 1f, 1f, 1f),
            };

            overlay.SetAuthoringCells(cells);
            overlay.SetVisible(true);

            Assert.IsTrue(overlay.IsAuthoringMode);
            Assert.AreEqual(3, overlay.AuthoringCellCount);
            Assert.AreEqual(3, overlay.CurrentVisualCount);
            Assert.AreEqual(3, CountActiveVisualHosts(go.transform));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetAuthoringCells_OverridesBoxColliderRendering()
        {
            // Building has both a root collider AND child CollTiles, but
            // authoring mode supplies a single cell — only that cell renders.
            // This is the exact contract that fixes the editor drift bug:
            // when authoring mode is active the visual ignores the live
            // physics shapes entirely.
            var go = NewBuilding(rootSize: new Vector2(4f, 4f));
            AddTileCollider(go, "CollTile_0_0", new Vector2(1f, 1f));
            AddTileCollider(go, "CollTile_1_1", new Vector2(1f, 1f));

            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
            overlay.SetAuthoringCells(new[] { new Rect(5f, 5f, 0.5f, 0.5f) });
            overlay.SetVisible(true);

            Assert.AreEqual(1, overlay.CurrentVisualCount,
                "Authoring mode must drive the visual count, not the BoxCollider2D children.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void ClearAuthoringCells_RevertsToBoxColliderInferredRendering()
        {
            // Round-trip: enter authoring mode, then exit. Should resume
            // rendering the live BoxCollider2D shapes (root + tiles).
            var go = NewBuilding(rootSize: new Vector2(2f, 2f));
            AddTileCollider(go, "CollTile_0_0", new Vector2(1f, 1f));
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();

            overlay.SetAuthoringCells(new[] { new Rect(0f, 0f, 1f, 1f) });
            overlay.SetVisible(true);
            Assert.AreEqual(1, overlay.CurrentVisualCount);
            Assert.IsTrue(overlay.IsAuthoringMode);

            overlay.ClearAuthoringCells();
            Assert.IsFalse(overlay.IsAuthoringMode);
            Assert.AreEqual(0, overlay.AuthoringCellCount);
            Assert.AreEqual(2, overlay.CurrentVisualCount,
                "After clearing authoring mode, root + CollTile must render again.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetAuthoringCells_NullOrEmpty_ProducesNoVisuals()
        {
            var go = NewBuilding(rootSize: new Vector2(2f, 2f));
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();

            overlay.SetAuthoringCells(null);
            overlay.SetVisible(true);
            Assert.IsTrue(overlay.IsAuthoringMode);
            Assert.AreEqual(0, overlay.CurrentVisualCount,
                "Authoring mode with null cells must render no visuals (root collider must be ignored).");

            overlay.SetAuthoringCells(new Rect[0]);
            Assert.AreEqual(0, overlay.CurrentVisualCount,
                "Authoring mode with empty cells must render no visuals.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetAuthoringCells_RepeatedReplacement_DoesNotLeakVisuals()
        {
            // Stress: replace the cell list 10 times alternating large/small
            // counts. Inactive visuals must be deactivated (not destroyed +
            // recreated) and CurrentVisualCount must always reflect the
            // current cell count exactly.
            var go = new GameObject("Building");
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
            overlay.SetVisible(true);

            for (int i = 0; i < 10; i++)
            {
                int n = (i % 2 == 0) ? 5 : 1;
                var rects = new Rect[n];
                for (int k = 0; k < n; k++)
                    rects[k] = new Rect(k, 0, 1, 1);

                overlay.SetAuthoringCells(rects);
                Assert.AreEqual(n, overlay.CurrentVisualCount, $"Iteration {i}: visual count must equal cell count.");
                Assert.AreEqual(n, CountActiveVisualHosts(go.transform), $"Iteration {i}: active visual hosts must equal cell count.");
            }

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetAuthoringCells_PositionsVisualsAtSuppliedWorldRect()
        {
            // Geometry contract: the SpriteRenderer fill must sit at the
            // CENTER of each supplied world rect, with localScale matching
            // the rect size (after the parent's inverse-scale compensation).
            // This is what guarantees click coordinates and visual feedback
            // share one coordinate system.
            var go = new GameObject("Building");
            go.transform.position = new Vector3(10f, 20f, 0f);
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
            var cell = new Rect(7f, 13f, 2f, 4f);
            overlay.SetAuthoringCells(new[] { cell });
            overlay.SetVisible(true);

            // Find the visual host (only one)
            Transform host = null;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                var c = go.transform.GetChild(i);
                if (c.name.StartsWith(VisualPrefix) && c.gameObject.activeSelf) { host = c; break; }
            }
            Assert.IsNotNull(host, "Authoring mode must produce one active visual host.");

            // Host position == cell center in world space.
            Vector2 expectedCenter = cell.center;
            Assert.AreEqual(expectedCenter.x, host.position.x, 0.001f, "Visual host X must equal cell center X.");
            Assert.AreEqual(expectedCenter.y, host.position.y, 0.001f, "Visual host Y must equal cell center Y.");

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
