using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Enemies.FSM;
using Valkur.UIKit;
using static Valkur.Tests.EditMode.Editors.FSM.FSMEditorTestSupport;

namespace Valkur.Tests.EditMode.Editors.FSM
{
    /// <summary>
    /// F12 graph-tool chrome fixes:
    ///   • add-node coordinate frame (centre-pivot click → top-left-anchored node position)
    ///   • self/global edge authoring through the Connect tool
    ///   • undo/redo actually recording the destructive graph operations
    ///
    /// Every test redirects persistence to a temp directory via
    /// <see cref="FSMEditorTestSupport.CreateEditorWithTempData"/> — never the real
    /// <c>StreamingAssets/FSM/</c>, which <c>FSMRuntimeEditor.Persistence</c> has no
    /// <c>RefuseWriteOutsidePlayMode</c> guard against.
    /// </summary>
    [TestFixture]
    public class FSMEditorGraphToolsTests
    {
        private readonly List<FSMEditorTestSupport.TempFsmEditor> _handles = new List<FSMEditorTestSupport.TempFsmEditor>();

        [TearDown]
        public void TearDown()
        {
            foreach (var h in _handles) h.Dispose();
            _handles.Clear();
        }

        private FSMEditorTestSupport.TempFsmEditor NewEditor()
        {
            var h = FSMEditorTestSupport.CreateEditorWithTempData();
            _handles.Add(h);
            return h;
        }

        // ── Fixture builders ─────────────────────────────────────────────────────

        private static FSMRuntimeEditor.FSMSetData MakeTestSet(string id = "TestSet")
        {
            var raw = new Dictionary<string, object>
            {
                ["id"] = id, ["label"] = id, ["initial"] = "IdleState",
                ["states"] = new List<object>(), ["transitions"] = new List<object>(),
            };
            var set = new FSMRuntimeEditor.FSMSetData { id = id, label = id, initial = "IdleState", raw = raw };

            AddState(set, "IdleState");
            AddState(set, "ChaseState");
            return set;
        }

        private static void AddState(FSMRuntimeEditor.FSMSetData set, string stateId)
        {
            var sraw = new Dictionary<string, object>
            {
                ["id"] = stateId, ["label"] = stateId, ["class"] = "",
                ["terminal"] = false, ["props"] = new Dictionary<string, object>(),
            };
            ((List<object>)set.raw["states"]).Add(sraw);
            set.states.Add(new FSMRuntimeEditor.FSMStateNode { id = stateId, label = stateId, stateClass = "", raw = sraw });
        }

        private static FSMRuntimeEditor.FSMTransitionData AddTransition(
            FSMRuntimeEditor.FSMSetData set, string from, string to, string trId = "t1")
        {
            var traw = new Dictionary<string, object> { ["id"] = trId, ["from"] = from, ["to"] = to, ["guard"] = "" };
            ((List<object>)set.raw["transitions"]).Add(traw);
            var tr = new FSMRuntimeEditor.FSMTransitionData { id = trId, from = from, to = to, raw = traw };
            set.transitions.Add(tr);
            return tr;
        }

        /// <summary>Loads an empty typed model against the temp dir, then splices in a
        /// hand-built set — the direct-construction equivalent of what a designer would
        /// build interactively through the Sets panel.</summary>
        private static void InstallSet(FSMRuntimeEditor ed, FSMRuntimeEditor.FSMSetData set)
        {
            ed.LoadSetsFromDisk();
            var fsmSets = GetField<List<FSMRuntimeEditor.FSMSetData>>(ed, "_fsmSets");
            fsmSets.Add(set);
            var setsRoot = GetField<Dictionary<string, object>>(ed, "_setsRoot");
            ((List<object>)setsRoot["sets"]).Add(set.raw);
            SetField(ed, "_selectedSet", set);
        }

        private static void SizeGraphContent(FSMRuntimeEditor ed, float w, float h)
        {
            var graphContent = GetField<RectTransform>(ed, "_graphContent");
            graphContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            graphContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        }

        // ── Item 1: add-node coordinate frame ────────────────────────────────────

        [Test]
        public void AddNodeAt_CentreClick_LandsAtHalfCanvasSize_NotAtOrigin()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            InstallSet(h.Editor, set);
            SizeGraphContent(h.Editor, 800f, 600f);

            // Local (0,0) in _graphContent's own pivot-(0.5,0.5) frame IS the canvas centre
            // — exactly what TryGetEmptyCanvasContentPos hands AddNodeAt for a click in the
            // middle of the panel.
            Invoke(h.Editor, "AddNodeAt", new Vector2(0f, 0f));

