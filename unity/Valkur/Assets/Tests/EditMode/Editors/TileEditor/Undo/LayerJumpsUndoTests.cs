using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Undo
{
    /// <summary>
    /// Regression coverage for Bug 3 of the metadata-undo fix: Layer-Jumps
    /// strokes had NO undo at all — Ctrl+Z did nothing. Root cause was two-fold:
    ///   (a) <c>HandleLayerJumpsInput</c> never recorded anything into the undo
    ///       system in the first place (fixed by wrapping every stroke in
    ///       <c>StartStroke(null) → RecordMetadataEdits → EndStroke</c>);
    ///   (b) even once it did, a Layer-Jumps stroke touches no Tilemap at all —
    ///       its <see cref="TileEditBatch.Edits"/> list is always empty — and the
    ///       OLD <c>TileEditorUndoSystem.EndStroke</c> guard was
    ///       <c>Edits.Count &gt; 0</c>, so the batch was silently discarded right
    ///       back out before ever reaching the undo stack. The new
    ///       <c>HasContent</c> helper (<c>Edits.Count &gt; 0 || MetadataEdits.Count &gt; 0</c>)
    ///       is what actually lets a metadata-only batch survive.
    ///
    /// Section 1 pins (b) in complete isolation — no manager, no tilemap, just
    /// <see cref="TileEditorUndoSystem"/> + a minimal <see cref="ITileMetadataMap"/>
    /// double — so a regression there fails fast and unambiguously.
    ///
    /// Section 2 drives the real production glue: a real
    /// <see cref="TileEditorManager"/>'s private <c>StampLayerJumpsFootprint</c>
    /// (invoked via reflection — the same convention as
    /// <c>TileEditorUndoRobustnessTests.InvokePrivate</c>) feeding a real
    /// <see cref="TileEditorUndoSystem"/> through the exact
    /// <c>StartStroke(null) → RecordMetadataEdits → EndStroke</c> sequence that
    /// <c>HandleLayerJumpsInput</c> itself performs. <c>HandleLayerJumpsInput</c>
    /// is not called directly because every one of its three branches is gated
    /// behind <c>MouseInputManager.WasLeftMouseButtonPressedThisFrame()</c>,
    /// which falls through to <c>UnityEngine.Input</c> — unusable outside Play
    /// Mode, so there is no way to drive per-frame mouse state from an EditMode
    /// test. Nothing besides that mouse-gate is left uncovered: the method's body
    /// is exactly the sequence reproduced here.
    /// </summary>
    [TestFixture]
    public class LayerJumpsUndoTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. Isolated HasContent / EndStroke pin (no manager required)
        // ════════════════════════════════════════════════════════════════════

        private class FakeMetadataMap : ITileMetadataMap
        {
            public readonly Dictionary<Vector3Int, string> Values = new Dictionary<Vector3Int, string>();
            public void Set(Vector3Int cell, string value)
            {
                if (string.IsNullOrEmpty(value)) Values.Remove(cell);
                else Values[cell] = value;
            }
        }

        [Test]
        public void MetadataOnlyBatch_NoTilemapInvolved_EndStroke_StillPushesToUndoStack()
        {
            // Mirrors HandleLayerJumpsInput exactly: StartStroke(null) — no tilemap —
            // then a MetadataEdit-only payload, then EndStroke.
            var undo = new TileEditorUndoSystem();
            var map = new FakeMetadataMap();
            var cell = Vector3Int.zero;

            undo.StartStroke(null);
            undo.RecordMetadataEdits(new List<MetadataEdit> { new MetadataEdit(cell, null, "3", map) });
            undo.EndStroke();
            map.Set(cell, "3"); // apply, mirroring what StampLayerJumpsFootprint does inline

            var undone = undo.Undo();

            Assert.IsNotNull(undone,
                "BUG 3 root cause — a batch whose Edits list is empty (no tilemap involved) but " +
                "whose MetadataEdits list is non-empty must still be pushed onto the undo stack. " +
                "The old EndStroke guard (Edits.Count > 0) silently dropped every Layer-Jumps " +
                "stroke here, so Ctrl+Z had nothing to undo — not a wrong result, NO result.");
            Assert.IsFalse(map.Values.ContainsKey(cell), "Undo must have reverted the fake map entry.");
        }

        [Test]
        public void MetadataOnlyBatch_EmptyMetadataAndEmptyEdits_IsNotPushed()
        {
            // Sanity companion: a truly empty stroke (nothing recorded at all) must
            // still be dropped — HasContent isn't "always true", just "OR of both".
            var undo = new TileEditorUndoSystem();
            undo.StartStroke(null);
            undo.EndStroke();

            Assert.IsNull(undo.Undo(), "A batch with neither Edits nor MetadataEdits must not be on the stack.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. Manager-level — real StampLayerJumpsFootprint + real undo system
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void LayerJumpsStroke_CtrlZ_RevertsTargetAssignment()
        {
            var manager = NewManager();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(2, 2, 0);

            DrawLayerJumpStroke(manager, undo, cell, target: "3", drawing: true);

            Assert.AreEqual("3", manager.LayerJumps.Get(cell), "Sanity: stroke wrote the target.");

            var undone = undo.Undo();

            Assert.IsNotNull(undone, "The stroke must have been committed to the undo stack (see HasContent fix).");
            Assert.AreEqual(string.Empty, manager.LayerJumps.Get(cell),
                "BUG 3 — Ctrl+Z on a Layer-Jumps stroke must clear the target assignment. " +
                "Before the fix this did nothing at all: the stroke was never on the undo stack.");
        }

        [Test]
        public void LayerJumpsStroke_CtrlZ_ThenRedo_ReappliesTarget()
        {
            var manager = NewManager();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(5, 5, 0);

            DrawLayerJumpStroke(manager, undo, cell, target: "6", drawing: true);
            undo.Undo();
            Assert.AreEqual(string.Empty, manager.LayerJumps.Get(cell), "Pre-condition: undone.");

            var redone = undo.Redo();

            Assert.IsNotNull(redone);
            Assert.AreEqual("6", manager.LayerJumps.Get(cell), "Redo must re-apply the target.");
        }

        [Test]
        public void LayerJumpsErase_CtrlZ_RestoresPriorTarget()
        {
            var manager = NewManager();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(0, 0, 0);

            DrawLayerJumpStroke(manager, undo, cell, target: "4", drawing: true);  // stroke 1: draw "4"
            DrawLayerJumpStroke(manager, undo, cell, target: null, drawing: false); // stroke 2: erase

            Assert.AreEqual(string.Empty, manager.LayerJumps.Get(cell), "Sanity: erased.");

            undo.Undo(); // reverts stroke 2 (the erase)

            Assert.AreEqual("4", manager.LayerJumps.Get(cell),
                "Undoing an erase must restore the ORIGINAL target ('4'), not leave the cell empty.");
        }

        [Test]
        public void LayerJumpsStroke_BrushSizeTwo_Undo_RevertsEveryCellOfFootprint()
        {
            var manager = NewManager();
            manager.State.BrushSize = 2;
            var undo = new TileEditorUndoSystem();
            var anchor = new Vector3Int(0, 0, 0);

            DrawLayerJumpStroke(manager, undo, anchor, target: "1", drawing: true);

            // Footprint convention (matches TileBrush/StampLayerJumpsFootprint): cursor is
            // top-left, extends +X and -Y.
            var cells = new[]
            {
                new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
                new Vector3Int(0, -1, 0), new Vector3Int(1, -1, 0),
            };
            foreach (var c in cells)
                Assert.AreEqual("1", manager.LayerJumps.Get(c), $"Sanity: cell {c} painted.");

            undo.Undo();

            foreach (var c in cells)
                Assert.AreEqual(string.Empty, manager.LayerJumps.Get(c), $"Undo must revert cell {c} too.");
        }

        [Test]
        public void LayerJumpsStroke_RepaintingSameTarget_ProducesNoMetadataEdit_NotPushedToStack()
        {
            var manager = NewManager();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(7, 7, 0);

            DrawLayerJumpStroke(manager, undo, cell, target: "2", drawing: true);
            var secondStrokeEdits = DrawLayerJumpStroke(manager, undo, cell, target: "2", drawing: true); // no-op

            Assert.IsEmpty(secondStrokeEdits, "Repainting the identical target must record zero MetadataEdits.");

            var firstUndo = undo.Undo();
            Assert.IsNotNull(firstUndo, "The real (first) stroke must still be undoable.");
            Assert.AreEqual(string.Empty, manager.LayerJumps.Get(cell));

            var secondUndo = undo.Undo();
            Assert.IsNull(secondUndo, "The no-op repaint must not have pushed an empty batch onto the stack.");
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════

        private TileEditorManager NewManager()
        {
            _host = new GameObject("LayerJumpsUndoTests_Host");
            return _host.AddComponent<TileEditorManager>();
        }

        /// <summary>Reproduces HandleLayerJumpsInput's per-click body verbatim (real,
        /// reflected StampLayerJumpsFootprint + real TileEditorUndoSystem) without
        /// depending on MouseInputManager frame polling, which is unusable in EditMode.
        /// Returns the MetadataEdit list recorded, for assertion convenience.</summary>
        private static List<MetadataEdit> DrawLayerJumpStroke(TileEditorManager manager,
            TileEditorUndoSystem undo, Vector3Int cursorCell, string target, bool drawing)
        {
            undo.StartStroke(null);
            var metaEdits = InvokeStampFootprint(manager, cursorCell, target, drawing);
            undo.RecordMetadataEdits(metaEdits);
            undo.EndStroke();
            return metaEdits;
        }

        private static List<MetadataEdit> InvokeStampFootprint(TileEditorManager manager,
            Vector3Int cursorCell, string target, bool drawing)
        {
            var mi = typeof(TileEditorManager).GetMethod("StampLayerJumpsFootprint",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(mi, "Reflection: StampLayerJumpsFootprint not found on TileEditorManager.");
            return (List<MetadataEdit>)mi.Invoke(manager, new object[] { cursorCell, target, drawing });
        }
    }
}
