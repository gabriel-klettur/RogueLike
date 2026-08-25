using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Tools
{
    /// <summary>
    /// Two gaps in one file, both about <see cref="TileEditorConstants.MaxBrushSize"/> (25):
    ///
    /// 1. <c>TileEditorManager.OnBrushSizeChanged</c> is the ONLY place in the Tile
    ///    Editor that clamps a brush size — <see cref="TileEditorState.BrushSize"/>
    ///    itself is intentionally unclamped at the state level (see
    ///    <c>TileEditorIntegrationTests.Integration_Limits_WorkCorrectly</c>, whose own
    ///    comment says "UI clamps separately"). Nothing in the suite had ever invoked
    ///    <c>OnBrushSizeChanged</c> itself before this file, so the clamp that actually
    ///    protects the game from an unbounded brush had zero coverage.
    ///
    /// 2. <see cref="TileBrush.Paint"/> / <see cref="TileBrush.Erase"/> footprint at the
    ///    two extremes of the valid range. The largest brush size exercised anywhere
    ///    else in the suite (<c>TileBrushExhaustiveTests</c>) is 4 — size 1 (min) and
    ///    25 (<see cref="TileEditorConstants.MaxBrushSize"/>) had never been painted.
    /// </summary>
    [TestFixture]
    public class TileEditorBrushSizeClampTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private GameObject _standaloneGrid;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            if (_standaloneGrid != null) Object.DestroyImmediate(_standaloneGrid);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Harness: manager + fully initialized UI. Required because
        // OnBrushSizeChanged calls `_ui.RefreshBrushSizeLabel()` unconditionally (no
        // `?.`) — a bare manager would NRE. Duplicated from
        // TileEditorLifecycleTests.NewManagerWithUI per project convention (small test
        // harnesses are copied per file, not shared). ──
        private (TileEditorManager manager, TileEditorUI ui) NewManagerWithUI()
        {
            LogAssert.ignoreFailingMessages = true; // TMP/Canvas init noise — see skill gotcha #4

            var managerGo = new GameObject("BrushSizeClampTests_Manager");
            _scene.Add(managerGo);
            var manager = managerGo.AddComponent<TileEditorManager>();

            var uiGo = new GameObject("BrushSizeClampTests_UI");
            uiGo.transform.SetParent(managerGo.transform);
            _scene.Add(uiGo);
            var ui = uiGo.AddComponent<TileEditorUI>();
            ui.Initialize(manager.State, catalog: null,
                onTileSelected: null, onToolChanged: null,
                onLayerChanged: null, onBrushSizeChanged: null);

            SetField(manager, "_ui", ui);
            return (manager, ui);
        }

        private static void SetField(object obj, string name, object value)
        {
            var f = typeof(TileEditorManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"Reflection: field '{name}' not found on TileEditorManager.");
            f.SetValue(obj, value);
        }

        private static TileEditorUIBuilder.UIRefs GetRefs(TileEditorUI ui)
        {
            var f = typeof(TileEditorUI).GetField("_refs", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "Reflection: '_refs' not found on TileEditorUI.");
            return (TileEditorUIBuilder.UIRefs)f.GetValue(ui);
        }

        private static void CallOnBrushSizeChanged(TileEditorManager manager, int newSize)
        {
            var mi = typeof(TileEditorManager).GetMethod("OnBrushSizeChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(mi, "Reflection: 'OnBrushSizeChanged' not found on TileEditorManager.");
            mi.Invoke(manager, new object[] { newSize });
        }

        // ════════════════════════════════════════════════════════════════════
        // OnBrushSizeChanged clamp
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OnBrushSizeChanged_Zero_ClampsToMinBrushSize()
        {
            var (manager, _) = NewManagerWithUI();
            CallOnBrushSizeChanged(manager, 0);
            Assert.AreEqual(TileEditorConstants.MinBrushSize, manager.State.BrushSize);
        }

        [Test]
        public void OnBrushSizeChanged_Negative_ClampsToMinBrushSize()
        {
            var (manager, _) = NewManagerWithUI();
            CallOnBrushSizeChanged(manager, -5);
            Assert.AreEqual(TileEditorConstants.MinBrushSize, manager.State.BrushSize);
        }

        [Test]
        public void OnBrushSizeChanged_OneAboveMax_ClampsToMaxBrushSize()
        {
            var (manager, _) = NewManagerWithUI();
            CallOnBrushSizeChanged(manager, TileEditorConstants.MaxBrushSize + 1);
            Assert.AreEqual(TileEditorConstants.MaxBrushSize, manager.State.BrushSize);
        }

        [Test]
        public void OnBrushSizeChanged_FarAboveMax_StillClampsToMaxBrushSize()
        {
            var (manager, _) = NewManagerWithUI();
            CallOnBrushSizeChanged(manager, 1000);
            Assert.AreEqual(TileEditorConstants.MaxBrushSize, manager.State.BrushSize,
                "A regression that reopens the class of bug MaxBrushSize=25 exists to prevent " +
                "(an effectively unbounded brush) would show up here as an unclamped 1000.");
        }

        [Test]
        public void OnBrushSizeChanged_AtExactBoundaries_KeepsExactValue()
        {
            var (manager, _) = NewManagerWithUI();

            CallOnBrushSizeChanged(manager, TileEditorConstants.MinBrushSize);
            Assert.AreEqual(TileEditorConstants.MinBrushSize, manager.State.BrushSize);

            CallOnBrushSizeChanged(manager, TileEditorConstants.MaxBrushSize);
            Assert.AreEqual(TileEditorConstants.MaxBrushSize, manager.State.BrushSize);
        }

        [Test]
        public void OnBrushSizeChanged_RefreshesBrushSizeLabelText()
        {
            var (manager, ui) = NewManagerWithUI();
            var refs = GetRefs(ui);
            Assert.IsNotNull(refs.BrushSizeLabel, "Sanity: BuildAll must populate BrushSizeLabel.");

            CallOnBrushSizeChanged(manager, 7);

            Assert.AreEqual("7x7", refs.BrushSizeLabel.text,
                "OnBrushSizeChanged must repaint the label, not just mutate state silently.");
        }

        // ════════════════════════════════════════════════════════════════════
        // TileBrush.Paint / Erase footprint at the two range extremes.
        // ════════════════════════════════════════════════════════════════════

        private Tilemap NewStandaloneTilemap()
        {
            _standaloneGrid = new GameObject("BrushSizeClampTests_Grid");
            _standaloneGrid.AddComponent<Grid>();
            var tmGo = new GameObject("BrushSizeClampTests_Tilemap");
            tmGo.transform.SetParent(_standaloneGrid.transform, false);
            return tmGo.AddComponent<Tilemap>();
        }

        private static Tile MakeTile(string name)
        {
            var tex = new Texture2D(2, 2);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 1f);
            sprite.name = name;
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.name = name;
            return tile;
        }

        [Test]
        public void Paint_BrushSizeOne_AffectsExactlyTheAnchorCell()
        {
            var tilemap = NewStandaloneTilemap();
            var tile = MakeTile("size1");
            var anchor = new Vector3Int(5, 5, 0);

            var edits = TileBrush.Paint(tilemap, anchor, tile, brushSize: TileEditorConstants.MinBrushSize);

            Assert.AreEqual(1, edits.Count, "Minimum brush size must paint exactly one cell.");
            Assert.AreEqual(anchor, edits[0].Position);
            Assert.AreEqual(tile, tilemap.GetTile(anchor));
        }

        [Test]
        public void Paint_BrushSizeMax_AffectsExactly625Cells_ExtendingRightAndDown()
        {
            var tilemap = NewStandaloneTilemap();
            var tile = MakeTile("size25");
            var anchor = new Vector3Int(10, 10, 0);
            int size = TileEditorConstants.MaxBrushSize; // 25

            var edits = TileBrush.Paint(tilemap, anchor, tile, brushSize: size);

            Assert.AreEqual(size * size, edits.Count,
                $"Brush size {size} must paint exactly {size * size} cells (a {size}x{size} square).");

            // Cursor is the TOP-LEFT of the footprint: extends right (+x) and down (-y),
            // per TileBrush.Paint's own doc-comment.
            Assert.AreEqual(tile, tilemap.GetTile(anchor), "Top-left (anchor) must be painted.");
            Assert.AreEqual(tile, tilemap.GetTile(anchor + new Vector3Int(size - 1, 0, 0)), "Top-right corner must be painted.");
            Assert.AreEqual(tile, tilemap.GetTile(anchor + new Vector3Int(0, -(size - 1), 0)), "Bottom-left corner must be painted.");
            Assert.AreEqual(tile, tilemap.GetTile(anchor + new Vector3Int(size - 1, -(size - 1), 0)), "Bottom-right corner must be painted.");

            // One cell beyond each edge must stay untouched — proves the footprint doesn't overshoot.
            Assert.IsNull(tilemap.GetTile(anchor + new Vector3Int(size, 0, 0)), "One cell past the right edge must stay empty.");
            Assert.IsNull(tilemap.GetTile(anchor + new Vector3Int(0, -size, 0)), "One cell past the bottom edge must stay empty.");
        }

        [Test]
        public void Erase_BrushSizeMax_ClearsExactly625PrefilledCells_AndStopsAtTheEdge()
        {
            var tilemap = NewStandaloneTilemap();
            var tile = MakeTile("prefilled");
            var anchor = new Vector3Int(0, 0, 0);
            int size = TileEditorConstants.MaxBrushSize;

            // Pre-fill a (size+1) x (size+1) block so the erase footprint has real tiles
            // to remove everywhere inside it, plus one extra ring cell just outside the
            // erase footprint on each axis to prove erase doesn't overshoot.
            for (int dy = 0; dy <= size; dy++)
                for (int dx = 0; dx <= size; dx++)
                    tilemap.SetTile(new Vector3Int(anchor.x + dx, anchor.y - dy, 0), tile);

            var edits = TileBrush.Erase(tilemap, anchor, brushSize: size);

            Assert.AreEqual(size * size, edits.Count, $"Erase at size {size} must clear exactly {size * size} cells.");
            Assert.IsNull(tilemap.GetTile(anchor), "Anchor cell must be erased.");
            Assert.IsNull(tilemap.GetTile(anchor + new Vector3Int(size - 1, -(size - 1), 0)), "Bottom-right corner of the footprint must be erased.");

            // The ring cell one step past the erase footprint was pre-filled but lies
            // OUTSIDE the 25x25 erase box — must survive untouched.
            Assert.AreEqual(tile, tilemap.GetTile(anchor + new Vector3Int(size, 0, 0)), "One cell past the right edge must be untouched by erase.");
            Assert.AreEqual(tile, tilemap.GetTile(anchor + new Vector3Int(0, -size, 0)), "One cell past the bottom edge must be untouched by erase.");
        }
    }
}
