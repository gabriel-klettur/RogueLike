using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World.Layering
{
    /// <summary>
    /// Coverage for the M1.9 void-cell hard-stop in
    /// <c>PlayerController.Movement.ClampInputAgainstVoid</c>. The clamp
    /// converts a player movement input into a "void-aware" version: any axis
    /// whose component would land the player inside a cell with zero tiles in
    /// any visual layer (a wall-by-absence) is zeroed out. Diagonal motion
    /// must still slide along a wall (axis-split test), matching how a real
    /// Unity collider feels.
    ///
    /// Behaviour pinned here:
    ///   • Painted cell next door → input passes through unchanged.
    ///   • Void cell next door     → input toward it gets zeroed.
    ///   • Diagonal with one axis  void + one axis valid → only the void axis
    ///     is zeroed (slide-along-edge).
    ///   • Zero input              → no-op, no probe call.
    ///   • Missing WorldGridBuilder→ no clamp (defensive — boot races).
    /// </summary>
    [TestFixture]
    public class PlayerVoidStopMovementTests
    {
        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _playerGo;
        private PlayerController _player;
        private Tile _tile;

        // moveSpeed * Time.fixedDeltaTime — the predict-step the clamp uses.
        // We override moveSpeed via reflection so the predicted cell lands
        // exactly one tile away from origin (1 unit step).
        private const float PREDICTABLE_MOVE_SPEED = 1f / 0.02f; // ≈ 50 → step = 1.0 at 50Hz fixed

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _playerGo = new GameObject("PlayerHost");
            // PlayerController requires Rigidbody2D + Health + the three layer
            // syncs; AddComponent satisfies the [RequireComponent] chain.
            _player = _playerGo.AddComponent<PlayerController>();

            _tile = ScriptableObject.CreateInstance<Tile>();
            _tile.name = "test_void_movement_tile";
            var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
            _tile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);

            // Drive moveSpeed to a deterministic value so the predict step
            // covers exactly 1 world unit at the default 0.02s fixed dt.
            var moveSpeedField = typeof(PlayerController).GetField(
                "moveSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
            moveSpeedField?.SetValue(_player, PREDICTABLE_MOVE_SPEED);

            // Seed the cached grid ref so the clamp doesn't need to
            // FindObjectOfType. We assign the local _grid directly.
            var gridField = typeof(PlayerController).GetField(
                "_voidProbeGrid", BindingFlags.Instance | BindingFlags.NonPublic);
            gridField?.SetValue(_player, _grid);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_playerGo);
            Object.DestroyImmediate(_gridGo);
            Object.DestroyImmediate(_tile);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void Paint(int x, int y)
            => _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground).SetTile(new Vector3Int(x, y, 0), _tile);

        private Vector2 Clamp(Vector2 input)
        {
            var method = typeof(PlayerController).GetMethod(
                "ClampInputAgainstVoid", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "PlayerController.ClampInputAgainstVoid must exist.");
            return (Vector2)method.Invoke(_player, new object[] { input });
        }

        private void PlaceAt(int x, int y) => _player.transform.position = new Vector3(x + 0.5f, y + 0.5f, 0f);

        // ── Cases ────────────────────────────────────────────────────────────

        [Test]
        public void PaintedDestination_InputPassesThrough()
        {
            // Standing at (0,0), walking +X into a painted (1,0) — no clamp.
            Paint(0, 0);
            Paint(1, 0);
            PlaceAt(0, 0);

            var result = Clamp(new Vector2(1f, 0f));

            Assert.AreEqual(1f, result.x, 0.001f,
                "Walking into a painted cell must not zero the input axis.");
            Assert.AreEqual(0f, result.y, 0.001f);
        }

        [Test]
        public void VoidDestination_AxisIsZeroed()
        {
            // (0,0) painted, (1,0) NOT painted → walking +X must zero out X.
            Paint(0, 0);
            PlaceAt(0, 0);

            var result = Clamp(new Vector2(1f, 0f));

            Assert.AreEqual(0f, result.x, 0.001f,
                "Walking into a void cell must zero the X axis (hard stop).");
        }

        [Test]
        public void Diagonal_OneAxisVoid_SlidesAlongValidEdge()
        {
            // (0,0) painted, (1,0) painted, (0,1) NOT painted, (1,1) NOT painted.
            // Walking +X+Y from (0,0): combined target (1,1) is void, X-only (1,0)
            // is valid, Y-only (0,1) is void → result should be (X=1, Y=0).
            Paint(0, 0);
            Paint(1, 0);
            PlaceAt(0, 0);

            var result = Clamp(new Vector2(1f, 1f));

            Assert.AreEqual(1f, result.x, 0.001f,
                "Axis-split slide: X is valid, must pass through.");
            Assert.AreEqual(0f, result.y, 0.001f,
                "Axis-split slide: Y leads into void, must be zeroed.");
        }

        [Test]
        public void Diagonal_BothAxesVoid_BothZeroed()
        {
            // Standing on (0,0) painted, everything around is void.
            Paint(0, 0);
            PlaceAt(0, 0);

            var result = Clamp(new Vector2(1f, 1f));

            Assert.AreEqual(0f, result.x, 0.001f);
            Assert.AreEqual(0f, result.y, 0.001f);
        }

        [Test]
        public void ZeroInput_NoClampInvoked_AndReturnedUnchanged()
        {
            // Standing in the middle of nowhere; even with no painted cells the
            // clamp must early-out on zero input rather than probe.
            PlaceAt(7, 7);

            var result = Clamp(Vector2.zero);

            Assert.AreEqual(0f, result.x, 0.001f);
            Assert.AreEqual(0f, result.y, 0.001f);
        }

        [Test]
        public void NoWorldGridBuilder_LeavesInputUnchanged()
        {
            // Defensive case: during boot, _voidProbeGrid can be null. The
            // clamp must return the raw input rather than freezing the player.
            var gridField = typeof(PlayerController).GetField(
                "_voidProbeGrid", BindingFlags.Instance | BindingFlags.NonPublic);
            gridField?.SetValue(_player, null);

            // Also remove the WorldGridBuilder from the scene so the lazy
            // FindObjectOfType inside ClampInputAgainstVoid can't repopulate it.
            Object.DestroyImmediate(_grid);
            _grid = null;
            Object.DestroyImmediate(_gridGo);
            _gridGo = null;

            PlaceAt(0, 0);

            var result = Clamp(new Vector2(1f, 0f));

            Assert.AreEqual(1f, result.x, 0.001f,
                "With no grid, the clamp must pass the input through " +
                "(otherwise the player can never move during scene boot).");
        }
    }
}
