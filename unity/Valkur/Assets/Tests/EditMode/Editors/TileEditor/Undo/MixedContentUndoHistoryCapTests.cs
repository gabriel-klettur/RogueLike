using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Undo
{
    /// <summary>
    /// The MAX_UNDO=50 history cap is already pinned for HOMOGENEOUS batch
    /// streams — all-tile strokes in
    /// <c>TileEditorUndoSystemTests.UndoStack_NeverExceedsMaxUndoLimit</c> and
    /// <c>TileEditorUndoRobustnessTests.EndToEnd_FiftyFirstStroke_DropsOldestNotNewest</c>;
    /// all-cross-tilemap strokes in
    /// <c>TileEditBatchCrossTilemapTests.CrossTilemap_HistoryCap_EvictsOldestBatch</c>.
    ///
    /// None of those exercise a stream that MIXES ordinary tile strokes with the
    /// metadata-only batches the <c>HasContent</c> fix newly allows onto the
    /// stack (<c>Edits.Count == 0 &amp;&amp; MetadataEdits.Count &gt; 0</c> — the exact
    /// shape of a Layer-Jumps stroke, see <c>LayerJumpsUndoTests</c>). The
    /// eviction logic itself (<c>TileEditorUndoSystem.EndStroke</c>'s
    /// <c>_undoStack.RemoveAt(0)</c>) is content-blind, but that is exactly the
    /// kind of "should obviously be fine" assumption worth pinning once
    /// explicitly rather than trusting by inspection — a future change that
    /// special-cased eviction by batch shape would slip past every existing
    /// (homogeneous) cap test without failing a single one of them.
    /// </summary>
    [TestFixture]
    public class MixedContentUndoHistoryCapTests
    {
        private GameObject _root;
        private Tilemap _tilemap;
        private Tile _tileA;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("MixedCapRoot");
            _root.AddComponent<Grid>().cellSize = Vector3.one;
            var go = new GameObject("Tilemap");
            go.transform.SetParent(_root.transform, false);
            _tilemap = go.AddComponent<Tilemap>();

            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.magenta);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            _tileA = ScriptableObject.CreateInstance<Tile>();
            _tileA.sprite = sprite;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_tileA != null) Object.DestroyImmediate(_tileA);
        }

        [Test]
        public void MixedTileAndMetadataOnlyStrokes_ExceedingMaxUndo_CapsAndEvictsOldestRegardlessOfContent()
        {
            var jumpMap = new LayerJumpMap();
            var undo = new TileEditorUndoSystem();
            int total = TileEditorState.MAX_UNDO + 5;

            for (int i = 0; i < total; i++)
            {
                var cell = new Vector3Int(i, 0, 0);
                if (i % 2 == 0)
                {
                    // Tile-only stroke — mirrors an ordinary Brush paint (Edits
                    // non-empty, MetadataEdits empty).
                    undo.StartStroke(_tilemap);
                    _tilemap.SetTile(cell, _tileA);
                    undo.RecordEdits(new List<TileEdit> { new TileEdit(cell, null, _tileA) });
                    undo.EndStroke();
                }
                else
                {
                    // Metadata-only stroke, no tilemap — mirrors a Layer-Jumps
                    // paint (Edits empty, MetadataEdits non-empty).
                    string target = (i % 9).ToString();
                    undo.StartStroke(null);
                    jumpMap.Set(cell, target);
                    undo.RecordMetadataEdits(new List<MetadataEdit> { new MetadataEdit(cell, string.Empty, target, jumpMap) });
                    undo.EndStroke();
                }
            }

            int popped = 0;
            while (undo.Undo() != null) popped++;
            Assert.AreEqual(TileEditorState.MAX_UNDO, popped,
                "Stack must cap at MAX_UNDO regardless of whether the evicted/kept batches are tile-only, " +
                "metadata-only, or a mix of both.");

            int evictedCount = total - TileEditorState.MAX_UNDO;

            // The oldest strokes (i = 0 .. evictedCount-1) were evicted before
            // they could ever reach Undo() — their effects must still be
            // sitting there, untouched.
            for (int i = 0; i < evictedCount; i++)
            {
                var cell = new Vector3Int(i, 0, 0);
                if (i % 2 == 0)
                    Assert.AreEqual(_tileA, _tilemap.GetTile(cell),
                        $"Evicted TILE stroke at i={i} must remain applied — it was never reached by Undo.");
                else
                    Assert.AreEqual((i % 9).ToString(), jumpMap.Get(cell),
                        $"Evicted METADATA stroke at i={i} must remain applied — it was never reached by Undo.");
            }

            // The most recent MAX_UNDO strokes (i = evictedCount .. total-1) were
            // all popped by the drain loop above — every one of them must be
            // fully reverted, regardless of which kind it was.
            for (int i = evictedCount; i < total; i++)
            {
                var cell = new Vector3Int(i, 0, 0);
                if (i % 2 == 0)
                    Assert.IsNull(_tilemap.GetTile(cell),
                        $"Kept-within-cap TILE stroke at i={i} must have been undone.");
                else
                    Assert.AreEqual(string.Empty, jumpMap.Get(cell),
                        $"Kept-within-cap METADATA stroke at i={i} must have been undone.");
            }
        }
    }
}
