using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.Editors;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.Editors.General;
using Valkur.Gameplay.Editors.SeedWorld;

namespace Valkur.Tests.EditMode.Editors.SeedWorld
{
    /// <summary>
    /// The Seed World editor's contract. uGUI performs no layout in Edit Mode, so these assert
    /// STRUCTURE and model state — never a size.
    /// </summary>
    public class SeedWorldEditorTests
    {
        private GameObject _go;
        private SeedWorldRuntimeEditor _editor;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("SeedWorldEditorTest");
            _editor = _go.AddComponent<SeedWorldRuntimeEditor>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_editor != null && _editor.IsActive) _editor.Deactivate();
            // Object.Destroy is an ERROR in Edit Mode.
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void TheEditor_IsInTheGeneralEditorLauncher()
        {
            Assert.AreEqual("Seed World", _editor.EditorName);
            Assert.IsTrue(GeneralEditorRegistry.BuildEntries().Any(e => e.Label == "Seed World"),
                "With the F-row retired the launcher is the only way in.");
        }

        [Test]
        public void Activate_BuildsBothPanels_AndPaintsThePreview()
        {
            _editor.Activate();

            Assert.IsTrue(_editor.IsActive);
            Assert.IsNotNull(_editor.Map, "Opening the editor must generate a preview.");
            var tex = _editor.PreviewTexture;
            Assert.IsNotNull(tex);
            Assert.AreEqual(_editor.Map.Columns, tex.width);
            Assert.AreEqual(_editor.Map.Rows, tex.height);
            Assert.AreEqual(FilterMode.Point, tex.filterMode, "A bilinear preview blurs biome borders into colours no biome has.");

            var raw = _go.GetComponentsInChildren<RawImage>(true).FirstOrDefault(r => r.name == "PreviewImage");
            Assert.IsNotNull(raw);
            Assert.AreSame(tex, raw.texture);
        }

        /// <summary>The texture shows the map it was generated from, pixel for pixel, on the biome layer.</summary>
        [Test]
        public void TheBiomeLayer_PaintsEachCellInItsBiomeColour()
        {
            _editor.Activate();
            var map = _editor.Map;
            var pixels = _editor.PreviewTexture.GetPixels32();

            int checkedCells = 0;
            for (int row = 0; row < map.Rows; row += 11)
                for (int col = 0; col < map.Columns; col += 11)
                {
                    int i = map.Index(col, row);
                    if (map.TownMask[i] != 0) continue; // towns are drawn over the biome
                    var expected = WorldBiomeTable.GetAt(map.Biomes[i]).PreviewColor;
                    var actual = pixels[i];
                    if (actual.Equals(WorldGenPalette.Spawn)) continue; // the spawn marker
                    Assert.AreEqual(expected, actual, $"cell ({col},{row})");
                    checkedCells++;
                }
            Assert.Greater(checkedCells, 0);
        }

        [Test]
        public void AnEdit_Regenerates_AndUndoRestoresTheWorld()
        {
            _editor.Activate();
            var before = (byte[])_editor.Map.Biomes.Clone();
            int seedBefore = _editor.Settings.seed;

            _editor.SetSeedText("otra semilla");
            Assert.AreNotEqual(seedBefore, _editor.Settings.seed);
            Assert.AreEqual(WorldSeed.Hash("otra semilla"), _editor.Settings.seed);
            Assert.AreEqual(1, _editor.UndoDepth);
            CollectionAssert.AreNotEqual(before, _editor.Map.Biomes);

            _editor.Undo();
            Assert.AreEqual(seedBefore, _editor.Settings.seed);
            CollectionAssert.AreEqual(before, _editor.Map.Biomes, "Undo must bring back the same world, not a similar one.");
            Assert.AreEqual(1, _editor.RedoDepth);

            _editor.Redo();
            Assert.AreEqual(WorldSeed.Hash("otra semilla"), _editor.Settings.seed);
        }

        [Test]
        public void AnEditThatChangesNothing_IsNotAnUndoStep()
        {
            _editor.Activate();
            _editor.SetSeedText(_editor.Settings.seed.ToString());
            Assert.AreEqual(0, _editor.UndoDepth);
        }

        [Test]
        public void DisablingABiome_RemovesItFromThePreview()
        {
            _editor.Activate();
            _editor.SetBiomeEnabled(WorldBiome.Plains, false);
            Assert.AreEqual(0, _editor.Map.BiomeCounts[(int)WorldBiome.Plains]);
        }

        [Test]
        public void TheWorkspace_RoundTripsTheSettings_AndTheView()
        {
            _editor.Activate();
            _editor.SetSeedText("12345");
            _editor.SetBiomeEnabled(WorldBiome.Desert, false);
            _editor.SetLayer(SeedWorldRuntimeEditor.PreviewLayer.Humidity);
            _editor.SetTab(SeedWorldRuntimeEditor.Tab.Climate);

            var ws = new EditorWorkspace { editorName = _editor.EditorName };
            _editor.CaptureWorkspace(ws);

            var otherGo = new GameObject("SeedWorldEditorTest2");
            try
            {
                var other = otherGo.AddComponent<SeedWorldRuntimeEditor>();
                other.RestoreWorkspace(ws);
                Assert.AreEqual(12345, other.Settings.seed);
                Assert.IsFalse(other.Settings.WeightOf(WorldBiome.Desert).enabled);
                Assert.AreEqual(SeedWorldRuntimeEditor.PreviewLayer.Humidity, other.ActiveLayer);
                Assert.AreEqual(SeedWorldRuntimeEditor.Tab.Climate, other.ActiveTab);
            }
            finally { Object.DestroyImmediate(otherGo); }
        }

        [Test]
        public void EveryTab_Builds()
        {
            _editor.Activate();
            foreach (SeedWorldRuntimeEditor.Tab tab in System.Enum.GetValues(typeof(SeedWorldRuntimeEditor.Tab)))
            {
                _editor.SetTab(tab);
                Assert.AreEqual(tab, _editor.ActiveTab);
            }
            foreach (SeedWorldRuntimeEditor.PreviewLayer layer in System.Enum.GetValues(typeof(SeedWorldRuntimeEditor.PreviewLayer)))
            {
                _editor.SetLayer(layer);
                Assert.AreEqual(layer, _editor.ActiveLayer);
            }
        }
    }
}
