using NUnit.Framework;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.UI
{
    /// <summary>
    /// Covers <see cref="TileEditorUIBuilder.FormatMoveToLayerLabel"/>, the pure
    /// helper that produces the "Target: {idx}: {LayerName}" string echoed beneath
    /// the Move-To-Layer slider while the user drags. The helper is exercised on
    /// every value-changed tick of the slider, so a bug here is visible on every
    /// drag — but more importantly, the index → enum mapping pins the slider's
    /// 0..8 contract: indices outside that range are clamped to the valid
    /// <see cref="TilemapLayerSetup.TilemapLayer"/> range rather than crashing.
    /// </summary>
    [TestFixture]
    public class MoveToLayerUIHelperTests
    {
        [Test]
        public void Format_MinIndex_ProducesGroundLabel()
        {
            Assert.AreEqual($"Target: 0: {TilemapLayerSetup.TilemapLayer.Ground}",
                TileEditorUIBuilder.FormatMoveToLayerLabel(0));
        }

        [Test]
        public void Format_MaxIndex_ProducesOverheadDetailsLabel()
        {
            Assert.AreEqual($"Target: 8: {TilemapLayerSetup.TilemapLayer.OverheadDetails}",
                TileEditorUIBuilder.FormatMoveToLayerLabel(8));
        }

        /// <summary>
        /// Every legal index 0..8 must resolve to a valid <see cref="TilemapLayerSetup.TilemapLayer"/>
        /// name. Catches the day someone reorders or renumbers the enum without
        /// updating the slider's range constant in the builder.
        /// </summary>
        [Test]
        public void Format_AllValidIndices_RoundTripThroughEnumName()
        {
            for (int i = 0; i <= 8; i++)
            {
                var expectedLayer = (TilemapLayerSetup.TilemapLayer)i;
                string expected = $"Target: {i}: {expectedLayer}";
                Assert.AreEqual(expected, TileEditorUIBuilder.FormatMoveToLayerLabel(i),
                    $"Format must reflect the canonical TilemapLayer enum entry at index {i}.");
            }
        }

        /// <summary>
        /// Negative indices (impossible from the slider but possible from a
        /// future hotkey path) must clamp to 0 (Ground) rather than producing a
        /// garbage cast or throwing.
        /// </summary>
        [TestCase(-1)]
        [TestCase(-100)]
        public void Format_NegativeIndex_ClampsToGround(int value)
        {
            Assert.AreEqual($"Target: 0: {TilemapLayerSetup.TilemapLayer.Ground}",
                TileEditorUIBuilder.FormatMoveToLayerLabel(value));
        }

        /// <summary>
        /// Indices above the enum's max must clamp to 8 (OverheadDetails). Pinned
        /// so a regression that lets the slider expose layer 9 (or higher) would
        /// be loud — the label would still read OverheadDetails but the action
        /// would be a no-op (silent failure for the user).
        /// </summary>
        [TestCase(9)]
        [TestCase(100)]
        public void Format_OverflowIndex_ClampsToOverheadDetails(int value)
        {
            Assert.AreEqual($"Target: 8: {TilemapLayerSetup.TilemapLayer.OverheadDetails}",
                TileEditorUIBuilder.FormatMoveToLayerLabel(value));
        }
    }
}
