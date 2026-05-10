using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Inspector
{
    /// <summary>
    /// Regression coverage for the Tile Editor Inspector's "Hover Layer" field.
    ///
    /// <para>The bug: <c>UpdateViewPanelHover</c> previously reported
    /// <c>_state.CurrentLayer</c> (the active editing layer) regardless of which
    /// layer actually held the tile under the cursor. Visiting an empty cell on the
    /// active layer (while a deeper layer had the hovered tile) showed an empty or
    /// misleading layer label.</para>
    ///
    /// <para>The fix factored the layer scan into
    /// <see cref="TileEditorManager.TryFindHoveredVisibleLayer"/>, which iterates
    /// layers from the topmost (OverheadDetails = 8) down to Ground (0), skipping
    /// (a) the Collision layer (alpha-zero authoring metadata) and (b) any layer
    /// the user has hidden via the Layers panel. These tests pin that contract
    /// directly so the bug cannot quietly come back via either the visibility check
    /// or the iteration order.</para>
    /// </summary>
    [TestFixture]
    public class InspectorHoverLayerTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. Empty cell across every layer → false
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void EmptyCell_ReturnsFalse_AndOutLayerIsDefault()
        {
            var manager = NewManagerWithGridAndUI();

            bool hit = TryFindHoveredVisibleLayer(manager, new Vector3Int(0, 0, 0),
                out var layer, out var tile);

            Assert.IsFalse(hit, "No tile placed anywhere — must report 'no hover'.");
            Assert.IsNull(tile,
                "Out tile must be null when nothing is hovered.");
            Assert.AreEqual(default(TilemapLayerSetup.TilemapLayer), layer,
                "Out layer must be the enum default when nothing is hovered.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. Single tile at known cell → reports the layer that holds it
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void SingleTileOnGround_ReportsGround()
        {
            var manager = NewManagerWithGridAndUI();
            var cell = new Vector3Int(3, 4, 0);
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.Ground, cell, "ground_tile");

            bool hit = TryFindHoveredVisibleLayer(manager, cell, out var layer, out var tile);

            Assert.IsTrue(hit);
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.Ground, layer);
            Assert.AreEqual("ground_tile", tile.name);
        }

        [Test]
        public void SingleTileOnOverheadDetails_ReportsOverheadDetails()
        {
            var manager = NewManagerWithGridAndUI();
            var cell = new Vector3Int(0, 0, 0);
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.OverheadDetails, cell, "overhead");

            bool hit = TryFindHoveredVisibleLayer(manager, cell, out var layer, out var tile);

            Assert.IsTrue(hit);
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.OverheadDetails, layer);
            Assert.AreEqual("overhead", tile.name);
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. Topmost-wins (higher layer index drawn on top)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TilesOnGroundAndDecorations_ReportsDecorations()
        {
            var manager = NewManagerWithGridAndUI();
            var cell = new Vector3Int(1, 1, 0);
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.Ground,      cell, "ground");
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.Decorations, cell, "deco");

            bool hit = TryFindHoveredVisibleLayer(manager, cell, out var layer, out var tile);

            Assert.IsTrue(hit);
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.Decorations, layer,
                "Decorations (5) is rendered above Ground (0) — must win.");
            Assert.AreEqual("deco", tile.name);
        }

        [Test]
        public void TilesAcrossManyLayers_ReportsHighestIndex()
        {
            var manager = NewManagerWithGridAndUI();
            var cell = new Vector3Int(2, 2, 0);
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.Ground,          cell, "g");
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.FloorDecals,     cell, "fd");
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.ObjectsLow,      cell, "ol");
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.WallsBottom,     cell, "wb");
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.Decorations,     cell, "d");
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.WallsTop,        cell, "wt");
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.ObjectsHigh,     cell, "oh");
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.OverheadDetails, cell, "od");

            bool hit = TryFindHoveredVisibleLayer(manager, cell, out var layer, out var tile);

            Assert.IsTrue(hit);
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.OverheadDetails, layer,
                "OverheadDetails has the highest layer index — must win regardless of " +
                "how many lower layers are populated.");
            Assert.AreEqual("od", tile.name);
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. Collision layer never reported (invisible authoring metadata)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TileOnCollisionOnly_ReturnsFalse()
        {
            var manager = NewManagerWithGridAndUI();
            var cell = new Vector3Int(5, 5, 0);
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.Collision, cell, "coll");

            bool hit = TryFindHoveredVisibleLayer(manager, cell, out _, out _);

            Assert.IsFalse(hit,
                "Collision tiles are alpha-zero authoring metadata — must never be " +
                "reported as the visually-hovered tile.");
        }

        [Test]
        public void TilesOnCollisionAndGround_ReportsGround_NotCollision()
        {
            var manager = NewManagerWithGridAndUI();
            var cell = new Vector3Int(6, 6, 0);
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.Collision, cell, "coll");
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.Ground,    cell, "ground");

            bool hit = TryFindHoveredVisibleLayer(manager, cell, out var layer, out var tile);

            Assert.IsTrue(hit);
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.Ground, layer,
                "Even though Collision (2) > Ground (0) by index, Collision is " +
                "skipped entirely — Ground must be reported.");
            Assert.AreEqual("ground", tile.name);
        }

        // ════════════════════════════════════════════════════════════════════
        // 5. Hidden layers are skipped
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void HoveredLayerHidden_ScanContinuesToLowerLayer()
        {
            var manager = NewManagerWithGridAndUI();
            var cell = new Vector3Int(7, 8, 0);
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.Ground,      cell, "ground");
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.Decorations, cell, "deco");

            // Hide Decorations — Ground should win the scan.
            SetLayerVisible(manager, TilemapLayerSetup.TilemapLayer.Decorations, false);

            bool hit = TryFindHoveredVisibleLayer(manager, cell, out var layer, out var tile);

            Assert.IsTrue(hit);
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.Ground, layer,
                "When the topmost layer is hidden the scan must fall through to the " +
                "next visible layer below.");
            Assert.AreEqual("ground", tile.name);
        }

        [Test]
        public void OnlyTileIsOnHiddenLayer_ReturnsFalse()
        {
            var manager = NewManagerWithGridAndUI();
            var cell = new Vector3Int(0, 0, 0);
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.WallsTop, cell, "wall");
            SetLayerVisible(manager, TilemapLayerSetup.TilemapLayer.WallsTop, false);

            bool hit = TryFindHoveredVisibleLayer(manager, cell, out _, out _);

            Assert.IsFalse(hit,
                "If the only layer with a tile at this cell is hidden, the inspector " +
                "must report 'no hover' — coherent with what the user actually sees.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 6. Active editing layer does not bias the result
        //     (this is the exact regression — old code always reported CurrentLayer)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void CurrentLayerEmpty_ButTileOnAnotherLayer_StillReportsThatLayer()
        {
            var manager = NewManagerWithGridAndUI();
            manager.State.CurrentLayer = TilemapLayerSetup.TilemapLayer.FloorDecals;
            var cell = new Vector3Int(2, 3, 0);
            // FloorDecals (the active layer) is intentionally empty at this cell;
            // the user is hovering a tile that lives on Ground.
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.Ground, cell, "ground");

            bool hit = TryFindHoveredVisibleLayer(manager, cell, out var layer, out var tile);

            Assert.IsTrue(hit, "Tile exists on Ground — must be hit even though the " +
                               "active editing layer (FloorDecals) is empty here.");
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.Ground, layer,
                "Pre-fix, CurrentLayer was reported regardless of what actually sits " +
                "under the cursor. This test guards against that exact regression.");
            Assert.AreEqual("ground", tile.name);
        }

        [Test]
        public void CurrentLayerHasTile_ButHigherLayerAlsoHasOne_HigherWins()
        {
            var manager = NewManagerWithGridAndUI();
            manager.State.CurrentLayer = TilemapLayerSetup.TilemapLayer.Ground;
            var cell = new Vector3Int(1, 0, 0);
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.Ground,    cell, "g");
            PlaceTile(manager, TilemapLayerSetup.TilemapLayer.WallsTop,  cell, "wt");

            bool hit = TryFindHoveredVisibleLayer(manager, cell, out var layer, out _);

            Assert.IsTrue(hit);
            Assert.AreEqual(TilemapLayerSetup.TilemapLayer.WallsTop, layer,
                "WallsTop (6) renders above Ground (0); even when Ground is the " +
                "active editing layer, the topmost rendered tile must win.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 7. Wiring guards (unwired manager must not throw)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void NoUiWired_ReturnsFalse_DoesNotThrow()
        {
            var manager = NewManager();
            // No grid, no UI — this can happen during init/teardown races.
            Assert.DoesNotThrow(() =>
            {
                bool hit = TryFindHoveredVisibleLayer(manager, Vector3Int.zero, out _, out _);
                Assert.IsFalse(hit);
            });
        }

        [Test]
        public void NoWorldGridBuilder_ReturnsFalse_DoesNotThrow()
        {
            var manager = NewManager();
            AttachUI(manager, allVisible: true);
            // worldGridBuilder is never assigned.
            Assert.DoesNotThrow(() =>
            {
                bool hit = TryFindHoveredVisibleLayer(manager, Vector3Int.zero, out _, out _);
                Assert.IsFalse(hit);
            });
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════

        private TileEditorManager NewManager()
        {
            _host = new GameObject("TileEditorManager_TestHost");
            return _host.AddComponent<TileEditorManager>();
        }

        private TileEditorManager NewManagerWithGridAndUI()
        {
            var manager = NewManager();
            AttachWorldGrid(manager);
            AttachUI(manager, allVisible: true);
            return manager;
        }

        /// <summary>
        /// Builds a Grid + 9 child Tilemaps (one per layer) and wires both the
        /// manager's <c>worldGridBuilder</c> field and the builder's private
        /// <c>_grid</c> field. Mirrors the pattern in
        /// <c>TileEditorSelectModeTests.AttachWorldGrid</c>.
        /// </summary>
        private static void AttachWorldGrid(TileEditorManager manager)
        {
            var gridGo = new GameObject("WorldGrid");
            gridGo.transform.SetParent(manager.transform, false);
            var grid = gridGo.AddComponent<Grid>();
            var wgb = gridGo.AddComponent<WorldGridBuilder>();

            for (int i = 0; i < 9; i++)
            {
                var layer = (TilemapLayerSetup.TilemapLayer)i;
                var tmGo = new GameObject(layer.ToString());
                tmGo.transform.SetParent(gridGo.transform, false);
                tmGo.AddComponent<Tilemap>();
                tmGo.AddComponent<TilemapRenderer>();
            }

            typeof(TileEditorManager)
                .GetField("worldGridBuilder", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, wgb);
            typeof(WorldGridBuilder)
                .GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(wgb, grid);
        }

        /// <summary>
        /// Attaches a <see cref="TileEditorUI"/> to the manager via reflection and
        /// initializes the private <c>_layerVisibility</c> array (defaults to all-false
        /// without a real <c>Initialize()</c> call, which would build the full canvas).
        /// </summary>
        private static TileEditorUI AttachUI(TileEditorManager manager, bool allVisible)
        {
            var uiGo = new GameObject("TileEditorUI_Test");
            uiGo.transform.SetParent(manager.transform, false);
            var ui = uiGo.AddComponent<TileEditorUI>();

            typeof(TileEditorManager)
                .GetField("_ui", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, ui);

            var arr = (bool[])typeof(TileEditorUI)
                .GetField("_layerVisibility", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(ui);
            for (int i = 0; i < arr.Length; i++) arr[i] = allVisible;
            return ui;
        }

        private static void SetLayerVisible(TileEditorManager manager,
            TilemapLayerSetup.TilemapLayer layer, bool visible)
        {
            var ui = (TileEditorUI)typeof(TileEditorManager)
                .GetField("_ui", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(manager);
            var arr = (bool[])typeof(TileEditorUI)
                .GetField("_layerVisibility", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(ui);
            arr[(int)layer] = visible;
        }

        private static void PlaceTile(TileEditorManager manager,
            TilemapLayerSetup.TilemapLayer layer, Vector3Int cell, string tileName)
        {
            var wgb = (WorldGridBuilder)typeof(TileEditorManager)
                .GetField("worldGridBuilder", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(manager);
            var tm = wgb.GetTilemap(layer);
            Assert.IsNotNull(tm, $"Test fixture is missing the {layer} tilemap.");

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = tileName;
            tm.SetTile(cell, tile);
        }

        private static bool TryFindHoveredVisibleLayer(TileEditorManager manager,
            Vector3Int cell, out TilemapLayerSetup.TilemapLayer layer, out TileBase tile)
        {
            var mi = typeof(TileEditorManager).GetMethod("TryFindHoveredVisibleLayer",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(mi, "Reflection: TryFindHoveredVisibleLayer not found — has it been renamed?");

            var args = new object[] { cell, default(TilemapLayerSetup.TilemapLayer), null };
            bool hit = (bool)mi.Invoke(manager, args);
            layer = (TilemapLayerSetup.TilemapLayer)args[1];
            tile  = (TileBase)args[2];
            return hit;
        }
    }
}
