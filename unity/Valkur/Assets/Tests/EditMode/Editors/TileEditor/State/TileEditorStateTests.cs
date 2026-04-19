using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.State
{
    /// <summary>
    /// Exhaustive coverage of <see cref="TileEditorState"/> — the single mutable
    /// data class that drives every Tile Editor tool, panel and visual.
    ///
    /// These tests pin down the contract that every UI builder, input handler and
    /// undo entry depends on: default values, tool/layer enums, brush size bounds,
    /// collider sub-mode, selection cell, and the live brush-stroke set.
    /// All assertions are pure (no scene, no MonoBehaviour, no I/O).
    /// </summary>
    [TestFixture]
    public class TileEditorStateTests
    {
        private TileEditorState _state;

        [SetUp]
        public void SetUp()
        {
            _state = new TileEditorState();
        }

        // ── Defaults ─────────────────────────────────────────────────────

        [Test]
        public void Defaults_AreSafeForFreshSession()
        {
            Assert.IsFalse(_state.Active,                          "Editor must start inactive.");
            Assert.AreEqual(TileEditorState.Tool.Select, _state.CurrentTool, "Default tool is Select.");
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.Ground, _state.CurrentLayer, "Default layer is Ground.");
            Assert.IsNull(_state.SelectedTile,                      "No tile selected at start.");
            Assert.AreEqual(-1, _state.SelectedCatalogIndex,        "Catalog index sentinel must be -1.");
            Assert.AreEqual(string.Empty, _state.SelectedCategory);
            Assert.AreEqual(1, _state.BrushSize,                    "Default brush size is 1×1.");
            Assert.IsFalse(_state.IsDragging);
            Assert.IsFalse(_state.ShowColliderOverlay);
            Assert.AreEqual(TileEditorState.ColliderMode.None, _state.CurrentColliderMode);
            Assert.IsFalse(_state.SelectedCellPos.HasValue);
            Assert.IsNotNull(_state.BrushStrokeCells, "Stroke set must always be non-null.");
            Assert.AreEqual(0, _state.BrushStrokeCells.Count);
        }

        // ── Tool transitions ─────────────────────────────────────────────

        [TestCase(TileEditorState.Tool.Select)]
        [TestCase(TileEditorState.Tool.Brush)]
        [TestCase(TileEditorState.Tool.Eraser)]
        [TestCase(TileEditorState.Tool.Eyedropper)]
        [TestCase(TileEditorState.Tool.Fill)]
        public void CurrentTool_AllToolsAreReachable(TileEditorState.Tool tool)
        {
            _state.CurrentTool = tool;
            Assert.AreEqual(tool, _state.CurrentTool);
        }

        // ── Layer transitions ────────────────────────────────────────────

        [Test]
        public void CurrentLayer_AllLayerEnumValuesAreSupported()
        {
            // Every value declared on the layer enum must be assignable without throwing.
            // This guards against partial enum support in the editor state.
            foreach (TilemapLayerSetup.TilemapLayer layer in System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer)))
            {
                _state.CurrentLayer = layer;
                Assert.AreEqual(layer, _state.CurrentLayer, $"Layer {layer} round-trip failed.");
            }
        }

        // ── Collider mode ────────────────────────────────────────────────

        [Test]
        public void ColliderMode_DrawAndEraseAreDistinctFromNone()
        {
            _state.CurrentColliderMode = TileEditorState.ColliderMode.Draw;
            Assert.AreEqual(TileEditorState.ColliderMode.Draw, _state.CurrentColliderMode);

            _state.CurrentColliderMode = TileEditorState.ColliderMode.Erase;
            Assert.AreEqual(TileEditorState.ColliderMode.Erase, _state.CurrentColliderMode);

            _state.CurrentColliderMode = TileEditorState.ColliderMode.None;
            Assert.AreEqual(TileEditorState.ColliderMode.None, _state.CurrentColliderMode);
        }

        [Test]
        public void ShowColliderOverlay_TogglesIndependentlyOfMode()
        {
            // Visual toggle must NOT depend on edit mode (you can visualise without painting).
            _state.ShowColliderOverlay = true;
            Assert.IsTrue(_state.ShowColliderOverlay);
            Assert.AreEqual(TileEditorState.ColliderMode.None, _state.CurrentColliderMode);

            _state.CurrentColliderMode = TileEditorState.ColliderMode.Draw;
            Assert.IsTrue(_state.ShowColliderOverlay, "Toggling mode must not flip overlay visibility.");
        }

        // ── Brush size + stroke set ──────────────────────────────────────

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void BrushSize_AcceptsCanonicalRange(int size)
        {
            _state.BrushSize = size;
            Assert.AreEqual(size, _state.BrushSize);
        }

        [Test]
        public void BrushStrokeCells_AddIsDeduplicated()
        {
            // Mirrors the brush behaviour where the same cell can be hovered multiple times
            // during a single drag — the set must not record duplicates.
            var cell = new Vector3Int(3, 4, 0);
            _state.BrushStrokeCells.Add(cell);
            _state.BrushStrokeCells.Add(cell);
            _state.BrushStrokeCells.Add(cell);
            Assert.AreEqual(1, _state.BrushStrokeCells.Count);
        }

        [Test]
        public void BrushStrokeCells_ClearAfterEndStrokeReleasesMemory()
        {
            for (int x = 0; x < 50; x++)
                _state.BrushStrokeCells.Add(new Vector3Int(x, 0, 0));
            Assert.AreEqual(50, _state.BrushStrokeCells.Count);

            _state.BrushStrokeCells.Clear();
            Assert.AreEqual(0, _state.BrushStrokeCells.Count);
        }

        // ── Selection cell ───────────────────────────────────────────────

        [Test]
        public void SelectedCellPos_IsNullableAndRoundTrips()
        {
            Assert.IsFalse(_state.SelectedCellPos.HasValue);

            var picked = new Vector3Int(7, -3, 0);
            _state.SelectedCellPos = picked;
            Assert.IsTrue(_state.SelectedCellPos.HasValue);
            Assert.AreEqual(picked, _state.SelectedCellPos.Value);

            _state.SelectedCellPos = null;
            Assert.IsFalse(_state.SelectedCellPos.HasValue, "Resetting to null must clear the selection.");
        }

        // ── Catalog selection ────────────────────────────────────────────

        [Test]
        public void SelectedCatalog_IndexAndCategoryUpdateTogether()
        {
            _state.SelectedCategory = "rock_grass";
            _state.SelectedCatalogIndex = 12;
            Assert.AreEqual("rock_grass", _state.SelectedCategory);
            Assert.AreEqual(12, _state.SelectedCatalogIndex);

            // Resetting index is the documented sentinel.
            _state.SelectedCatalogIndex = -1;
            Assert.AreEqual(-1, _state.SelectedCatalogIndex);
        }

        [Test]
        public void SelectedTile_AcceptsAndReleasesReferences()
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            try
            {
                _state.SelectedTile = tile;
                Assert.AreSame(tile, _state.SelectedTile);

                _state.SelectedTile = null;
                Assert.IsNull(_state.SelectedTile);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tile);
            }
        }

        // ── Constants ────────────────────────────────────────────────────

        [Test]
        public void MaxUndo_IsPositive()
        {
            // Hard guarantee — the undo system depends on this being > 0.
            Assert.Greater(TileEditorState.MAX_UNDO, 0);
        }
    }
}
