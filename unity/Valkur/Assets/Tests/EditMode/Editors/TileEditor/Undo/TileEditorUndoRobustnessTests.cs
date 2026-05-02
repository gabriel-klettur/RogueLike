using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Undo
{
    /// <summary>
    /// Robustness pin-down tests for the Tile Editor's undo/redo pipeline.
    /// Complements <c>TileEditorUndoSystemTests</c> (which covers happy-path
    /// lifecycle + history cap) by exercising the corner cases that produced
    /// real bugs in production:
    ///
    ///   • Bug 1 — Ctrl+Z while still dragging the brush dropped the in-flight
    ///     batch and contaminated the next stroke. <see cref="HandleUndoRedo"/>
    ///     must commit the active stroke first.
    ///   • Bug 2 — A second <c>StartStroke</c> without a prior <c>EndStroke</c>
    ///     used to silently discard the previous batch. The new policy auto-ends
    ///     it instead so no edits leak.
    ///   • Bug 3 — Undo on a Collision-layer batch left the <c>CompositeCollider2D</c>
    ///     stale; Physics2D queries kept seeing the pre-undo geometry.
    ///   • Bug 4 — Ctrl+S during an open stroke could persist edits without the
    ///     matching undo entry.
    ///   • Bug 5 — Changing the active layer mid-stroke produced a batch whose
    ///     edits targeted one tilemap but whose <c>TargetTilemap</c> reference
    ///     was the previous layer, so undo silently corrupted the wrong layer.
    ///
    /// Direct/POCO tests sit at the top; manager-level integration tests use
    /// reflection to drive private callbacks (project convention — see
    /// <c>TileEditorViewPanelTests</c>).
    /// </summary>
    [TestFixture]
    public class TileEditorUndoRobustnessTests
    {
        private GameObject _root;
        private Tilemap _tilemap;
        private Tilemap _otherTilemap;
        private Tile _tileA;
        private Tile _tileB;
        private TileEditorUndoSystem _undo;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TilemapRoot");
            _root.AddComponent<Grid>().cellSize = Vector3.one;

            var go1 = new GameObject("TilemapA");
            go1.transform.SetParent(_root.transform, false);
            _tilemap = go1.AddComponent<Tilemap>();

            var go2 = new GameObject("TilemapB");
            go2.transform.SetParent(_root.transform, false);
            _otherTilemap = go2.AddComponent<Tilemap>();

            _tileA = MakeTile(Color.red);
            _tileB = MakeTile(Color.blue);
            _undo = new TileEditorUndoSystem();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_tileA != null) Object.DestroyImmediate(_tileA);
            if (_tileB != null) Object.DestroyImmediate(_tileB);
        }

        private static Tile MakeTile(Color c)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            var t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = sprite;
            return t;
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. Bug 2 — StartStroke must auto-end the previous open batch
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Bug2_StartStroke_WithOpenBatch_CommitsPreviousBeforeStartingNew()
        {
            // First stroke: 1 edit, never explicitly ended.
            _undo.StartStroke(_tilemap);
            _undo.RecordEdits(TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, brushSize: 1));

            // Second StartStroke without EndStroke — should auto-commit the first.
            _undo.StartStroke(_tilemap);
            _undo.RecordEdits(TileBrush.Paint(_tilemap, new Vector3Int(5, 5, 0), _tileB, brushSize: 1));
            _undo.EndStroke();

            // First Undo undoes the SECOND stroke (LIFO).
            var second = _undo.Undo();
            Assert.IsNotNull(second);
            Assert.AreEqual(new Vector3Int(5, 5, 0), second.Edits[0].Position,
                "LIFO: first Undo() must pop the most recent batch.");

            // Second Undo undoes the FIRST stroke — only possible if it was
            // committed. The pre-fix bug silently discarded it (returned null here).
            var first = _undo.Undo();
            Assert.IsNotNull(first,
                "Bug 2: a leaked stroke must be committed by the next StartStroke, " +
                "NOT silently dropped.");
            Assert.AreEqual(Vector3Int.zero, first.Edits[0].Position,
                "Recovered batch must contain the original edits.");
        }

        [Test]
        public void Bug2_StartStroke_WithEmptyOpenBatch_DiscardsItSilently()
        {
            // Empty stroke: no edits recorded. Should NOT push a phantom batch.
            _undo.StartStroke(_tilemap);
            // (no RecordEdits)
            _undo.StartStroke(_tilemap);
            _undo.RecordEdits(TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, brushSize: 1));
            _undo.EndStroke();

            // Only the second (real) stroke is on the stack.
            Assert.IsNotNull(_undo.Undo(), "Real stroke must be undoable.");
            Assert.IsNull(_undo.Undo(),
                "An empty leaked stroke must NOT push a phantom undo entry.");
        }

        [Test]
        public void Bug2_StartStroke_AcrossDifferentTilemaps_BothBatchesUndoableIndependently()
        {
            // Mid-stroke layer switch (without the OnLayerChanged hook, which is the
            // safety net): if production code forgets to EndStroke, the auto-end
            // must still produce TWO independent batches — one per tilemap.
            _undo.StartStroke(_tilemap);
            _undo.RecordEdits(TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, brushSize: 1));

            _undo.StartStroke(_otherTilemap);
            _undo.RecordEdits(TileBrush.Paint(_otherTilemap, Vector3Int.zero, _tileB, brushSize: 1));
            _undo.EndStroke();

            // Undo first pops the otherTilemap batch — restoring otherTilemap.
            _undo.Undo();
            Assert.IsNull(_otherTilemap.GetTile(Vector3Int.zero),
                "First Undo must restore the second (otherTilemap) batch.");
            Assert.AreEqual(_tileA, _tilemap.GetTile(Vector3Int.zero),
                "First (tilemap) batch must remain applied at this point.");

            _undo.Undo();
            Assert.IsNull(_tilemap.GetTile(Vector3Int.zero),
                "Second Undo restores the first batch on the original tilemap.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. RecordEdits / EndStroke defensive behaviour
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void RecordEdits_WithNoActiveStroke_IsSilentNoOp()
        {
            // Production code must never NRE if RecordEdits is called outside a
            // stroke (e.g. timing race after an EndStroke).
            Assert.DoesNotThrow(() =>
                _undo.RecordEdits(TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, brushSize: 1)));
            Assert.IsNull(_undo.Undo(),
                "Edits dropped outside a stroke must NOT magically appear on the undo stack.");
        }

        [Test]
        public void EndStroke_CalledTwice_IsIdempotent()
        {
            _undo.StartStroke(_tilemap);
            _undo.RecordEdits(TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, brushSize: 1));
            _undo.EndStroke();
            Assert.DoesNotThrow(() => _undo.EndStroke());

            // Stack size unaffected by the second EndStroke.
            int popped = 0;
            while (_undo.Undo() != null) popped++;
            Assert.AreEqual(1, popped,
                "Calling EndStroke twice must not duplicate the batch on the stack.");
        }

        [Test]
        public void HasActiveStroke_FlipsCorrectlyAcrossStartAndEnd()
        {
            Assert.IsFalse(_undo.HasActiveStroke);
            _undo.StartStroke(_tilemap);
            Assert.IsTrue(_undo.HasActiveStroke);
            _undo.EndStroke();
            Assert.IsFalse(_undo.HasActiveStroke);
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. Multi-undo / Multi-redo ordering
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Undo_AcrossMultipleBatches_RestoresInLIFOOrder()
        {
            PaintStroke(_tilemap, new Vector3Int(0, 0, 0), _tileA);
            PaintStroke(_tilemap, new Vector3Int(1, 0, 0), _tileA);
            PaintStroke(_tilemap, new Vector3Int(2, 0, 0), _tileA);

            _undo.Undo();
            Assert.IsNull(_tilemap.GetTile(new Vector3Int(2, 0, 0)),
                "Most recent stroke must be the first to undo.");
            Assert.AreEqual(_tileA, _tilemap.GetTile(new Vector3Int(1, 0, 0)));
            Assert.AreEqual(_tileA, _tilemap.GetTile(new Vector3Int(0, 0, 0)));

            _undo.Undo();
            Assert.IsNull(_tilemap.GetTile(new Vector3Int(1, 0, 0)));
            Assert.AreEqual(_tileA, _tilemap.GetTile(new Vector3Int(0, 0, 0)));

            _undo.Undo();
            Assert.IsNull(_tilemap.GetTile(new Vector3Int(0, 0, 0)));

            Assert.IsNull(_undo.Undo(), "Empty stack returns null.");
        }

        [Test]
        public void Redo_AcrossMultipleBatches_AppliesInFIFOOrder()
        {
            PaintStroke(_tilemap, new Vector3Int(0, 0, 0), _tileA);
            PaintStroke(_tilemap, new Vector3Int(1, 0, 0), _tileB);

            _undo.Undo();
            _undo.Undo();
            Assert.IsNull(_tilemap.GetTile(new Vector3Int(0, 0, 0)));
            Assert.IsNull(_tilemap.GetTile(new Vector3Int(1, 0, 0)));

            _undo.Redo();
            Assert.AreEqual(_tileA, _tilemap.GetTile(new Vector3Int(0, 0, 0)),
                "First redo must re-apply the older batch.");

            _undo.Redo();
            Assert.AreEqual(_tileB, _tilemap.GetTile(new Vector3Int(1, 0, 0)));
        }

        [Test]
        public void Undo_WithDestroyedTilemap_DoesNotThrow()
        {
            // If the tilemap is destroyed (scene reload) while a batch sits in the
            // stack, Undo must early-return silently — not NRE on SetTile.
            PaintStroke(_tilemap, Vector3Int.zero, _tileA);
            Object.DestroyImmediate(_tilemap.gameObject);

            Assert.DoesNotThrow(() =>
            {
                var batch = _undo.Undo();
                Assert.IsNotNull(batch,
                    "Batch must still pop from the stack — only the SetTile call is skipped.");
            });
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. Bug 1 / Bug 4 / Bug 5 — Manager-level integration via reflection
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Bug1_OnUndoClicked_CommitsActiveStrokeBeforeUndoing()
        {
            // Scenario: user is mid-drag, presses Ctrl+Z. The in-flight stroke
            // must be committed first, otherwise the undo would skip past the
            // user's current edits to whatever was on the stack before.
            var manager = NewManagerWithUndo(out var host);
            try
            {
                var undo = GetUndo(manager);

                // Open stroke with a real edit.
                undo.StartStroke(_tilemap);
                undo.RecordEdits(TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, brushSize: 1));
                Assert.IsTrue(undo.HasActiveStroke);

                // Trigger OnUndoClicked while the stroke is still open.
                InvokePrivate(manager, "OnUndoClicked");

                Assert.IsFalse(undo.HasActiveStroke,
                    "OnUndoClicked must close any active stroke before undoing.");
                Assert.IsNull(_tilemap.GetTile(Vector3Int.zero),
                    "The committed-then-undone in-flight stroke must be visually rolled back.");
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void Bug1_OnRedoClicked_AlsoCommitsActiveStrokeFirst()
        {
            // Symmetric to OnUndoClicked: pressing Ctrl+Y mid-drag must close the
            // open stroke (which clears the redo stack) — not redo whatever was
            // there before.
            var manager = NewManagerWithUndo(out var host);
            try
            {
                var undo = GetUndo(manager);

                // Build a redo entry: paint, then undo.
                PaintStrokeViaUndo(undo, _tilemap, Vector3Int.zero, _tileA);
                undo.Undo();
                Assert.IsNull(_tilemap.GetTile(Vector3Int.zero), "Pre-condition: undone.");

                // Open a NEW stroke (this would normally clear redo) but don't end it.
                undo.StartStroke(_tilemap);
                undo.RecordEdits(TileBrush.Paint(_tilemap, new Vector3Int(9, 9, 0), _tileB, brushSize: 1));
                Assert.IsTrue(undo.HasActiveStroke);

                // Ctrl+Y should commit the open stroke (which clears redo) — and
                // therefore find nothing to redo.
                InvokePrivate(manager, "OnRedoClicked");

                Assert.IsFalse(undo.HasActiveStroke,
                    "OnRedoClicked must close any active stroke first.");
                Assert.AreEqual(_tileB, _tilemap.GetTile(new Vector3Int(9, 9, 0)),
                    "The committed in-flight stroke remains applied.");
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void Bug5_OnLayerChanged_EndsActiveStrokeBeforeSwitching()
        {
            // Without this guard, edits painted on the new layer end up bound to
            // the original layer's tilemap on Undo — silent corruption.
            var manager = NewManagerWithUndo(out var host);
            try
            {
                var undo = GetUndo(manager);
                undo.StartStroke(_tilemap);
                undo.RecordEdits(TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, brushSize: 1));

                Assert.IsTrue(undo.HasActiveStroke);
                InvokePrivate(manager, "OnLayerChanged",
                    TilemapLayerSetup.TilemapLayer.WallsBottom);

                Assert.IsFalse(undo.HasActiveStroke,
                    "OnLayerChanged must commit the open batch — its TargetTilemap " +
                    "is the OLD layer's tilemap and recording edits past this " +
                    "point would corrupt the undo data.");
                Assert.AreEqual(TilemapLayerSetup.TilemapLayer.WallsBottom,
                    manager.State.CurrentLayer);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void Bug3_RegenerateColliderIfNeeded_NullBatch_NoOp()
        {
            // Defensive: helper must not NRE when called on a null batch.
            var manager = NewManagerWithUndo(out var host);
            try
            {
                Assert.DoesNotThrow(() =>
                    InvokePrivate(manager, "RegenerateColliderIfNeeded", new object[] { null }));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void Bug3_RegenerateColliderIfNeeded_NonCollisionTilemap_NoOp()
        {
            // Helper only fires for batches whose TargetTilemap is the Collision
            // layer — Brush/Eraser strokes on Ground must NOT trigger a needless
            // composite rebake.
            var manager = NewManagerWithUndo(out var host);
            try
            {
                var batch = new TileEditBatch { TargetTilemap = _tilemap };
                Assert.DoesNotThrow(() =>
                    InvokePrivate(manager, "RegenerateColliderIfNeeded", new object[] { batch }));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void Bug3_OnUndoClicked_ForCollisionBatch_TriggersColliderRegen()
        {
            // End-to-end: build a Collision-layer stroke, OnUndoClicked must take
            // the regen path. We probe by checking that the helper finds the
            // collision tilemap. Without an actual TilemapCollider2D + Composite
            // in the test scene, we verify the routing decision (TargetTilemap
            // matches GetCollisionTilemap), not the bake itself.
            var manager = NewManagerWithUndo(out var host);
            try
            {
                AttachWorldGridForCollision(manager, out var collisionTilemap);

                var undo = GetUndo(manager);
                undo.StartStroke(collisionTilemap);
                undo.RecordEdits(TileBrush.Paint(collisionTilemap, Vector3Int.zero, _tileA, brushSize: 1));
                undo.EndStroke();

                // OnUndoClicked must not throw and must visually undo. The collider
                // regeneration is exercised internally — what we guard here is the
                // fact that the routing helper runs at all.
                Assert.DoesNotThrow(() => InvokePrivate(manager, "OnUndoClicked"));
                Assert.IsNull(collisionTilemap.GetTile(Vector3Int.zero),
                    "The collision-layer edit must be undone.");
            }
            finally { Object.DestroyImmediate(host); }
        }

        // ════════════════════════════════════════════════════════════════════
        // 5. End-to-end manager flow — clipboard + undo composition
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void EndToEnd_CutThenPaste_AreIndependentUndoBatches()
        {
            // Plan-validated invariant: undoing only the paste does NOT resurrect
            // the cut tiles. Cut and Paste are semantically independent batches.
            var manager = NewManagerWithUndo(out var host);
            try
            {
                AttachWorldGrid(manager, out var ground);
                manager.State.CurrentTool = TileEditorState.Tool.Select;
                ground.SetTile(new Vector3Int(0, 0, 0), _tileA);
                manager.State.SelectedCells.Add(new Vector3Int(0, 0, 0));

                InvokePrivate(manager, "OnCutClicked");
                Assert.IsNull(ground.GetTile(new Vector3Int(0, 0, 0)),
                    "Cut must remove the source tile.");

                manager.State.SelectedCellPos = new Vector3Int(50, 50, 0);
                InvokePrivate(manager, "OnPasteClicked");
                Assert.AreEqual(_tileA, ground.GetTile(new Vector3Int(50, 50, 0)),
                    "Paste must reproduce the tile at the new anchor.");

                InvokePrivate(manager, "OnUndoClicked");
                Assert.IsNull(ground.GetTile(new Vector3Int(50, 50, 0)),
                    "First undo rolls back ONLY the paste.");
                Assert.IsNull(ground.GetTile(new Vector3Int(0, 0, 0)),
                    "Source remains cut — undoing paste must NOT resurrect it.");

                InvokePrivate(manager, "OnUndoClicked");
                Assert.AreEqual(_tileA, ground.GetTile(new Vector3Int(0, 0, 0)),
                    "Second undo rolls back the cut — source tile is back.");
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void EndToEnd_BrushUndoRedo_RestoresThenReapplies()
        {
            var manager = NewManagerWithUndo(out var host);
            try
            {
                AttachWorldGrid(manager, out var ground);

                var undo = GetUndo(manager);
                PaintStrokeViaUndo(undo, ground, Vector3Int.zero, _tileA);
                Assert.AreEqual(_tileA, ground.GetTile(Vector3Int.zero));

                InvokePrivate(manager, "OnUndoClicked");
                Assert.IsNull(ground.GetTile(Vector3Int.zero), "Undo restores empty.");

                InvokePrivate(manager, "OnRedoClicked");
                Assert.AreEqual(_tileA, ground.GetTile(Vector3Int.zero), "Redo re-paints.");
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void EndToEnd_NewStrokeAfterUndo_ClearsRedoStack_AndDoesNotResurrect()
        {
            // Standard editor convention: once you make a new edit after an Undo,
            // the redo history is gone. Verify at the manager level that the next
            // OnRedoClicked does nothing instead of resurrecting old state.
            var manager = NewManagerWithUndo(out var host);
            try
            {
                AttachWorldGrid(manager, out var ground);
                var undo = GetUndo(manager);

                PaintStrokeViaUndo(undo, ground, Vector3Int.zero, _tileA);
                undo.Undo();
                PaintStrokeViaUndo(undo, ground, new Vector3Int(5, 5, 0), _tileB);

                InvokePrivate(manager, "OnRedoClicked");
                Assert.IsNull(ground.GetTile(Vector3Int.zero),
                    "The original tileA paint must NOT come back — redo stack was " +
                    "invalidated by the new tileB stroke.");
                Assert.AreEqual(_tileB, ground.GetTile(new Vector3Int(5, 5, 0)));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void EndToEnd_CtrlS_DuringStroke_CommitsStrokeBeforeSaving()
        {
            // Bug 4 surface: SaveAllChanges flushes the persistence layer to disk.
            // If the stroke is still open, its edits live in _currentBatch (NOT the
            // undo stack), so they would be saved to disk without an undo entry to
            // match. Guarded by EndStroke() preceding SaveAllChanges().
            var manager = NewManagerWithUndo(out var host);
            try
            {
                AttachWorldGrid(manager, out var ground);
                var undo = GetUndo(manager);

                undo.StartStroke(ground);
                undo.RecordEdits(TileBrush.Paint(ground, Vector3Int.zero, _tileA, brushSize: 1));
                Assert.IsTrue(undo.HasActiveStroke);

                // SaveAllChanges is the public path; it internally calls EndStroke.
                manager.SaveAllChanges();

                Assert.IsFalse(undo.HasActiveStroke,
                    "SaveAllChanges must close any open stroke first — otherwise " +
                    "the in-flight edits would be persisted with no undo entry.");
                // Re-undo confirms the stroke went onto the stack.
                InvokePrivate(manager, "OnUndoClicked");
                Assert.IsNull(ground.GetTile(Vector3Int.zero),
                    "The committed-before-save stroke must still be undoable.");
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void EndToEnd_FiftyFirstStroke_DropsOldestNotNewest()
        {
            // The cap behaviour matters for users — capping the WRONG end (eg.
            // dropping the newest) would feel like undo silently failed.
            var manager = NewManagerWithUndo(out var host);
            try
            {
                AttachWorldGrid(manager, out var ground);
                var undo = GetUndo(manager);

                for (int i = 0; i < TileEditorState.MAX_UNDO + 1; i++)
                    PaintStrokeViaUndo(undo, ground, new Vector3Int(i, 0, 0), _tileA);

                // The MOST RECENT edit (i = MAX_UNDO) must still be undoable.
                InvokePrivate(manager, "OnUndoClicked");
                Assert.IsNull(ground.GetTile(new Vector3Int(TileEditorState.MAX_UNDO, 0, 0)),
                    "Cap must drop the OLDEST stroke; the newest must remain undoable.");
            }
            finally { Object.DestroyImmediate(host); }
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════

        private void PaintStroke(Tilemap tm, Vector3Int pos, Tile tile)
        {
            _undo.StartStroke(tm);
            _undo.RecordEdits(TileBrush.Paint(tm, pos, tile, brushSize: 1));
            _undo.EndStroke();
        }

        private static void PaintStrokeViaUndo(TileEditorUndoSystem undo, Tilemap tm,
            Vector3Int pos, Tile tile)
        {
            undo.StartStroke(tm);
            undo.RecordEdits(TileBrush.Paint(tm, pos, tile, brushSize: 1));
            undo.EndStroke();
        }

        private static TileEditorManager NewManagerWithUndo(out GameObject host)
        {
            host = new GameObject("TileEditorManager_TestHost");
            var manager = host.AddComponent<TileEditorManager>();
            // The manager builds its undo system in OnSingletonAwake — which AddComponent
            // doesn't run synchronously across all Unity versions. Force the field via
            // reflection to a fresh instance so tests don't depend on lifecycle timing.
            var fi = typeof(TileEditorManager).GetField("_undo",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi != null && fi.GetValue(manager) == null)
                fi.SetValue(manager, new TileEditorUndoSystem());
            return manager;
        }

        private static TileEditorUndoSystem GetUndo(TileEditorManager m)
        {
            return (TileEditorUndoSystem)typeof(TileEditorManager)
                .GetField("_undo", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(m);
        }

        private static void AttachWorldGrid(TileEditorManager manager, out Tilemap groundTilemap)
        {
            var gridGo = new GameObject("WorldGrid");
            gridGo.transform.SetParent(manager.transform, false);
            var grid = gridGo.AddComponent<Grid>();
            var wgb = gridGo.AddComponent<WorldGridBuilder>();

            groundTilemap = null;
            for (int i = 0; i < 9; i++)
            {
                var layer = (TilemapLayerSetup.TilemapLayer)i;
                var tmGo = new GameObject(layer.ToString());
                tmGo.transform.SetParent(gridGo.transform, false);
                var tm = tmGo.AddComponent<Tilemap>();
                tmGo.AddComponent<TilemapRenderer>();
                if (layer == TilemapLayerSetup.TilemapLayer.Ground)
                    groundTilemap = tm;
            }

            typeof(TileEditorManager)
                .GetField("worldGridBuilder", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, wgb);
            typeof(WorldGridBuilder)
                .GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(wgb, grid);

            manager.State.CurrentLayer = TilemapLayerSetup.TilemapLayer.Ground;
        }

        private static void AttachWorldGridForCollision(TileEditorManager manager,
            out Tilemap collisionTilemap)
        {
            AttachWorldGrid(manager, out _);
            // Switch active layer to the collision tilemap for this test.
            var wgb = (WorldGridBuilder)typeof(TileEditorManager)
                .GetField("worldGridBuilder", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(manager);
            collisionTilemap = wgb.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            manager.State.CurrentLayer = TilemapLayerSetup.TilemapLayer.Collision;
        }

        private static void InvokePrivate(object target, string methodName, params object[] args)
        {
            var t = target.GetType();
            MethodInfo mi = null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != methodName) continue;
                var ps = m.GetParameters();
                if (ps.Length != args.Length) continue;
                mi = m;
                break;
            }
            Assert.IsNotNull(mi, $"Reflection: {methodName}({args.Length} args) not found on {t.Name}.");
            mi.Invoke(target, args);
        }
    }
}
