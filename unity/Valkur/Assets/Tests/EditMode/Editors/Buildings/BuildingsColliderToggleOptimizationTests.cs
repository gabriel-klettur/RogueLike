using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Performance-contract tests for the "Show Colliders" toggle in
    /// <see cref="BuildingsRuntimeEditor"/>.
    ///
    /// Background — what we are protecting against
    /// --------------------------------------------
    /// The toggle used to call <c>ReapplyAllColliderStates()</c> +
    /// <c>Physics2D.SyncTransforms()</c> + <c>LogColliderDiagnostics()</c> on every
    /// activation. Each of those is O(buildings × cells) over the whole scene,
    /// turning a purely visual toggle into a multi-second freeze in scenes with
    /// ~140 buildings. The optimisation moved them OUT of the toggle path:
    ///
    ///   • Physical BoxCollider2D state is the responsibility of the systems that
    ///     own it (BuildingCollisionLoader on boot, HandleColliderPaint on edit,
    ///     ApplyGridSnapshot on undo, RefreshCollisionFor on structural change).
    ///     Toggling visibility must NEVER mutate it.
    ///
    ///   • LogColliderDiagnostics now lives behind <c>_logDiagOnShow</c>
    ///     (default OFF) so the noisy O(N) snapshot is opt-in.
    ///
    /// These tests pin those contracts so future refactors can't quietly bring
    /// the freeze back.
    /// </summary>
    [TestFixture]
    public class BuildingsColliderToggleOptimizationTests
    {
        private readonly List<GameObject>       _scene  = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        // Captured Unity logs for assertions about LogColliderDiagnostics emission.
        private readonly List<string>  _capturedLogs = new List<string>();
        private Application.LogCallback _logHandler;

        // ── Reflection helpers ──────────────────────────────────────────────────

        private static FieldInfo Field(object obj, string name) => Field(obj.GetType(), name);

        private static FieldInfo Field(Type type, string name)
        {
            var t = type;
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                          BindingFlags.Instance  | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static MethodInfo Method(Type type, string name, Type[] paramTypes = null)
        {
            var t = type;
            while (t != null)
            {
                MethodInfo m;
                if (paramTypes == null)
                    m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public |
                                          BindingFlags.Instance  | BindingFlags.Static);
                else
                    m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public |
                                          BindingFlags.Instance  | BindingFlags.Static,
                                    null, paramTypes, null);
                if (m != null) return m;
                t = t.BaseType;
            }
            return null;
        }

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var t = typeof(T).BaseType;
            while (t != null)
            {
                var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                t = t.BaseType;
            }
        }

        // ── Factories ───────────────────────────────────────────────────────────

        private BuildingsRuntimeEditor CreateEditor()
        {
            ClearSingletonInstance<BuildingsRuntimeEditor>();
            var go = new GameObject("TestBuildingsEditor");
            _scene.Add(go);
            var ed = go.AddComponent<BuildingsRuntimeEditor>();
            // Bypass JSON I/O: pretend the authoring stores are loaded.
            Field(ed, "_colliderDataLoaded")?.SetValue(ed, true);
            return ed;
        }

        /// <summary>
        /// Solid 2×2-cell building anchored at the origin — same layout used by
        /// ColliderBrushTests. Pre-seeds CollTile_0_0 and CollTile_1_1 children
        /// (with enabled BoxCollider2D) so we can observe whether the toggle
        /// touches them.
        /// </summary>
        private BuildingObject CreateBuildingWithSeededCollTiles()
        {
            var template = ScriptableObject.CreateInstance<BuildingTemplateData>();
            template.templateId      = 1;
            template.originalScale   = new Vector2Int(64, 64);
            template.solid           = true;
            template.colliderScope   = "CG";
            template.sourceImagePath = "assets/buildings/test.png";
            _assets.Add(template);

            var go = new GameObject("Building");
            go.transform.position = Vector3.zero;
            var rootBox = go.AddComponent<BoxCollider2D>();
            rootBox.enabled = true;
            var b = go.AddComponent<BuildingObject>();
            Field(b, "_template")?.SetValue(b, template);
            Field(b, "_instanceId")?.SetValue(b, 1);
            _scene.Add(go);

            // Seed two CollTile children so we can verify they're untouched.
            SeedCollTile(go.transform, "CollTile_0_0", new Vector3(-0.5f, 1.5f, 0f));
            SeedCollTile(go.transform, "CollTile_1_1", new Vector3( 0.5f, 0.5f, 0f));
            return b;
        }

        private static void SeedCollTile(Transform parent, string name, Vector3 worldPos)
        {
            var tile = new GameObject(name);
            tile.transform.SetParent(parent, worldPositionStays: false);
            tile.transform.localPosition = parent.InverseTransformPoint(worldPos);
            var box = tile.AddComponent<BoxCollider2D>();
            box.enabled  = true;
            box.size     = Vector2.one;
            box.isTrigger = false;
        }

        private static int CountActiveCollTiles(Transform parent)
        {
            int count = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name.StartsWith("CollTile_", StringComparison.Ordinal) && child.gameObject.activeSelf)
                    count++;
            }
            return count;
        }

        private static void InvokeToggle(BuildingsRuntimeEditor ed)
        {
            var toggle = Method(typeof(BuildingsRuntimeEditor), "ToggleCollidersVisible", Type.EmptyTypes);
            Assert.IsNotNull(toggle, "ToggleCollidersVisible must exist as a private method.");
            toggle.Invoke(ed, null);
        }

        // ── Lifecycle ───────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            // Ignore EditMode log noise from Toast / Debug.Log inside the editor —
            // we still capture explicitly via Application.logMessageReceived for the
            // diagnostic-emission tests.
            LogAssert.ignoreFailingMessages = true;

            _capturedLogs.Clear();
            _logHandler = (string condition, string stackTrace, LogType type) =>
            {
                if (type == LogType.Log || type == LogType.Warning)
                    _capturedLogs.Add(condition ?? string.Empty);
            };
            Application.logMessageReceived += _logHandler;
        }

        [TearDown]
        public void TearDown()
        {
            if (_logHandler != null)
            {
                Application.logMessageReceived -= _logHandler;
                _logHandler = null;
            }

            foreach (var go in _scene)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _scene.Clear();

            foreach (var so in _assets)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();

            LogAssert.ignoreFailingMessages = false;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  CONTRACT 1 — Toggle is purely visual
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void ToggleCollidersVisible_PreservesRootBoxColliderEnabledState()
        {
            // The root BoxCollider2D's `enabled` flag is owned by ApplyGridOverride /
            // BuildingCollisionLoader — never the toggle. Flipping visibility must
            // not enable or disable it, regardless of its starting value.
            var ed       = CreateEditor();
            var building = CreateBuildingWithSeededCollTiles();
            var rootBox  = building.GetComponent<BoxCollider2D>();
            Assert.IsNotNull(rootBox);

            // Case A: starts enabled → must remain enabled after Show + Hide.
            rootBox.enabled = true;
            InvokeToggle(ed); // show
            Assert.IsTrue(rootBox.enabled, "Show must not disable root collider.");
            InvokeToggle(ed); // hide
            Assert.IsTrue(rootBox.enabled, "Hide must not disable root collider.");

            // Case B: starts disabled → must remain disabled after Show + Hide.
            rootBox.enabled = false;
            InvokeToggle(ed); // show
            Assert.IsFalse(rootBox.enabled, "Show must not re-enable a disabled root collider.");
            InvokeToggle(ed); // hide
            Assert.IsFalse(rootBox.enabled, "Hide must not re-enable a disabled root collider.");
        }

        [Test]
        public void ToggleCollidersVisible_PreservesCollTileChildHierarchy()
        {
            // The pre-seeded CollTile_0_0 / CollTile_1_1 children must survive
            // both Show and Hide unchanged. ClearCollisionTiles + EnsureCollTile
            // must NOT run from the toggle path.
            var ed       = CreateEditor();
            var building = CreateBuildingWithSeededCollTiles();
            int beforeActive = CountActiveCollTiles(building.transform);
            Assert.AreEqual(2, beforeActive, "Test pre-condition: two seeded CollTile children expected.");

            InvokeToggle(ed); // show
            Assert.AreEqual(beforeActive, CountActiveCollTiles(building.transform),
                "Show must not pool / disable any CollTile children.");

            InvokeToggle(ed); // hide
            Assert.AreEqual(beforeActive, CountActiveCollTiles(building.transform),
                "Hide must not pool / disable any CollTile children.");
        }

        [Test]
        public void ToggleCollidersVisible_PreservesCollTileBoxColliderEnabled()
        {
            // Each child BoxCollider2D.enabled flag is the source of truth for
            // physics — toggle must not touch it on either edge.
            var ed       = CreateEditor();
            var building = CreateBuildingWithSeededCollTiles();

            var enabledBefore = new Dictionary<string, bool>();
            for (int i = 0; i < building.transform.childCount; i++)
            {
                var child = building.transform.GetChild(i);
                if (!child.name.StartsWith("CollTile_", StringComparison.Ordinal)) continue;
                var box = child.GetComponent<BoxCollider2D>();
                if (box != null) enabledBefore[child.name] = box.enabled;
            }
            Assert.AreEqual(2, enabledBefore.Count);

            InvokeToggle(ed); // show
            InvokeToggle(ed); // hide

            for (int i = 0; i < building.transform.childCount; i++)
            {
                var child = building.transform.GetChild(i);
                if (!enabledBefore.TryGetValue(child.name, out var was)) continue;
                var box = child.GetComponent<BoxCollider2D>();
                Assert.IsNotNull(box, $"{child.name} BoxCollider2D must still exist after toggle round-trip.");
                Assert.AreEqual(was, box.enabled,
                    $"{child.name}.enabled must be unchanged by Show + Hide (toggle is visual-only).");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  CONTRACT 2 — Diagnostic log is opt-in
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void ToggleCollidersVisible_DefaultLogDiagFalse_DoesNotEmitDiagnostic()
        {
            // The diagnostic snapshot is the most expensive op in the toggle
            // path (Debug.Log captures a stack trace per call and the message
            // is built from FindObjectsOfType + GetComponentsInChildren). It
            // must be silent unless the user opts in via _logDiagOnShow.
            var ed = CreateEditor();
            CreateBuildingWithSeededCollTiles();

            // Sanity: the SerializeField is false by default (not just `==
            // default(bool)` — verify it is exposed as `_logDiagOnShow` so a
            // future rename can't silently break the contract).
            var flag = Field(ed, "_logDiagOnShow");
            Assert.IsNotNull(flag, "_logDiagOnShow inspector flag must exist.");
            Assert.IsFalse((bool)flag.GetValue(ed),
                "_logDiagOnShow must default to false (opt-in).");

            _capturedLogs.Clear();
            InvokeToggle(ed); // show

            Assert.AreEqual(0, CountDiagnosticLogs(_capturedLogs),
                "LogColliderDiagnostics must not emit when _logDiagOnShow is false.");
        }

        [Test]
        public void ToggleCollidersVisible_LogDiagTrue_EmitsDiagnostic()
        {
            // When opted-in, the diagnostic IS emitted exactly once per Show.
            // Hide must never emit it.
            var ed = CreateEditor();
            CreateBuildingWithSeededCollTiles();
            Field(ed, "_logDiagOnShow")?.SetValue(ed, true);

            _capturedLogs.Clear();
            InvokeToggle(ed); // show
            int diagCountAfterShow = CountDiagnosticLogs(_capturedLogs);
            Assert.AreEqual(1, diagCountAfterShow,
                "Exactly one diagnostic log must be emitted when _logDiagOnShow is true and the user toggles Show.");

            _capturedLogs.Clear();
            InvokeToggle(ed); // hide
            int diagCountAfterHide = CountDiagnosticLogs(_capturedLogs);
            Assert.AreEqual(0, diagCountAfterHide,
                "Hide must never emit a diagnostic log, even with _logDiagOnShow=true.");
        }

        private static int CountDiagnosticLogs(List<string> logs)
        {
            int n = 0;
            for (int i = 0; i < logs.Count; i++)
            {
                var m = logs[i] ?? string.Empty;
                if (m.IndexOf("Show Colliders", StringComparison.Ordinal) >= 0 &&
                    m.IndexOf("diagnostics", StringComparison.Ordinal) >= 0)
                {
                    n++;
                }
            }
            return n;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  CONTRACT 3 — Dead code is gone (no resurrection of the freeze path)
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void ReapplyAllColliderStates_PrivateMethod_HasBeenRemoved()
        {
            // Pinned: the old "rebuild every collider on toggle" entry point was
            // deleted in the optimisation. If a future change re-adds it as a
            // private method, this test fails so we can re-evaluate whether
            // it's being called from the toggle path again.
            var m = Method(typeof(BuildingsRuntimeEditor), "ReapplyAllColliderStates", Type.EmptyTypes);
            Assert.IsNull(m,
                "ReapplyAllColliderStates was removed because it was only ever called from " +
                "ToggleCollidersVisible / SetCollBrushMode (the freeze path). If you need to " +
                "rebuild colliders, call ApplyCollisionStateForBuilding / RefreshCollisionFor " +
                "directly from the structural-change site instead.");
        }

        [Test]
        public void ToggleCollidersVisible_OnSecondCall_DoesNotEmitDiagnosticEvenWhenFlagOnDuringHide()
        {
            // _logDiagOnShow is gated to the SHOW branch only — verify Hide path
            // never touches it even if the flag flips between calls.
            var ed = CreateEditor();
            CreateBuildingWithSeededCollTiles();

            // Show with flag OFF.
            Field(ed, "_logDiagOnShow")?.SetValue(ed, false);
            InvokeToggle(ed);
            // Flip flag mid-life, then Hide.
            Field(ed, "_logDiagOnShow")?.SetValue(ed, true);
            _capturedLogs.Clear();
            InvokeToggle(ed); // hide

            Assert.AreEqual(0, CountDiagnosticLogs(_capturedLogs),
                "Hide branch must never emit the diagnostic, even if _logDiagOnShow is true.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  CONTRACT 4 — Progressive overlay coroutine (the "no freeze" contract)
        // ═══════════════════════════════════════════════════════════════════════
        //
        // The progressive build is what actually fixes the user-visible freeze.
        // We verify:
        //   • The Show branch starts the coroutine (non-null _overlayShowCoroutine).
        //   • Calling Start back-to-back is idempotent (cancels + restarts).
        //   • The coroutine yields between batches so the FIRST frame returns
        //     before all overlays exist (no freeze).
        //   • Driving the coroutine to completion populates every building's
        //     overlay and emits the final "Colliders visible (N shapes)." toast.
        //   • If _collidersVisible flips OFF mid-run, the coroutine bails out
        //     cleanly without touching the rest of the buildings.

        private static MethodInfo CoroutineMethod() =>
            Method(typeof(BuildingsRuntimeEditor), "ProgressiveShowOverlayCoroutine", Type.EmptyTypes);

        private static IEnumerator GetProgressiveCoroutine(BuildingsRuntimeEditor ed)
        {
            var m = CoroutineMethod();
            Assert.IsNotNull(m, "ProgressiveShowOverlayCoroutine must exist as a private method.");
            return (IEnumerator)m.Invoke(ed, null);
        }

        /// <summary>
        /// Spawns <paramref name="count"/> additional buildings so we can verify
        /// the coroutine yields between batches (the per-frame budget is 8 by
        /// default; with > 8 buildings the first frame must NOT have processed
        /// every overlay).
        /// </summary>
        private List<BuildingObject> SpawnBuildings(int count)
        {
            var list = new List<BuildingObject>();
            for (int i = 0; i < count; i++)
            {
                var template = ScriptableObject.CreateInstance<BuildingTemplateData>();
                template.templateId      = 100 + i;
                template.originalScale   = new Vector2Int(64, 64);
                template.solid           = true;
                template.colliderScope   = "CG";
                template.sourceImagePath = $"assets/buildings/test_{i}.png";
                _assets.Add(template);

                var go = new GameObject($"Building_{i}");
                go.transform.position = new Vector3(i * 4f, 0f, 0f);
                go.AddComponent<BoxCollider2D>().enabled = true;
                var b = go.AddComponent<BuildingObject>();
                Field(b, "_template")?.SetValue(b, template);
                Field(b, "_instanceId")?.SetValue(b, 100 + i);
                _scene.Add(go);
                list.Add(b);
            }
            return list;
        }

        private static int CountActiveOverlays(IEnumerable<BuildingObject> buildings)
        {
            int n = 0;
            foreach (var b in buildings)
            {
                if (b == null) continue;
                var ov = b.GetComponent<BuildingColliderDebugOverlay>();
                if (ov != null && ov.Visible) n++;
            }
            return n;
        }

        [Test]
        public void ToggleCollidersVisible_Show_DispatchesProgressiveBuild()
        {
            // EditMode-friendly assertion: Show must reach the progressive
            // pipeline (observable via the "Loading colliders…" toast emitted
            // synchronously inside StartProgressiveShowOverlay). We can't probe
            // _overlayShowCoroutine directly because StartCoroutine returns
            // null when no play loop is running.
            var ed = CreateEditor();
            CreateBuildingWithSeededCollTiles();

            _capturedLogs.Clear();
            InvokeToggle(ed); // show

            bool sawLoading = false;
            foreach (var msg in _capturedLogs)
            {
                if ((msg ?? string.Empty).IndexOf("Loading colliders", StringComparison.Ordinal) >= 0)
                {
                    sawLoading = true;
                    break;
                }
            }
            Assert.IsTrue(sawLoading,
                "Toggling Show must dispatch the progressive pipeline (signalled by 'Loading colliders…' toast).");
        }

        [Test]
        public void ToggleCollidersVisible_Hide_EmitsHiddenToast()
        {
            // Hide branch is synchronous and must emit the "Colliders hidden."
            // toast directly (no progressive build, no "Loading colliders…").
            var ed = CreateEditor();
            CreateBuildingWithSeededCollTiles();

            InvokeToggle(ed); // show
            _capturedLogs.Clear();
            InvokeToggle(ed); // hide

            bool sawHidden = false;
            bool sawLoading = false;
            foreach (var msg in _capturedLogs)
            {
                var m = msg ?? string.Empty;
                if (m.IndexOf("Colliders hidden", StringComparison.Ordinal) >= 0) sawHidden = true;
                if (m.IndexOf("Loading colliders", StringComparison.Ordinal) >= 0) sawLoading = true;
            }
            Assert.IsTrue(sawHidden, "Hide branch must emit the 'Colliders hidden.' toast.");
            Assert.IsFalse(sawLoading, "Hide branch must NOT dispatch the progressive build.");
        }

        [Test]
        public void StartProgressiveShowOverlay_TwiceInARow_EmitsTwoLoadingToasts()
        {
            // Each Start call must (a) emit its own "Loading colliders…" toast
            // and (b) cancel any prior in-flight coroutine before kicking off
            // a new one. In EditMode StartCoroutine actually runs the coroutine
            // synchronously up to the first yield, so we use enough buildings
            // (> per-frame budget) that the yield is hit and the coroutine
            // does NOT complete inside the Start call.
            var ed = CreateEditor();
            // > OVERLAY_BUILDING_BUDGET_PER_FRAME (8) so the first run yields
            // before completing; that lets us observe Start() being idempotent
            // without StartCoroutine returning a non-null handle.
            SpawnBuildings(24);
            Field(ed, "_collidersVisible")?.SetValue(ed, true);

            var startMethod = Method(typeof(BuildingsRuntimeEditor),
                "StartProgressiveShowOverlay", Type.EmptyTypes);
            Assert.IsNotNull(startMethod, "StartProgressiveShowOverlay must exist as a private method.");

            _capturedLogs.Clear();
            startMethod.Invoke(ed, null);
            startMethod.Invoke(ed, null);

            int loadingCount = 0;
            foreach (var msg in _capturedLogs)
            {
                if ((msg ?? string.Empty).IndexOf("Loading colliders", StringComparison.Ordinal) >= 0)
                    loadingCount++;
            }
            Assert.AreEqual(2, loadingCount,
                "Two back-to-back Start calls must each emit one 'Loading colliders…' toast.");
        }

        [Test]
        public void ProgressiveCoroutine_YieldsBetweenBatches_NotAllInFirstFrame()
        {
            // With 24 buildings (> 3 × per-frame budget of 8), the coroutine must
            // yield at least once before processing them all. We drive it
            // manually until the first yield and assert FEWER than 24 overlays
            // are ready.
            const int total = 24;
            var ed = CreateEditor();
            var buildings = SpawnBuildings(total);
            Field(ed, "_collidersVisible")?.SetValue(ed, true);

            var co = GetProgressiveCoroutine(ed);
            // First MoveNext processes the first batch then yields.
            Assert.IsTrue(co.MoveNext(), "Coroutine must yield at least once for >budget items.");

            int afterFirstYield = CountActiveOverlays(buildings);
            Assert.Less(afterFirstYield, total,
                $"Progressive build must NOT process all {total} overlays in the first frame — found {afterFirstYield}.");
            Assert.Greater(afterFirstYield, 0,
                "First batch must process at least one overlay before yielding.");
        }

        [Test]
        public void ProgressiveCoroutine_DriveToCompletion_ActivatesEveryOverlay()
        {
            const int total = 24;
            var ed = CreateEditor();
            var buildings = SpawnBuildings(total);
            Field(ed, "_collidersVisible")?.SetValue(ed, true);

            _capturedLogs.Clear();
            var co = GetProgressiveCoroutine(ed);
            // Hard cap on iterations so a buggy infinite loop fails fast.
            int safety = 1000;
            while (co.MoveNext() && --safety > 0) { }
            Assert.Greater(safety, 0, "Coroutine drove past the safety cap — infinite loop?");

            Assert.AreEqual(total, CountActiveOverlays(buildings),
                "After completion, every building's overlay must be visible.");

            bool sawFinalToast = false;
            foreach (var msg in _capturedLogs)
            {
                if ((msg ?? string.Empty).IndexOf("Colliders visible (", StringComparison.Ordinal) >= 0)
                {
                    sawFinalToast = true;
                    break;
                }
            }
            Assert.IsTrue(sawFinalToast,
                "Coroutine must emit a final 'Colliders visible (N shapes).' toast on completion.");
        }

        [Test]
        public void ProgressiveCoroutine_BailsOutCleanlyIfHiddenMidRun()
        {
            // If the user toggles Hide while the coroutine is still building
            // overlays, _collidersVisible flips to false and the coroutine must
            // exit on its very next batch — without crashing or processing the
            // remaining buildings.
            const int total = 24;
            var ed = CreateEditor();
            var buildings = SpawnBuildings(total);
            Field(ed, "_collidersVisible")?.SetValue(ed, true);

            var co = GetProgressiveCoroutine(ed);
            Assert.IsTrue(co.MoveNext(), "Coroutine must yield at least once before mid-run cancel.");
            int countAtCancel = CountActiveOverlays(buildings);

            // Simulate the user toggling Hide.
            Field(ed, "_collidersVisible")?.SetValue(ed, false);

            // Drive to completion — the coroutine must yield-break almost immediately.
            int safety = 100;
            while (co.MoveNext() && --safety > 0) { }
            Assert.Greater(safety, 0, "Coroutine did not bail out after _collidersVisible flipped to false.");

            int countAfterCancel = CountActiveOverlays(buildings);
            Assert.AreEqual(countAtCancel, countAfterCancel,
                "After mid-run cancel, no further overlays should have been activated.");
        }

        [Test]
        public void ProgressiveCoroutine_LoadingToastEmittedOnStart()
        {
            // Sanity: the user-visible feedback "Loading colliders…" is emitted
            // by StartProgressiveShowOverlay so the toggle never feels frozen.
            var ed = CreateEditor();
            CreateBuildingWithSeededCollTiles();
            Field(ed, "_collidersVisible")?.SetValue(ed, true);

            _capturedLogs.Clear();
            Method(typeof(BuildingsRuntimeEditor), "StartProgressiveShowOverlay", Type.EmptyTypes)
                .Invoke(ed, null);

            bool sawLoading = false;
            foreach (var msg in _capturedLogs)
            {
                if ((msg ?? string.Empty).IndexOf("Loading colliders", StringComparison.Ordinal) >= 0)
                {
                    sawLoading = true;
                    break;
                }
            }
            Assert.IsTrue(sawLoading,
                "StartProgressiveShowOverlay must emit a 'Loading colliders…' toast for immediate user feedback.");
        }
    }
}