            Assert.AreEqual(3, set.states.Count, "IdleState + ChaseState + the new node.");
            var added = set.states[set.states.Count - 1];
            Assert.AreEqual(400f, added.x, 0.01f, "Centre click must land at half the canvas width.");
            Assert.AreEqual(300f, added.y, 0.01f, "Centre click must land at half the canvas height.");
        }

        [Test]
        public void AddNodeAt_TopLeftOfCanvas_MapsToOriginInNodeFrame()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            InstallSet(h.Editor, set);
            SizeGraphContent(h.Editor, 800f, 600f);

            // The canvas's own top-left corner, expressed in the centre-pivot frame, is
            // (-w/2, +h/2) — Y+ is UP there. It must map to node-frame (0,0), the exact
            // point CreateNodeVisual's `anchoredPosition = (state.x, -state.y)` also calls
            // the top-left corner.
            Invoke(h.Editor, "AddNodeAt", new Vector2(-400f, 300f));

            var added = set.states[set.states.Count - 1];
            Assert.AreEqual(0f, added.x, 0.01f);
            Assert.AreEqual(0f, added.y, 0.01f);
        }

        // ── Item 3: undo/redo ─────────────────────────────────────────────────────

        [Test]
        public void AddNodeAt_IsUndoable()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            InstallSet(h.Editor, set);
            SizeGraphContent(h.Editor, 800f, 600f);

            int before = set.states.Count;
            Invoke(h.Editor, "AddNodeAt", new Vector2(0f, 0f));
            Assert.AreEqual(before + 1, set.states.Count);

            var undo = GetField<UndoStack>(h.Editor, "_undo");
            Assert.IsTrue(undo.CanUndo, "AddNodeAt must record an undo step — the Undo button must not be a no-op.");

            undo.Undo();
            Assert.AreEqual(before, set.states.Count, "Undo must remove the node it added.");

            Assert.IsTrue(undo.CanRedo);
            undo.Redo();
            Assert.AreEqual(before + 1, set.states.Count, "Redo must restore it.");
        }

        [Test]
        public void DeleteNode_CascadesTransitions_AndIsUndoable()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            var tr = AddTransition(set, "IdleState", "ChaseState");
            InstallSet(h.Editor, set);

            var idle = set.states.First(s => s.id == "IdleState");
            var undo = GetField<UndoStack>(h.Editor, "_undo");

            Invoke(h.Editor, "DeleteNode", idle);

            Assert.IsFalse(set.states.Contains(idle), "Node must be removed.");
            Assert.AreEqual(0, set.transitions.Count, "Its incoming/outgoing transition must cascade-delete.");

            undo.Undo();
            Assert.IsTrue(set.states.Contains(idle), "Undo must restore the node.");
            Assert.AreEqual(1, set.transitions.Count, "Undo must restore the cascaded transition too, not just the node.");
            Assert.AreSame(tr, set.transitions[0]);
        }

        [Test]
        public void DeleteEdge_IsUndoable()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            var tr = AddTransition(set, "IdleState", "ChaseState");
            InstallSet(h.Editor, set);

            var undo = GetField<UndoStack>(h.Editor, "_undo");
            Invoke(h.Editor, "DeleteEdge", tr);
            Assert.AreEqual(0, set.transitions.Count);

            undo.Undo();
            Assert.AreEqual(1, set.transitions.Count);
            Assert.AreSame(tr, set.transitions[0]);
        }

        [Test]
        public void DeleteSetConfirmed_IsUndoable()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            InstallSet(h.Editor, set);

            var fsmSets = GetField<List<FSMRuntimeEditor.FSMSetData>>(h.Editor, "_fsmSets");
            int before = fsmSets.Count;

            Invoke(h.Editor, "DeleteSetConfirmed", set);
            Assert.AreEqual(before - 1, fsmSets.Count);
            Assert.IsFalse(fsmSets.Contains(set));

            var undo = GetField<UndoStack>(h.Editor, "_undo");
            Assert.IsTrue(undo.CanUndo, "Delete-Set must record an undo step — it writes to disk immediately.");
            undo.Undo();

            Assert.AreEqual(before, fsmSets.Count);
            Assert.IsTrue(fsmSets.Any(s => s.id == set.id));
        }

        // ── Item 2: self-transitions refused, global ("*") edges authorable ─────────

        [Test]
        public void HandleConnectClickFrom_SameNodeTwice_RefusesSelfTransition()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            InstallSet(h.Editor, set);

            Invoke(h.Editor, "HandleConnectClickFrom", "IdleState", true);
            Invoke(h.Editor, "HandleConnectClickFrom", "IdleState", true);

            Assert.AreEqual(0, set.transitions.Count,
                "A self-transition can never fire — StateMachine.TryTakeAuthoredTransition " +
                "skips any edge whose To equals the current state unconditionally — so the " +
                "Connect tool must refuse to author one rather than silently create dead data.");
        }

        [Test]
        public void HandleConnectClickFrom_AnyStateToRealNode_CreatesGlobalTransition()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            InstallSet(h.Editor, set);

            Invoke(h.Editor, "HandleConnectClickFrom", "*", true);
            Invoke(h.Editor, "HandleConnectClickFrom", "ChaseState", true);

            Assert.AreEqual(1, set.transitions.Count);
            var tr = set.transitions[0];
            Assert.AreEqual("*", tr.from);
            Assert.AreEqual("ChaseState", tr.to);
            // NormalizeSets (run inside SaveSets, called by PersistSets) folds a wildcard
            // source into tr["global"] = true — the flag FSMTransition.IsGlobal and the
            // runtime factory's ExtractTransitions depend on being present after a save.
            Assert.IsTrue(tr.raw.ContainsKey("global") && (bool)tr.raw["global"],
                "A wildcard-source transition must be marked global once persisted.");
        }

        [Test]
        public void HandleConnectClickFrom_GlobalEdge_IsUndoable()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            InstallSet(h.Editor, set);

            var undo = GetField<UndoStack>(h.Editor, "_undo");
            Invoke(h.Editor, "HandleConnectClickFrom", "*", true);
            Invoke(h.Editor, "HandleConnectClickFrom", "ChaseState", true);
            Assert.AreEqual(1, set.transitions.Count);

            undo.Undo();
            Assert.AreEqual(0, set.transitions.Count);
        }

        [Test]
        public void RefreshGraph_AlwaysRegistersAnyStateNode()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            InstallSet(h.Editor, set);

            Invoke(h.Editor, "RefreshGraph");

            var nodeRects = GetField<Dictionary<string, RectTransform>>(h.Editor, "_nodeRects");
            Assert.IsTrue(nodeRects.ContainsKey("*"),
                "The Any State pseudo-node must always be registered so a global edge " +
                "('from': '*') has somewhere to draw to/from — mirrors Unity's own Animator " +
                "window 'Any State' node.");
        }

        [Test]
        public void HandleUndoRedoShortcuts_DoesNotThrow_WithNoKeyboardActivity()
        {
            // Ctrl+Z/Ctrl+Y actually calling _undo.Undo()/Redo() needs a simulated
            // InputSystem keyboard to exercise meaningfully; this pins the cheaper but
            // still real regression — the glue that makes the tutorial overlay's
            // long-standing ("Ctrl+Z", "Undo") / ("Ctrl+Y", "Redo") entries true must not
            // throw when Update() calls it every frame with nothing held.
            var h = NewEditor();
            Assert.DoesNotThrow(() => Invoke(h.Editor, "HandleUndoRedoShortcuts"));
        }

        [Test]
        public void RefreshGraph_DrawsGlobalEdge_FromAnyStateNode()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            InstallSet(h.Editor, set);

            Invoke(h.Editor, "HandleConnectClickFrom", "*", true);
            Invoke(h.Editor, "HandleConnectClickFrom", "ChaseState", true);

            // Built-in edges share _edgeObjects with the authored ones, and this test is
            // about the AUTHORED global edge reaching the canvas at all. Counting both would
            // re-baseline it against FSMBuiltInTransitions' size and turn a behaviour
            // assertion into a headcount that changes whenever a state class gains an exit.
            SetField(h.Editor, "_showBuiltInEdges", false);
            Invoke(h.Editor, "RefreshGraph");

            var edgeObjects = GetField<List<GameObject>>(h.Editor, "_edgeObjects");
            // One line + one label per edge (CreateEdgeVisual). Before the Any State node
            // existed, CreateEdgeVisual looked up _nodeRects["*"], found nothing, and
            // returned early — a global edge was authored but never drawn.
            Assert.AreEqual(2, edgeObjects.Count);

            // ...and with the built-in layer on, the same authored edge is still there plus
            // the code-owned ones. This half is what would have caught the layer silently
            // not drawing at all.
            SetField(h.Editor, "_showBuiltInEdges", true);
            Invoke(h.Editor, "RefreshGraph");
            Assert.Greater(GetField<List<GameObject>>(h.Editor, "_edgeObjects").Count, 2,
                "the built-in edge layer must add to the authored edges, not replace them");
        }
    }
}
