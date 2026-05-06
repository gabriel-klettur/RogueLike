using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Buildings;

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
    }
}
