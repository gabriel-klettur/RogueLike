using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Spawners;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.Spawners
{
    /// <summary>
    /// Behaviour tests for the RMB move-drag of an already-placed spawner —
    /// Buildings / Entities parity introduced in the Spawner Editor (F3).
    ///
    /// The drag has three observable phases:
    ///   1. <c>BeginMoveDrag(worldPos)</c> — finds the spawner under the cursor,
    ///      arms the move state, captures offset + start position.
    ///   2. <c>HandleMapInteraction</c> follows the cursor each frame
    ///      (not exercised here — covered by manual testing because mouse
    ///      simulation in EditMode is fragile).
    ///   3. <c>FinalizeMoveDrag()</c> — clears flag, records undo entry, sets
    ///      a status. No-movement drags are reported as cancelled and don't
    ///      pollute the undo stack.
    ///
    /// This fixture pins down the side-effects of phases 1 and 3 so the
    /// move-drag UX can't silently regress.
    /// </summary>
    [TestFixture]
    public class SpawnerMoveDragTests
    {
        private readonly List<GameObject>       _scene  = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private SpawnerEditorManager _mgr;

        // ── Reflection helpers ───────────────────────────────────────────────

        private static FieldInfo GetField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static T GetFieldValue<T>(object obj, string name)
            => (T)GetField(obj, name)?.GetValue(obj);

        private static void SetFieldValue(object obj, string name, object value)
            => GetField(obj, name)?.SetValue(obj, value);

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        // ── Scene factories ──────────────────────────────────────────────────

        private SpawnerInstance MakeSpawner(string id, Vector3 pos)
        {
            var template = ScriptableObject.CreateInstance<SpawnerTemplateData>();
            template.templateId    = id;
            template.triggerRadius = 1f;
            _assets.Add(template);

            var go = new GameObject($"TestSpawner_{id}");
            go.transform.position = pos;
            _scene.Add(go);

            var si = go.AddComponent<SpawnerInstance>();
            si.Initialize(template, id, zone: "Lobby", spawner: null);
            return si;
        }

        // ── Setup / Teardown ─────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            ClearSingletonInstance<SpawnerEditorManager>();

            var go = new GameObject("[SpawnerEditorManager-Test]");
            _scene.Add(go);
            _mgr = go.AddComponent<SpawnerEditorManager>();

            // Force-active without going through BuildUI — we test logic, not chrome.
            SetFieldValue(_mgr, "_active", true);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)  if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var so in _assets) if (so != null) Object.DestroyImmediate(so);
            _assets.Clear();

            ClearSingletonInstance<SpawnerEditorManager>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── BeginMoveDrag ────────────────────────────────────────────────────

        [Test]
        public void BeginMoveDrag_NoSpawnerNearby_ReturnsNullAndDoesNotArm()
        {
            MakeSpawner("a", new Vector3(50f, 50f, 0f));

            var hit = _mgr.BeginMoveDrag(Vector3.zero);

            Assert.IsNull(hit, "Empty world position must not arm a drag.");
            Assert.IsFalse(GetFieldValue<bool>(_mgr, "_dragging"),
                "_dragging must remain false when no spawner is hit.");
        }

        [Test]
        public void BeginMoveDrag_SpawnerNearby_ArmsDragAndSelects()
        {
            var si = MakeSpawner("a", new Vector3(0.2f, 0f, 0f));

            var hit = _mgr.BeginMoveDrag(new Vector3(0.0f, 0f, 0f));

            Assert.AreEqual(si, hit, "BeginMoveDrag must return the spawner under the cursor.");
            Assert.IsTrue(GetFieldValue<bool>(_mgr, "_dragging"),
                "_dragging must be set so HandleMapInteraction follows the cursor.");
            Assert.AreEqual(si, GetFieldValue<SpawnerInstance>(_mgr, "_selectedInstance"),
                "Dragging must auto-select the instance — Buildings / Entities parity.");
        }

        [Test]
        public void BeginMoveDrag_PreservesClickOffset()
        {
            // Spawner at (3, 0). Click at (2.5, 0). Offset = (0.5, 0).
            var si = MakeSpawner("a", new Vector3(3f, 0f, 0f));
            var clickPos = new Vector3(2.5f, 0f, 0f);

            _mgr.BeginMoveDrag(clickPos);

            Vector3 offset = GetFieldValue<Vector3>(_mgr, "_dragOffset");
            Assert.AreEqual(0.5f, offset.x, 0.0001f, "Drag offset X must equal spawner.x - cursor.x.");
            Assert.AreEqual(0f,   offset.y, 0.0001f, "Drag offset Y must be zero on a horizontal click.");
        }

        [Test]
        public void BeginMoveDrag_StoresStartPositionForUndo()
        {
            var si = MakeSpawner("a", new Vector3(7f, 9f, 0f));

            _mgr.BeginMoveDrag(new Vector3(7f, 9f, 0f));

            Vector3 start = GetFieldValue<Vector3>(_mgr, "_dragStartWorldPos");
            Assert.AreEqual(7f, start.x, 0.0001f, "_dragStartWorldPos must capture spawner.x at drag start.");
            Assert.AreEqual(9f, start.y, 0.0001f, "_dragStartWorldPos must capture spawner.y at drag start.");
        }

        [Test]
        public void BeginMoveDrag_PicksClosestWhenStacked()
        {
            // Two spawners both within selection radius; the closer one wins.
            MakeSpawner("far",  new Vector3(1.0f, 0f, 0f));
            var near = MakeSpawner("near", new Vector3(0.2f, 0f, 0f));

            var hit = _mgr.BeginMoveDrag(Vector3.zero);

            Assert.AreEqual(near, hit, "Closest spawner inside the selection radius must win.");
        }

        // ── FinalizeMoveDrag ─────────────────────────────────────────────────

        [Test]
        public void FinalizeMoveDrag_NoMovement_DoesNotRecordUndo()
        {
            var si = MakeSpawner("a", new Vector3(0f, 0f, 0f));
            _mgr.BeginMoveDrag(Vector3.zero);
            // No movement: _selectedInstance.position unchanged.

            int undoBefore = GetUndoCount(_mgr);
            _mgr.FinalizeMoveDrag();
            int undoAfter = GetUndoCount(_mgr);

            Assert.AreEqual(undoBefore, undoAfter,
                "A drag with zero movement must NOT pollute the undo stack.");
            Assert.IsFalse(GetFieldValue<bool>(_mgr, "_dragging"),
                "FinalizeMoveDrag must clear the _dragging flag even on no-op drops.");
        }

        [Test]
        public void FinalizeMoveDrag_WithMovement_RecordsUndo()
        {
            var si = MakeSpawner("a", new Vector3(0f, 0f, 0f));
            _mgr.BeginMoveDrag(Vector3.zero);

            // Simulate the drag by relocating the instance.
            si.transform.position = new Vector3(5f, 3f, 0f);

            int undoBefore = GetUndoCount(_mgr);
            _mgr.FinalizeMoveDrag();
            int undoAfter = GetUndoCount(_mgr);

            Assert.AreEqual(undoBefore + 1, undoAfter,
                "FinalizeMoveDrag with movement must push exactly one entry onto the undo stack.");
            Assert.IsFalse(GetFieldValue<bool>(_mgr, "_dragging"),
                "FinalizeMoveDrag must clear the _dragging flag on commit.");
        }

        [Test]
        public void FinalizeMoveDrag_Undo_RestoresOriginalPosition()
        {
            var si = MakeSpawner("a", new Vector3(2f, 4f, 0f));
            _mgr.BeginMoveDrag(new Vector3(2f, 4f, 0f));

            si.transform.position = new Vector3(10f, 12f, 0f);
            _mgr.FinalizeMoveDrag();

            // Push the recorded command back out.
            var undo = GetFieldValue<UndoStack>(_mgr, "_undo");
            Assert.IsNotNull(undo, "Test fixture must locate the editor's UndoStack.");
            Assert.IsTrue(undo.Undo(), "Undo must succeed when there is a recorded move.");

            Assert.AreEqual(new Vector3(2f, 4f, 0f), si.transform.position,
                "Undo of a move drag must restore the original world position.");
        }

        [Test]
        public void FinalizeMoveDrag_UndoThenRedo_ReappliesNewPosition()
        {
            var si = MakeSpawner("a", Vector3.zero);
            _mgr.BeginMoveDrag(Vector3.zero);
            var dropPos = new Vector3(3f, -1.5f, 0f);
            si.transform.position = dropPos;
            _mgr.FinalizeMoveDrag();

            var undo = GetFieldValue<UndoStack>(_mgr, "_undo");
            undo.Undo();
            Assert.IsTrue(undo.Redo(), "Redo must succeed after a successful undo.");

            Assert.AreEqual(dropPos, si.transform.position,
                "Redo of a move drag must restore the dropped position.");
        }

        [Test]
        public void FinalizeMoveDrag_NoSelection_IsSafeNoOp()
        {
            // No drag was ever started — finalizing must not throw.
            Assert.DoesNotThrow(() => _mgr.FinalizeMoveDrag(),
                "FinalizeMoveDrag must be safe to call without an armed drag.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static int GetUndoCount(SpawnerEditorManager mgr)
        {
            var stack = GetFieldValue<UndoStack>(mgr, "_undo");
            return stack != null ? stack.UndoCount : -1;
        }
    }
}
