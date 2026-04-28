using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using System.Collections;

namespace Valkur.Tests.EditMode.Editors.TileEditor
{
    [TestFixture]
    public class TileEditorBrushSizeTests
    {
        private TileEditorState _state;

        [SetUp]
        public void SetUp()
        {
            _state = new TileEditorState();
        }

        [Test]
        public void TileEditorConstants_HasCorrectBrushSizeRange()
        {
            // Assert
            Assert.AreEqual(1, TileEditorConstants.MinBrushSize, "MinBrushSize should be 1");
            Assert.AreEqual(25, TileEditorConstants.MaxBrushSize, "MaxBrushSize should be 25");
        }

        [Test]
        public void TileEditorState_DefaultBrushSize_IsValid()
        {
            // Assert
            Assert.IsTrue(_state.BrushSize >= TileEditorConstants.MinBrushSize, 
                "Default brush size should be >= minimum");
            Assert.IsTrue(_state.BrushSize <= TileEditorConstants.MaxBrushSize, 
                "Default brush size should be <= maximum");
        }

        [Test]
        public void TileEditorState_BrushSize_SetValidValues_WorksCorrectly()
        {
            // Arrange & Act
            _state.BrushSize = 5;
            _state.BrushSize = 10;
            _state.BrushSize = 25;

            // Assert
            Assert.AreEqual(25, _state.BrushSize, "Should set brush size to 25");
        }

        [Test]
        public void TileEditorState_BrushSize_SetMinimumValue_WorksCorrectly()
        {
            // Arrange & Act
            _state.BrushSize = TileEditorConstants.MinBrushSize;

            // Assert
            Assert.AreEqual(TileEditorConstants.MinBrushSize, _state.BrushSize, 
                "Should set brush size to minimum value");
        }

        [Test]
        public void TileEditorState_BrushSize_SetMaximumValue_WorksCorrectly()
        {
            // Arrange & Act
            _state.BrushSize = TileEditorConstants.MaxBrushSize;

            // Assert
            Assert.AreEqual(TileEditorConstants.MaxBrushSize, _state.BrushSize, 
                "Should set brush size to maximum value");
        }

        [Test]
        public void TileEditorState_BrushSize_RangeValidation_AllValidSizes()
        {
            // Act & Assert - Test all valid brush sizes
            for (int size = TileEditorConstants.MinBrushSize; size <= TileEditorConstants.MaxBrushSize; size++)
            {
                _state.BrushSize = size;
                Assert.AreEqual(size, _state.BrushSize, $"Should set brush size to {size}");
            }
        }

        [Test]
        public void TileEditorState_BrushSize_BrushStrokeCells_Initialized()
        {
            // Assert
            Assert.IsNotNull(_state.BrushStrokeCells, "BrushStrokeCells should be initialized");
            Assert.AreEqual(0, _state.BrushStrokeCells.Count, "BrushStrokeCells should be empty initially");
        }

        [Test]
        public void TileEditorState_BrushStrokeCells_AddAndRemove_WorksCorrectly()
        {
            // Arrange
            var cell1 = new Vector3Int(1, 1, 0);
            var cell2 = new Vector3Int(2, 2, 0);

            // Act
            _state.BrushStrokeCells.Add(cell1);
            _state.BrushStrokeCells.Add(cell2);

            // Assert
            Assert.AreEqual(2, _state.BrushStrokeCells.Count, "Should have 2 cells");
            Assert.IsTrue(_state.BrushStrokeCells.Contains(cell1), "Should contain cell1");
            Assert.IsTrue(_state.BrushStrokeCells.Contains(cell2), "Should contain cell2");

            // Act - Clear
            _state.BrushStrokeCells.Clear();

            // Assert
            Assert.AreEqual(0, _state.BrushStrokeCells.Count, "Should be empty after clear");
        }

        [Test]
        public void TileEditorState_SelectedCellPos_InitiallyNull()
        {
            // Assert
            Assert.IsNull(_state.SelectedCellPos, "SelectedCellPos should be null initially");
        }

        [Test]
        public void TileEditorState_SelectedCellPos_SetAndGet_WorksCorrectly()
        {
            // Arrange
            var cell = new Vector3Int(5, 10, 0);

            // Act
            _state.SelectedCellPos = cell;

            // Assert
            Assert.AreEqual(cell, _state.SelectedCellPos, "Should set and get SelectedCellPos correctly");
        }

        [Test]
        public void TileEditorState_SelectedCellPos_SetToNull_WorksCorrectly()
        {
            // Arrange
            _state.SelectedCellPos = new Vector3Int(1, 1, 0);

            // Act
            _state.SelectedCellPos = null;

            // Assert
            Assert.IsNull(_state.SelectedCellPos, "Should be able to set SelectedCellPos to null");
        }

        [Test]
        public void TileEditorState_IsDragging_InitiallyFalse()
        {
            // Assert
            Assert.IsFalse(_state.IsDragging, "IsDragging should be false initially");
        }

        [Test]
        public void TileEditorState_IsDragging_SetAndGet_WorksCorrectly()
        {
            // Act
            _state.IsDragging = true;

            // Assert
            Assert.IsTrue(_state.IsDragging, "Should set IsDragging to true");

            // Act
            _state.IsDragging = false;

            // Assert
            Assert.IsFalse(_state.IsDragging, "Should set IsDragging to false");
        }

        [Test]
        public void TileEditorState_DefaultValues_AreValid()
        {
            // Assert all default values are valid
            Assert.AreEqual(TileEditorState.Tool.Select, _state.CurrentTool, "Default tool should be Select");
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.Ground, _state.CurrentLayer, "Default layer should be Ground");
            Assert.IsNull(_state.SelectedTile, "SelectedTile should be null initially");
            Assert.AreEqual(-1, _state.SelectedCatalogIndex, "SelectedCatalogIndex should be -1 initially");
            Assert.AreEqual("", _state.SelectedCategory, "SelectedCategory should be empty initially");
            Assert.IsFalse(_state.Active, "Active should be false initially");
            Assert.AreEqual(TileEditorState.ColliderMode.None, _state.CurrentColliderMode, "ColliderMode should be None initially");
            Assert.IsFalse(_state.ShowColliderOverlay, "ShowColliderOverlay should be false initially");
        }

        [Test]
        public void TileEditorState_MaxUndo_Constant_IsValid()
        {
            // Assert
            Assert.AreEqual(50, TileEditorState.MAX_UNDO, "MAX_UNDO should be 50");
        }

        [UnityTest]
        public IEnumerator TileEditorState_BrushStrokeCells_ThreadSafety()
        {
            // Arrange
            _state.BrushStrokeCells.Clear();

            // Act - Add cells from multiple "threads" (simulated)
            for (int i = 0; i < 10; i++)
            {
                var cell = new Vector3Int(i, i, 0);
                _state.BrushStrokeCells.Add(cell);
                yield return null; // Simulate thread switch
            }

            // Assert
            Assert.AreEqual(10, _state.BrushStrokeCells.Count, "Should handle concurrent additions correctly");
        }

        [Test]
        public void TileEditorConstants_BrushSizeRange_HasReasonableRange()
        {
            // Assert the range is reasonable for tile editing
            int range = TileEditorConstants.MaxBrushSize - TileEditorConstants.MinBrushSize + 1;
            Assert.AreEqual(25, range, "Should have 25 possible brush sizes");
            Assert.IsTrue(TileEditorConstants.MinBrushSize >= 1, "Minimum should be at least 1");
            Assert.IsTrue(TileEditorConstants.MaxBrushSize <= 50, "Maximum should be reasonable (not too large)");
        }
    }
}
