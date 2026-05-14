using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Undo
{
    /// <summary>
    /// Covers the cross-tilemap capability added to <see cref="TileEdit"/> +
    /// <see cref="TileEditBatch"/> so a single Ctrl+Z can revert an operation that
    /// touched two tilemaps (e.g. the Tile Editor's Move-To-Layer action: source
    /// cleared on one layer, paint applied to another in the same batch).
    ///
    /// The tests deliberately drive <see cref="TileEditorUndoSystem"/> directly
    /// rather than the higher-level manager, because the manager is a
    /// MonoBehaviour that would require a Grid + WorldGridBuilder scene to spin
    /// up in EditMode. The semantics that matter for the new feature live in
    /// the batch's per-edit <c>TargetTilemap</c> dispatch, which is what this
    /// suite pins.
    /// </summary>
    [TestFixture]
    public class TileEditBatchCrossTilemapTests
    {
        private GameObject _root;
        private Tilemap _sourceTm;
        private Tilemap _destTm;
        private Tile _tileA;
        private Tile _tileB;
        private TileEditorUndoSystem _undo;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("CrossTilemapRoot");
            _root.AddComponent<Grid>().cellSize = Vector3.one;

            var srcGo = new GameObject("SourceTilemap");
            srcGo.transform.SetParent(_root.transform, false);
            _sourceTm = srcGo.AddComponent<Tilemap>();

            var dstGo = new GameObject("DestTilemap");
            dstGo.transform.SetParent(_root.transform, false);
            _destTm = dstGo.AddComponent<Tilemap>();

            _tileA = MakeTile(Color.red);
            _tileB = MakeTile(Color.blue);
            _undo  = new TileEditorUndoSystem();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_tileA);
            Object.DestroyImmediate(_tileB);
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

        /// <summary>
        /// Backwards compatibility: a TileEdit constructed with the legacy 3-arg
        /// ctor (no per-edit TargetTilemap) must still apply to the batch's
        /// fallback tilemap on both Undo and Redo. This is the contract every
        /// existing brush/eraser/fill/paste call site relies on.
        /// </summary>
        [Test]
        public void SingleTilemap_LegacyEdits_StillApplyToBatchFallback()
        {
            var cell = new Vector3Int(2, 3, 0);

            _undo.StartStroke(_sourceTm);
            _sourceTm.SetTile(cell, _tileA);
            _undo.RecordEdits(new List<TileEdit> { new TileEdit(cell, null, _tileA) });
            _undo.EndStroke();

            Assert.AreEqual(_tileA, _sourceTm.GetTile(cell), "Initial paint should land on the source tilemap.");

            _undo.Undo();
            Assert.IsNull(_sourceTm.GetTile(cell), "Undo should restore the source tilemap to its pre-edit state.");

            _undo.Redo();
            Assert.AreEqual(_tileA, _sourceTm.GetTile(cell), "Redo should re-apply to the same fallback tilemap.");
        }

        /// <summary>
        /// The Move-To-Layer scenario: a single batch contains two edits per cell
        /// — one clearing the source tilemap, one painting the destination — and
        /// each edit carries its own <c>TargetTilemap</c>. Undo must reverse BOTH
        /// halves; the fallback should never trip in for these edits.
        /// </summary>
        [Test]
        public void CrossTilemap_MovePattern_UndoRestoresBothLayers()
        {
            var cell = new Vector3Int(1, 0, 0);
            _sourceTm.SetTile(cell, _tileA);
            // destTm starts empty at cell
            Assume.That(_destTm.GetTile(cell), Is.Null, "Pre-condition: destination starts empty.");

            // Simulate OnMoveToLayerClicked's per-cell logic.
            _undo.StartStroke(_sourceTm); // batch fallback = source, but every edit overrides it
            var oldDst = _destTm.GetTile(cell);
            _sourceTm.SetTile(cell, null);
            _destTm.SetTile(cell, _tileA);
            _undo.RecordEdits(new List<TileEdit>
            {
                new TileEdit(cell, _tileA, null,  _sourceTm),
                new TileEdit(cell, oldDst, _tileA, _destTm),
            });
            _undo.EndStroke();

            Assert.IsNull(_sourceTm.GetTile(cell), "Source should be cleared after the move.");
            Assert.AreEqual(_tileA, _destTm.GetTile(cell), "Destination should hold the moved tile.");

            // Single Undo reverses BOTH halves in one atomic operation.
            _undo.Undo();
            Assert.AreEqual(_tileA, _sourceTm.GetTile(cell), "Undo should restore the source tile.");
            Assert.IsNull(_destTm.GetTile(cell), "Undo should clear the destination tile.");

            // Single Redo re-applies BOTH halves.
            _undo.Redo();
            Assert.IsNull(_sourceTm.GetTile(cell), "Redo should re-clear the source.");
            Assert.AreEqual(_tileA, _destTm.GetTile(cell), "Redo should re-paint the destination.");
        }

        /// <summary>
        /// Mixed batch: some edits with per-edit TargetTilemap (cross-layer move)
        /// interleaved with legacy edits (no override). Each kind must dispatch
        /// correctly — the overrides hit their own tilemap, the legacy ones fall
        /// back to the batch's tilemap.
        /// </summary>
        [Test]
        public void MixedBatch_PerEditAndFallback_DispatchIndependently()
        {
            var crossCell  = new Vector3Int(0, 0, 0);
            var legacyCell = new Vector3Int(5, 5, 0);

            _sourceTm.SetTile(crossCell,  _tileA);
            _sourceTm.SetTile(legacyCell, _tileB);

            _undo.StartStroke(_sourceTm);

            // Cross-layer half: clear source[crossCell] + paint dest[crossCell]
            _sourceTm.SetTile(crossCell, null);
            _destTm.SetTile(crossCell, _tileA);

            // Legacy edit: repaint legacyCell on the source tilemap (batch fallback)
            _sourceTm.SetTile(legacyCell, _tileA); // overwrites _tileB

            _undo.RecordEdits(new List<TileEdit>
            {
                new TileEdit(crossCell,  _tileA, null,  _sourceTm),
                new TileEdit(crossCell,  null,   _tileA, _destTm),
                new TileEdit(legacyCell, _tileB, _tileA), // no target ⇒ fallback to batch (source)
            });
            _undo.EndStroke();

            // Undo: cross-layer half restored AND legacy half rolled back, in one shot.
            _undo.Undo();
            Assert.AreEqual(_tileA, _sourceTm.GetTile(crossCell),  "Cross-half: source restored.");
            Assert.IsNull(_destTm.GetTile(crossCell),               "Cross-half: dest cleared.");
            Assert.AreEqual(_tileB, _sourceTm.GetTile(legacyCell), "Legacy edit: fallback rolled back to old tile.");
        }

        /// <summary>
        /// An edit with a null per-edit TargetTilemap and a null batch fallback
        /// must be silently skipped — neither Undo nor Redo should NRE. (Defensive
        /// path; not a happy-flow scenario, but the production code guards for it.)
        /// </summary>
        [Test]
        public void NullTilemapEdit_IsSkippedWithoutThrowing()
        {
            var batch = new TileEditBatch
            {
                TargetTilemap = null, // and edits below also have null target
                Edits = new List<TileEdit>
                {
                    new TileEdit(new Vector3Int(0, 0, 0), null, _tileA),
                }
            };

            Assert.DoesNotThrow(() => batch.Undo(), "Undo must not throw on a fully-null edit.");
            Assert.DoesNotThrow(() => batch.Redo(), "Redo must not throw on a fully-null edit.");
        }

        /// <summary>
        /// The 3-arg <see cref="TileEdit"/> ctor (used by every legacy call site)
        /// must leave <see cref="TileEdit.TargetTilemap"/> at null so the batch's
        /// fallback dispatch kicks in. The 4-arg ctor must persist the override.
        /// Pin both contracts so a future refactor of the struct can't silently
        /// flip semantics for every existing brush / eraser / fill / paste call.
        /// </summary>
        [Test]
        public void TileEdit_LegacyCtor_LeavesTargetTilemapNull()
        {
            var legacy = new TileEdit(Vector3Int.zero, null, _tileA);
            Assert.IsNull(legacy.TargetTilemap, "3-arg ctor must default TargetTilemap to null.");

            var overridden = new TileEdit(Vector3Int.zero, null, _tileA, _destTm);
            Assert.AreSame(_destTm, overridden.TargetTilemap, "4-arg ctor must persist the per-edit target.");
        }

        /// <summary>
        /// Chained moves are the realistic authoring path: paint on Ground →
        /// move to FloorDecals → move again to WallsBottom. Each move is a
        /// separate batch on the undo stack; popping them in LIFO order must
        /// reverse each hop independently and return the tile to its origin.
        /// </summary>
        [Test]
        public void SequentialCrossTilemapMoves_UndoIndividuallyInLifoOrder()
        {
            // Three tilemaps: A → B → C, simulating Ground → FloorDecals → WallsBottom.
            var b = new GameObject("MidTilemap"); b.transform.SetParent(_root.transform, false);
            var midTm = b.AddComponent<Tilemap>();
            var c = new GameObject("FarTilemap"); c.transform.SetParent(_root.transform, false);
            var farTm = c.AddComponent<Tilemap>();
            try
            {
                var cell = new Vector3Int(4, 4, 0);
                _sourceTm.SetTile(cell, _tileA);

                // Move source → mid
                _undo.StartStroke(_sourceTm);
                _sourceTm.SetTile(cell, null);
                midTm.SetTile(cell, _tileA);
                _undo.RecordEdits(new List<TileEdit>
                {
                    new TileEdit(cell, _tileA, null,  _sourceTm),
                    new TileEdit(cell, null,   _tileA, midTm),
                });
                _undo.EndStroke();

                // Move mid → far
                _undo.StartStroke(midTm);
                midTm.SetTile(cell, null);
                farTm.SetTile(cell, _tileA);
                _undo.RecordEdits(new List<TileEdit>
                {
                    new TileEdit(cell, _tileA, null,  midTm),
                    new TileEdit(cell, null,   _tileA, farTm),
                });
                _undo.EndStroke();

                Assert.IsNull(_sourceTm.GetTile(cell), "Pre-undo: source empty.");
                Assert.IsNull(midTm.GetTile(cell),     "Pre-undo: mid empty.");
                Assert.AreEqual(_tileA, farTm.GetTile(cell), "Pre-undo: far holds the tile.");

                // First Undo reverses the second hop.
                _undo.Undo();
                Assert.IsNull(_sourceTm.GetTile(cell));
                Assert.AreEqual(_tileA, midTm.GetTile(cell), "After 1× undo: tile lives on mid again.");
                Assert.IsNull(farTm.GetTile(cell));

                // Second Undo reverses the first hop and returns to origin.
                _undo.Undo();
                Assert.AreEqual(_tileA, _sourceTm.GetTile(cell), "After 2× undo: tile back at origin.");
                Assert.IsNull(midTm.GetTile(cell));
                Assert.IsNull(farTm.GetTile(cell));
            }
            finally
            {
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(c);
            }
        }

        /// <summary>
        /// Committing a brand-new edit after an Undo wipes the redo stack — the
        /// stale "future" can't be recovered once history has branched. This is
        /// the canonical undo-system invariant; pinning it for cross-tilemap
        /// batches guards against a regression where the per-edit override path
        /// somehow shortcuts the standard Redo() clearing.
        /// </summary>
        [Test]
        public void CrossTilemap_NewEditAfterUndo_DropsRedoStack()
        {
            var cell = new Vector3Int(1, 1, 0);
            _sourceTm.SetTile(cell, _tileA);

            // Commit the cross-tilemap move.
            _undo.StartStroke(_sourceTm);
            _sourceTm.SetTile(cell, null);
            _destTm.SetTile(cell, _tileA);
            _undo.RecordEdits(new List<TileEdit>
            {
                new TileEdit(cell, _tileA, null,  _sourceTm),
                new TileEdit(cell, null,   _tileA, _destTm),
            });
            _undo.EndStroke();

            _undo.Undo();
            Assert.AreEqual(_tileA, _sourceTm.GetTile(cell));
            Assert.IsNull(_destTm.GetTile(cell));

            // New legacy edit on the source — should clear the redo stack so the
            // previous cross-tilemap move can no longer be redone.
            _undo.StartStroke(_sourceTm);
            _sourceTm.SetTile(cell, _tileB);
            _undo.RecordEdits(new List<TileEdit> { new TileEdit(cell, _tileA, _tileB) });
            _undo.EndStroke();

            // Calling Redo should be a no-op (redo stack was dropped); the
            // destination tilemap stays empty even though the original move
            // pre-Undo had populated it.
            var redone = _undo.Redo();
            Assert.IsNull(redone, "Redo stack must be empty after a fresh edit follows an Undo.");
            Assert.AreEqual(_tileB, _sourceTm.GetTile(cell), "Source must reflect the new edit, not the redone move.");
            Assert.IsNull(_destTm.GetTile(cell), "Destination must NOT come back from the dropped redo history.");
        }

        /// <summary>
        /// Move-To-Layer fires THREE edits per cell when the source visual tile also
        /// has a collider on the Collision layer:
        ///   A) clear source visual tile
        ///   B) paint destination visual tile
        ///   C) erase the collision cell (user-confirmed: visual tile moves → obstacle moves with it)
        /// All three live in the same batch via the per-edit <c>TargetTilemap</c> override.
        /// A single Ctrl+Z must restore source + dest + collider atomically.
        /// </summary>
        [Test]
        public void MoveToLayerWithCollider_SingleUndoRestoresAllThreeTilemaps()
        {
            var b = new GameObject("CollisionTilemap"); b.transform.SetParent(_root.transform, false);
            var collisionTm = b.AddComponent<Tilemap>();
            try
            {
                var cell = new Vector3Int(2, 2, 0);
                _sourceTm.SetTile(cell, _tileA);   // visual tile on source
                collisionTm.SetTile(cell, _tileB); // collider on Collision layer

                _undo.StartStroke(_sourceTm);
                // Phase A: clear source
                _sourceTm.SetTile(cell, null);
                // Phase B: paint dest
                _destTm.SetTile(cell, _tileA);
                // Phase C: erase collision (the M1 user-confirmed behaviour)
                collisionTm.SetTile(cell, null);

                _undo.RecordEdits(new List<TileEdit>
                {
                    new TileEdit(cell, _tileA, null,  _sourceTm),
                    new TileEdit(cell, null,   _tileA, _destTm),
                    new TileEdit(cell, _tileB, null,  collisionTm),
                });
                _undo.EndStroke();

                Assert.IsNull(_sourceTm.GetTile(cell),       "Pre-undo: source cleared.");
                Assert.AreEqual(_tileA, _destTm.GetTile(cell), "Pre-undo: dest holds visual tile.");
                Assert.IsNull(collisionTm.GetTile(cell),     "Pre-undo: collider erased.");

                _undo.Undo();

                Assert.AreEqual(_tileA, _sourceTm.GetTile(cell), "Undo: source visual restored.");
                Assert.IsNull(_destTm.GetTile(cell),             "Undo: dest cleared.");
                Assert.AreEqual(_tileB, collisionTm.GetTile(cell), "Undo: collider restored.");

                _undo.Redo();
                Assert.IsNull(_sourceTm.GetTile(cell));
                Assert.AreEqual(_tileA, _destTm.GetTile(cell));
                Assert.IsNull(collisionTm.GetTile(cell));
            }
            finally { Object.DestroyImmediate(b); }
        }

        /// <summary>
        /// The undo stack caps at <see cref="TileEditorState.MAX_UNDO"/> regardless of
        /// whether a batch is single- or cross-tilemap. Push N+1 cross-tilemap batches
        /// and verify the oldest is evicted (its Undo no longer reaches the source).
        /// </summary>
        [Test]
        public void CrossTilemap_HistoryCap_EvictsOldestBatch()
        {
            int cap = TileEditorState.MAX_UNDO;
            var origin = new Vector3Int(0, 0, 0);

            for (int i = 0; i <= cap; i++) // cap + 1 batches → first must be evicted
            {
                var cell = new Vector3Int(i, 0, 0);
                _sourceTm.SetTile(cell, _tileA);

                _undo.StartStroke(_sourceTm);
                _sourceTm.SetTile(cell, null);
                _destTm.SetTile(cell, _tileA);
                _undo.RecordEdits(new List<TileEdit>
                {
                    new TileEdit(cell, _tileA, null,  _sourceTm),
                    new TileEdit(cell, null,   _tileA, _destTm),
                });
                _undo.EndStroke();
            }

            // Drain the undo stack — it should hold exactly `cap` batches now.
            int undone = 0;
            while (_undo.Undo() != null) undone++;
            Assert.AreEqual(cap, undone, $"Undo stack must cap at MAX_UNDO ({cap}); the first batch should have been evicted.");

            // Cell (0,0,0) was touched by the EVICTED batch: source was cleared
            // and never restored on undo. Cell (cap,0,0) was the most recent batch
            // and DID get undone — source restored, destination cleared.
            Assert.IsNull(_sourceTm.GetTile(new Vector3Int(0, 0, 0)),
                "Eldest batch's source-clear was not reversible after eviction — cell stays empty.");
            Assert.AreEqual(_tileA, _destTm.GetTile(new Vector3Int(0, 0, 0)),
                "Eldest batch's dest-paint also can't be reversed once evicted.");

            Assert.AreEqual(_tileA, _sourceTm.GetTile(new Vector3Int(cap, 0, 0)),
                "Most recent batch (within cap) must have been undone.");
            Assert.IsNull(_destTm.GetTile(new Vector3Int(cap, 0, 0)),
                "Most recent batch's dest must be cleared by undo.");
        }
    }
}
