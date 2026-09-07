// The brush footprint, from the state all the way to the cells a stroke actually changes.
//
// Reported from the running editor, in this order: opening the Tile Editor showed "1x1" in the
// BRUSH SIZE panel while the on-screen cursor was two cells across; setting 2x2 and back to 1x1
// made the label agree again, and the stroke still painted 2x2. Three symptoms, one cause — the
// AUTO brush and the manual brush shared a single size, so restoring AutoBrushMode from the
// workspace resized the manual brush behind the author's back, and the label had been built
// before that happened.
//
// So the fix is structural rather than a resync: each mode carries its OWN footprint, the
// active one is whichever mode is being driven, and the label, the cursor and the paint call
// all read that same value. These tests walk that chain end to end, because the three used to
// disagree with each other and each disagreement looked like a separate bug.

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Brush
{
    [TestFixture]
    public class BrushSizePerModeTests
    {
        private const BindingFlags Instance =
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

        private readonly List<Object> _created = new List<Object>();
        private TileEditorManager _manager;

        [SetUp]
        public void SetUp()
        {
            var host = new GameObject(nameof(BrushSizePerModeTests));
            _created.Add(host);
            _manager = host.AddComponent<TileEditorManager>();
            _manager.State.CurrentTool = TileEditorState.Tool.Brush;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private void SetSize(int n) =>
            typeof(TileEditorManager).GetMethod("OnBrushSizeChanged", Instance).Invoke(_manager, new object[] { n });

        private bool Toggle() =>
            (bool)typeof(TileEditorManager).GetMethod("OnAutoBrushToggleClicked", Instance).Invoke(_manager, null);

        private Tilemap NewTilemap()
        {
            var gridGo = new GameObject("Grid");
            _created.Add(gridGo);
            gridGo.AddComponent<Grid>();
            var tilemapGo = new GameObject("Tilemap");
            tilemapGo.transform.SetParent(gridGo.transform);
            _created.Add(tilemapGo);
            return tilemapGo.AddComponent<Tilemap>();
        }

        private Tile NewTile()
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            _created.Add(tile);
            return tile;
        }

        // ── The two footprints are independent ───────────────────────────────────

        [Test]
        public void TheTwoModesKeepSeparateFootprints()
        {
            var state = _manager.State;
            state.AutoBrushMode = false;

            SetSize(5);
            Assert.AreEqual(5, state.BrushSize);
            Assert.AreEqual(5, state.ActiveBrushSize);

            Toggle();
            Assert.AreEqual(TileEditorConstants.AutoBrushSize, state.ActiveBrushSize,
                "AUTO brings its own footprint.");
            Assert.AreEqual(5, state.BrushSize,
                "and must not resize the manual brush — that is what made the manual brush look " +
                "like it had stopped responding to its own control.");

            SetSize(4);
            Assert.AreEqual(4, state.AutoBrushSize, "+/- moves the ACTIVE footprint");
            Assert.AreEqual(5, state.BrushSize, "never the other one");

            Toggle();
            Assert.AreEqual(5, state.ActiveBrushSize, "the manual brush comes back untouched");
            Toggle();
            Assert.AreEqual(4, state.ActiveBrushSize, "and AUTO's is remembered too");
        }

        [Test]
        public void AutoIsAModifierOnTheBrushToolAlone()
        {
            // The eraser, the collider paint and the layer-jump stamp all read BrushSize, so a
            // lit AUTO checkbox must not change the footprint they get.
            var state = _manager.State;
            state.AutoBrushMode = true;
            state.BrushSize = 5;
            state.AutoBrushSize = 2;

            state.CurrentTool = TileEditorState.Tool.Brush;
            Assert.IsTrue(state.IsAutoBrushActive);
            Assert.AreEqual(2, state.ActiveBrushSize);

            foreach (var tool in new[]
                     {
                         TileEditorState.Tool.Eraser, TileEditorState.Tool.Fill,
                         TileEditorState.Tool.Select, TileEditorState.Tool.Eyedropper,
                     })
            {
                state.CurrentTool = tool;
                Assert.IsFalse(state.IsAutoBrushActive, $"{tool} is not the AUTO brush");
                Assert.AreEqual(5, state.ActiveBrushSize, $"{tool} must use the manual footprint");
            }
        }

        [Test]
        public void AutoBrushSize_DefaultsToTwo_AsADefaultAndNotAnOverride()
        {
            Assert.AreEqual(2, TileEditorConstants.AutoBrushSize,
                "A corner is a vertex shared by four cells, so 2x2 is the smallest footprint " +
                "whose unit of action matches the model's unit of decision.");
            Assert.AreEqual(TileEditorConstants.AutoBrushSize, new TileEditorState().AutoBrushSize);

            // And it is a starting value the author may move, not a constant forced on them.
            _manager.State.AutoBrushMode = true;
            SetSize(6);
            Assert.AreEqual(6, _manager.State.AutoBrushSize);
            Assert.AreEqual(6, _manager.State.ActiveBrushSize);
        }

        // ── What the author aims at is what gets painted ─────────────────────────

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(4, false)]
        [TestCase(1, true)]
        [TestCase(3, true)]
        public void TheStrokeCoversExactlyTheActiveFootprint(int size, bool auto)
        {
            // The manual paint primitive is shared by both modes' footprint arithmetic, so this
            // is the chain from "the number on screen" to "the cells that changed".
            var state = _manager.State;
            state.AutoBrushMode = auto;
            SetSize(size);
            Assert.AreEqual(size, state.ActiveBrushSize);

            var tilemap = NewTilemap();
            var edits = TileBrush.Paint(tilemap, new Vector3Int(0, 0, 0), NewTile(), state.ActiveBrushSize, null);

            Assert.AreEqual(size * size, edits.Count,
                $"an {size}x{size} brush must change {size * size} cells");

            int painted = 0;
            for (int y = -size - 1; y <= size + 1; y++)
            for (int x = -size - 1; x <= size + 1; x++)
                if (tilemap.GetTile(new Vector3Int(x, y, 0)) != null) painted++;
            Assert.AreEqual(size * size, painted, "and nothing outside it");
        }

        [Test]
        public void TheStrokeIsAnchoredAtTheCursorCell_ExtendingRightAndDown()
        {
            // The cursor rect is centred with this same convention, so a mismatch here is a
            // brush that paints somewhere other than the square the author is looking at.
            _manager.State.AutoBrushMode = false;
            SetSize(2);

            var tilemap = NewTilemap();
            TileBrush.Paint(tilemap, new Vector3Int(0, 0, 0), NewTile(), _manager.State.ActiveBrushSize, null);

            foreach (var cell in new[]
                     {
                         new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
                         new Vector3Int(0, -1, 0), new Vector3Int(1, -1, 0),
                     })
                Assert.IsNotNull(tilemap.GetTile(cell), $"{cell} is inside the 2x2 footprint");

            foreach (var cell in new[]
                     {
                         new Vector3Int(-1, 0, 0), new Vector3Int(0, 1, 0),
                         new Vector3Int(2, 0, 0), new Vector3Int(0, -2, 0),
                     })
                Assert.IsNull(tilemap.GetTile(cell), $"{cell} is outside it");
        }

        // ── The controls cannot disagree with the tool ───────────────────────────

        [Test]
        public void TheAutoCheckbox_IsPaintedFromTheState_NotOnlyByItsOwnClick()
        {
            // What the author photographed: reopening with AUTO saved ON drew the checkbox in
            // its build-time OFF colours, because only the click closure ever repainted it. A
            // control that disagrees with the mode the editor is in makes every downstream
            // symptom look like a separate bug.
            var refresh = typeof(TileEditorUI).GetMethod("RefreshAutoToggle", Instance);
            Assert.IsNotNull(refresh,
                "TileEditorUI.RefreshAutoToggle is what lets the restore repaint the checkbox.");

            var refs = typeof(TileEditorUIBuilder).GetNestedType("UIRefs", Instance | BindingFlags.Static);
            Assert.IsNotNull(refs);
            Assert.IsNotNull(refs.GetField("AutoToggleBox"),
                "the checkbox must be reachable from outside its own click closure");
            Assert.IsNotNull(refs.GetField("AutoToggleLabel"));
        }

        [Test]
        public void RestoringTheWorkspace_RepaintsBothControls()
        {
            // RestoreBrush assigns the flag directly, so nothing else would tell the label or
            // the checkbox that the mode changed.
            string body = System.IO.File.ReadAllText(WorkspaceSource);

            int at = body.IndexOf("private void RestoreBrush", System.StringComparison.Ordinal);
            Assert.Greater(at, 0, "RestoreBrush must exist.");
            string restore = body.Substring(at, System.Math.Min(1800, body.Length - at));

            StringAssert.Contains("RefreshBrushSizeLabel", restore,
                "the number on screen must follow the footprint that was restored");
            StringAssert.Contains("RefreshAutoToggle", restore,
                "and the checkbox must follow the mode that was restored");
            StringAssert.DoesNotContain("OnBrushSizeChanged(ws.GetInt", restore,
                "Both footprints are restored as themselves. Routing them through " +
                "OnBrushSizeChanged writes to whichever is ACTIVE, so one would land on top of " +
                "the other depending on the order they arrive in.");
        }

        [Test]
        public void TheEditorAlwaysOpensWithAutoOff()
        {
            // AUTO changes what a click MEANS — it paints the pack's terrain and re-resolves a
            // whole neighbourhood instead of stamping the picked tile — so reopening straight
            // into it is how an author overwrites work they only meant to look at. Same
            // decision this editor already makes for its collider and layer-jump paint modes.
            Assert.IsFalse(new TileEditorState().AutoBrushMode,
                "A fresh state must not be in AUTO.");

            string body = System.IO.File.ReadAllText(WorkspaceSource);
            StringAssert.DoesNotContain("_state.AutoBrushMode = ws.", body,
                "Restoring the flag is what made the editor open in AUTO. The footprint is " +
                "remembered; the MODE is not.");
            StringAssert.DoesNotContain("SetBool(WS_AUTO_BRUSH,", body,
                "and it must not be written either, or a stale value outlives this decision.");
        }

        [Test]
        public void TheAutoFootprint_IsStillRemembered()
        {
            // Only the MODE resets. The size an author chose for AUTO is theirs.
            string body = System.IO.File.ReadAllText(WorkspaceSource);
            StringAssert.Contains("ws.SetInt(WS_AUTO_BRUSH_SIZE, _state.AutoBrushSize)", body);
            StringAssert.Contains("_state.AutoBrushSize = Mathf.Clamp(ws.GetInt(WS_AUTO_BRUSH_SIZE", body);
        }

        private static string WorkspaceSource => System.IO.Path.Combine(
            Application.dataPath, "_Project", "Scripts", "Gameplay", "Editors", "Tile",
            "TileEditorManager.Workspace.cs");

        [Test]
        public void ThePersistedFootprints_AreTheTwoOfThem_NotWhicheverWasActive()
        {
            string body = System.IO.File.ReadAllText(WorkspaceSource);

            StringAssert.Contains("ws.SetInt(WS_BRUSH_SIZE, _state.BrushSize)", body);
            StringAssert.Contains("ws.SetInt(WS_AUTO_BRUSH_SIZE, _state.AutoBrushSize)", body,
                "Saving only one number is what let a session's AUTO footprint overwrite the " +
                "author's manual one on disk, after which the manual brush was stuck for good.");
        }
    }
}
