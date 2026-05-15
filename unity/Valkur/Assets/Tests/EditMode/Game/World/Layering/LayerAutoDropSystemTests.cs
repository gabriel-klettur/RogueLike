using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.World.Layering
{
    /// <summary>
    /// State-machine coverage for <see cref="LayerAutoDropSystem"/> — the M1.9
    /// "gravity" rule that drops the player to a lower visual layer when they
    /// walk off an elevated tile. The fixture drives the cell-enter detector
    /// through <see cref="LayerAutoDropSystem.TestBind"/> + <see cref="TestStepToCell"/>
    /// so we don't need a full Player + InputSystem scene.
    ///
    /// Pinned invariants (matches plan §1.9):
    ///   • Underfoot strictly less than current layer → drop fires.
    ///   • Same cell twice → no re-fire (cell-enter once).
    ///   • Jump tile present at the cell → auto-drop yields to the jump system.
    ///   • Underfoot equal or higher than current → no-op.
    ///   • Underfoot -1 (void cell) → no-op (movement clamp prevents entry).
    ///   • A re-visit after leaving the cell does re-fire (no consumed state).
    /// </summary>
    [TestFixture]
    public class LayerAutoDropSystemTests
    {
        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _playerGo;
        private VisualLayerOccupant _occ;
        private LayerAutoDropSystem _sys;
        private LayerJumpMap _jumps;
        private Tile _tile;

        [SetUp]
        public void SetUp()
        {
            // Defensive: a previous fixture may have left the singleton alive.
            if (LayerAutoDropSystem.HasInstance)
                Object.DestroyImmediate(LayerAutoDropSystem.Instance.gameObject);

            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _playerGo = new GameObject("PlayerHost");
            _occ = _playerGo.AddComponent<VisualLayerOccupant>();

            _jumps = new LayerJumpMap();

            // Spawn the system manually so TestBind has something to call.
            var sysGo = new GameObject(nameof(LayerAutoDropSystem));
            _sys = sysGo.AddComponent<LayerAutoDropSystem>();
            _sys.TestBind(_occ, _grid, _jumps);

            _tile = ScriptableObject.CreateInstance<Tile>();
            _tile.name = "test_autodrop_tile";
            var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
            _tile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_sys != null) Object.DestroyImmediate(_sys.gameObject);
            Object.DestroyImmediate(_playerGo);
            Object.DestroyImmediate(_gridGo);
            Object.DestroyImmediate(_tile);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void PaintTile(TilemapLayerSetup.TilemapLayer layer, int x, int y)
        {
            _grid.GetTilemap(layer).SetTile(new Vector3Int(x, y, 0), _tile);
        }

        private void MovePlayerTo(int x, int y)
        {
            // Center of cell so the WorldToCell sample inside GetTopmostLayer
            // resolves unambiguously to (x, y).
            _playerGo.transform.position = new Vector3(x + 0.5f, y + 0.5f, 0f);
        }

        // ── Core drop semantics ──────────────────────────────────────────────

        [Test]
        public void StrictlyLowerUnderfoot_DropsToThatLayer()
        {
            _occ.SetVisualLayer(8);
            PaintTile(TilemapLayerSetup.TilemapLayer.Ground, 5, 5); // underfoot = 0

            MovePlayerTo(5, 5);
            bool fired = _sys.TestStepToCell(new Vector2Int(5, 5));

            Assert.IsTrue(fired, "Auto-drop must fire when underfoot (0) < current (8).");
            Assert.AreEqual(0, _occ.CurrentVisualLayer,
                "Player must snap directly to the underfoot layer (Decision 1).");
        }

        [Test]
        public void IntermediateUnderfoot_DropsDirectly_NotStepByStep()
        {
            // Decision 1: direct to underfoot — no layer-by-layer descent.
            _occ.SetVisualLayer(8);
            PaintTile(TilemapLayerSetup.TilemapLayer.WallsBottom, 5, 5); // underfoot = 4

            MovePlayerTo(5, 5);
            _sys.TestStepToCell(new Vector2Int(5, 5));

            Assert.AreEqual(4, _occ.CurrentVisualLayer,
                "Player must drop straight to 4, not step 8→7→6→5→4 across frames.");
        }

        // ── No-op conditions ─────────────────────────────────────────────────

        [Test]
        public void UnderfootEqualsCurrent_DoesNotFire()
        {
            _occ.SetVisualLayer(3);
            PaintTile(TilemapLayerSetup.TilemapLayer.ObjectsLow, 5, 5); // underfoot = 3

            MovePlayerTo(5, 5);
            bool fired = _sys.TestStepToCell(new Vector2Int(5, 5));

            Assert.IsFalse(fired);
            Assert.AreEqual(3, _occ.CurrentVisualLayer,
                "Same layer must not retrigger SetVisualLayer.");
        }

        [Test]
        public void UnderfootHigherThanCurrent_DoesNotAutoClimb()
        {
            // Decision 3: only drops, never climbs.
            _occ.SetVisualLayer(0);
            PaintTile(TilemapLayerSetup.TilemapLayer.OverheadDetails, 5, 5); // underfoot = 8

            MovePlayerTo(5, 5);
            bool fired = _sys.TestStepToCell(new Vector2Int(5, 5));

            Assert.IsFalse(fired);
            Assert.AreEqual(0, _occ.CurrentVisualLayer,
                "Auto-drop must NEVER raise the layer — climbing requires a jump tile.");
        }

        [Test]
        public void VoidCell_DoesNotFire()
        {
            // Decision 2: void cells are blocked by the movement clamp; even if
            // a teleport bypasses it, auto-drop must no-op (not drop to 0).
            _occ.SetVisualLayer(5);
            // No tiles painted anywhere → underfoot returns -1.

            MovePlayerTo(7, 7);
            bool fired = _sys.TestStepToCell(new Vector2Int(7, 7));

            Assert.IsFalse(fired);
            Assert.AreEqual(5, _occ.CurrentVisualLayer,
                "Void cell must NOT drop the player — the layer stays where it was.");
        }

        // ── Cell-enter dispatch ──────────────────────────────────────────────

        [Test]
        public void SameCellTwice_DoesNotReFire()
        {
            _occ.SetVisualLayer(8);
            PaintTile(TilemapLayerSetup.TilemapLayer.Ground, 5, 5);

            MovePlayerTo(5, 5);
            Assert.IsTrue(_sys.TestStepToCell(new Vector2Int(5, 5)),
                "Initial cell-enter should fire.");

            // Even with the player still on the tile and current==underfoot
            // mismatch, the cell-enter tracker must block a second fire.
            _occ.SetVisualLayer(8); // simulate re-elevation by some other path
            Assert.IsFalse(_sys.TestStepToCell(new Vector2Int(5, 5)),
                "Standing on the same cell must NOT re-trigger the drop.");
        }

        [Test]
        public void LeaveAndReEnter_FiresAgain()
        {
            // No consumed state — re-visiting the cell after walking away must
            // re-fire the drop. Matches the "stand still: no, walk away and
            // back: yes" semantic that Layer Jumps already use.
            _occ.SetVisualLayer(8);
            PaintTile(TilemapLayerSetup.TilemapLayer.Ground, 5, 5);
            PaintTile(TilemapLayerSetup.TilemapLayer.Ground, 10, 10); // adjacent tile

            MovePlayerTo(5, 5);
            _sys.TestStepToCell(new Vector2Int(5, 5));
            Assert.AreEqual(0, _occ.CurrentVisualLayer);

            // Walk away.
            _occ.SetVisualLayer(8);
            MovePlayerTo(10, 10);
            _sys.TestStepToCell(new Vector2Int(10, 10));
            Assert.AreEqual(0, _occ.CurrentVisualLayer);

            // Re-elevate, then come back.
            _occ.SetVisualLayer(8);
            MovePlayerTo(5, 5);
            bool refired = _sys.TestStepToCell(new Vector2Int(5, 5));
            Assert.IsTrue(refired, "Returning to a cell after leaving must re-fire.");
            Assert.AreEqual(0, _occ.CurrentVisualLayer);
        }

        // ── Coexistence with Layer Jumps ─────────────────────────────────────

        [Test]
        public void CellWithJumpTile_AutoDropYields()
        {
            // Decision 5: a painted jump tile owns the cell's transition.
            // LayerJumpTriggerSystem will fire this same frame; auto-drop
            // must NOT also race a SetVisualLayer call.
            _occ.SetVisualLayer(8);
            PaintTile(TilemapLayerSetup.TilemapLayer.Ground, 5, 5);
            _jumps.Set(new Vector2Int(5, 5), "3"); // author painted a jump-to-3

            MovePlayerTo(5, 5);
            bool fired = _sys.TestStepToCell(new Vector2Int(5, 5));

            Assert.IsFalse(fired,
                "Auto-drop must yield when a jump tile sits on the cell.");
            Assert.AreEqual(8, _occ.CurrentVisualLayer,
                "Layer must NOT change from auto-drop. The jump system " +
                "(driven separately) is the one allowed to update it.");
        }

        // ── Probe integration ────────────────────────────────────────────────

        [Test]
        public void CollisionOnlyCell_DoesNotCountAsUnderfoot()
        {
            // VisualLayerProbe.GetTopmostLayer skips Collision — a cell with
            // ONLY a Collision (invisible wall) tile must read as void from
            // the auto-drop perspective. Otherwise the system would drop the
            // player onto layer 2 (Collision), which has no visual surface.
            _occ.SetVisualLayer(5);
            PaintTile(TilemapLayerSetup.TilemapLayer.Collision, 5, 5);

            MovePlayerTo(5, 5);
            bool fired = _sys.TestStepToCell(new Vector2Int(5, 5));

            Assert.IsFalse(fired);
            Assert.AreEqual(5, _occ.CurrentVisualLayer);
        }
    }
}
