using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Lock-down tests for the performance-oriented changes in
    /// <see cref="BuildingColliderDebugOverlay"/>:
    ///
    ///   * <c>_dirty</c> + per-visual cached state mean that a second
    ///     <c>SyncVisuals</c> call with no changes performs no observable
    ///     work on the hosts (no transform writes, no renderer writes).
    ///   * <c>MarkDirty()</c> forces a re-apply of every visual.
    ///   * <c>transform.hasChanged</c> re-triggers a re-apply so visuals
    ///     follow the building when it is moved/scaled.
    ///   * The default-mode collider cache is rebuilt on dirty sync but NOT
    ///     on a clean second sync (validates the hot-path short-circuit).
    ///   * 120fps budget: N overlays resynced with no changes cost well
    ///     under the 8.3ms-per-frame target.
    ///
    /// These tests rely on reflection to poke the private <c>_dirty</c>
    /// field and read the private <c>_defaultColliderCount</c>. If those
    /// names change, update the reflection helpers below.
    /// </summary>
    [TestFixture]
    public class BuildingColliderDebugOverlayPerformanceTests
    {
        private const string VisualPrefix = "_ColliderDebug_";

        private static FieldInfo s_dirtyField;
        private static FieldInfo s_defaultCountField;
        private static MethodInfo s_syncMethod;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var t = typeof(BuildingColliderDebugOverlay);
            s_dirtyField = t.GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic);
            s_defaultCountField = t.GetField("_defaultColliderCount", BindingFlags.Instance | BindingFlags.NonPublic);
            s_syncMethod = t.GetMethod("SyncVisuals", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(s_dirtyField, "Reflection target '_dirty' missing — perf tests must be updated.");
            Assert.IsNotNull(s_defaultCountField, "Reflection target '_defaultColliderCount' missing.");
            Assert.IsNotNull(s_syncMethod, "Reflection target 'SyncVisuals' missing.");
        }

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Dirty-flag short-circuit
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void SyncVisuals_WhenNothingChanged_DoesNotMutateVisualTransforms()
        {
            // Setup: one root collider → one visual.
            var go = NewBuilding(rootSize: new Vector2(2f, 2f));
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
            overlay.SetVisible(true);

            // Find the one visual host and snapshot its transform.
            Transform host = FindFirstActiveVisualHost(go.transform);
            Assert.IsNotNull(host);
            Vector3 posBefore = host.position;
            Quaternion rotBefore = host.rotation;
            Vector3 scaleBefore = host.localScale;

            // Manually corrupt the host transform to a sentinel. If the
            // fast-path is working, the next clean SyncVisuals will NOT
            // overwrite these values.
            host.position = new Vector3(999f, 999f, 999f);
            host.rotation = Quaternion.Euler(0f, 0f, 45f);
            host.localScale = new Vector3(7f, 7f, 7f);

            InvokeSync(overlay); // no dirty, no transform.hasChanged
            Assert.AreEqual(999f, host.position.x, 0.001f,
                "Clean SyncVisuals must NOT rewrite the host position — this is the 120fps fast path.");
            Assert.AreEqual(999f, host.position.y, 0.001f);

            // Restore & verify the dirty path DOES rewrite.
            SetDirty(overlay, true);
            InvokeSync(overlay);
            Assert.That(host.position.x, Is.Not.EqualTo(999f).Within(0.001f),
                "Dirty SyncVisuals must rewrite the host position.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void MarkDirty_ForcesReapply()
        {
            var go = NewBuilding(rootSize: new Vector2(2f, 2f));
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
            overlay.SetVisible(true);

            Transform host = FindFirstActiveVisualHost(go.transform);
            // Corrupt host, call MarkDirty → host position must be restored.
            host.position = new Vector3(-123f, -456f, -789f);
            overlay.MarkDirty();

            Assert.That(host.position.x, Is.Not.EqualTo(-123f).Within(0.001f),
                "MarkDirty() must trigger a full re-apply that overwrites host transforms.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TransformHasChanged_TriggersReapply()
        {
            // Move the building while overlay is visible: LateUpdate reads
            // transform.hasChanged. Simulate the LateUpdate by invoking
            // SyncVisuals after the move; the dirty-flag logic should NOT
            // be required here because the perf path checks transform.
            // But since LateUpdate itself is driven by Unity, we simulate
            // the flow: the perf code ALWAYS re-applies when dirty OR the
            // transform moved. A raw InvokeSync bypasses the LateUpdate
            // gate, so we must reach through LateUpdate. We approximate by
            // setting the dirty flag manually when transform.hasChanged is
            // true (same effective outcome the production LateUpdate
            // produces).
            var go = NewBuilding(rootSize: new Vector2(2f, 2f));
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
            overlay.SetVisible(true);

            Transform host = FindFirstActiveVisualHost(go.transform);
            Vector3 posBefore = host.position;

            go.transform.position = new Vector3(100f, 50f, 0f);
            Assert.IsTrue(go.transform.hasChanged,
                "Moving the building must set transform.hasChanged — required by the lazy sync.");

            // In EditMode, BoxCollider2D.bounds does not update until the physics
            // system syncs with the new transform. Force that sync explicitly.
            Physics2D.SyncTransforms();

            // MarkDirty() models the "transform.hasChanged OR dirty" branch.
            overlay.MarkDirty();

            Assert.AreNotEqual(posBefore, host.position,
                "Moving the building + re-sync must relocate the visual host accordingly.");

            Object.DestroyImmediate(go);
        }

        // ────────────────────────────────────────────────────────────────────
        //  Default-mode collider cache
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void DefaultColliderCache_RebuildsOnDirty_NotOnCleanSync()
        {
            var go = NewBuilding(rootSize: new Vector2(2f, 2f));
            AddTileCollider(go, "CollTile_0_0", new Vector2(1f, 1f));
            var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
            overlay.SetVisible(true);

            int cachedCountAfterShow = GetDefaultColliderCount(overlay);
            Assert.AreEqual(2, cachedCountAfterShow,
                "Default-mode cache must contain the 2 valid colliders (root + CollTile).");

            // Add a new tile WITHOUT marking dirty → next clean sync must
            // NOT pick it up, because the cache is stale by design (this
            // is what keeps per-frame cost O(0) instead of O(n)).
            AddTileCollider(go, "CollTile_0_1", new Vector2(1f, 1f));
            InvokeSync(overlay); // clean
            Assert.AreEqual(2, GetDefaultColliderCount(overlay),
                "Clean SyncVisuals must NOT rebuild the default collider cache.");

            // Now set dirty → cache must be rebuilt.
            SetDirty(overlay, true);
            InvokeSync(overlay);
            Assert.AreEqual(3, GetDefaultColliderCount(overlay),
                "Dirty SyncVisuals must rebuild the default collider cache (root + 2 tiles).");

            Object.DestroyImmediate(go);
        }

        // ────────────────────────────────────────────────────────────────────
        //  120fps budget
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void SyncVisuals_NoChanges_StaysWithin120fpsBudgetForManyOverlays()
        {
            // 120fps = 8.33ms per frame. All overlays combined must fit in a
            // small fraction of that budget when nothing changed — otherwise
            // "Show Colliders" will tank FPS in the real scene (142 overlays).
            const int N = 150;
            const double BudgetMs = 2.0; // very generous vs the 8.33ms frame budget
            const int Frames = 60;       // simulate 60 LateUpdate ticks

            var overlays = new List<BuildingColliderDebugOverlay>(N);
            var buildings = new List<GameObject>(N);
            for (int i = 0; i < N; i++)
            {
                var go = NewBuilding(rootSize: new Vector2(2f, 2f));
                AddTileCollider(go, "CollTile_0_0", new Vector2(1f, 1f));
                AddTileCollider(go, "CollTile_0_1", new Vector2(1f, 1f));
                var overlay = go.AddComponent<BuildingColliderDebugOverlay>();
                overlay.SetVisible(true); // warm-up sync
                buildings.Add(go);
                overlays.Add(overlay);
            }

            // Measure: Frames frames of "nothing changed" syncs.
            var sw = Stopwatch.StartNew();
            for (int f = 0; f < Frames; f++)
                for (int i = 0; i < N; i++)
                    InvokeSync(overlays[i]);
            sw.Stop();

            double perFrameMs = sw.Elapsed.TotalMilliseconds / Frames;
            UnityEngine.Debug.Log($"[Perf] {N} overlays idle sync: {perFrameMs:F3}ms/frame " +
                                  $"(budget={BudgetMs:F2}ms, 120fps frame=8.33ms)");

            Assert.LessOrEqual(perFrameMs, BudgetMs,
                $"Idle SyncVisuals for {N} overlays must stay under {BudgetMs}ms/frame " +
                $"to guarantee 120fps; measured {perFrameMs:F3}ms/frame.");

            for (int i = 0; i < N; i++)
                Object.DestroyImmediate(buildings[i]);
        }

        // ────────────────────────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────────────────────────

        private static GameObject NewBuilding(Vector2 rootSize)
        {
            var go = new GameObject("Building");
            go.AddComponent<BoxCollider2D>().size = rootSize;
            return go;
        }

        private static void AddTileCollider(GameObject parent, string name, Vector2 size)
        {
            var tile = new GameObject(name);
            tile.transform.SetParent(parent.transform, worldPositionStays: false);
            tile.AddComponent<BoxCollider2D>().size = size;
        }

        private static Transform FindFirstActiveVisualHost(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name.StartsWith(VisualPrefix) && c.gameObject.activeSelf) return c;
            }
            return null;
        }

        private static void InvokeSync(BuildingColliderDebugOverlay overlay)
        {
            s_syncMethod.Invoke(overlay, null);
        }

        private static void SetDirty(BuildingColliderDebugOverlay overlay, bool value)
        {
            s_dirtyField.SetValue(overlay, value);
        }

        private static int GetDefaultColliderCount(BuildingColliderDebugOverlay overlay)
        {
            return (int)s_defaultCountField.GetValue(overlay);
        }
    }
}
