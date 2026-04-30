// Auto-generated test for player↔tile collision verification.
// Validates the runtime collision pipeline that lives in WorldGridBuilder + OverlayLoader:
//   Tilemap (collider tiles) → TilemapCollider2D (usedByComposite) → CompositeCollider2D (Manual) → Static Rigidbody2D
// Spawns a player-like Dynamic Rigidbody2D + BoxCollider2D and asserts physics blocks/permits movement.

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Valkur.Tests.PlayMode.World
{
    /// <summary>
    /// End-to-end physics tests proving the player Rigidbody2D is correctly blocked
    /// by tiles painted on a Collision-style tilemap with the same component stack
    /// that <see cref="Valkur.Gameplay.World.WorldGridBuilder"/> creates at runtime.
    /// </summary>
    [TestFixture]
    public class PlayerTileCollisionPlayTests
    {
        private const int PlayerLayer = 8; // Mirror project Physics2D layer.
        private GameObject _gridGo;
        private GameObject _playerGo;
        private Tilemap _tilemap;
        private CompositeCollider2D _composite;
        private Rigidbody2D _playerRb;
        private Tile _wallTile;

        [SetUp]
        public void SetUp()
        {
            // Grid + child tilemap that mirrors WorldGridBuilder output.
            _gridGo = new GameObject("TestGrid");
            var grid = _gridGo.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);

            var tmGo = new GameObject("TestCollision");
            tmGo.transform.SetParent(_gridGo.transform, false);
            _tilemap = tmGo.AddComponent<Tilemap>();
            tmGo.AddComponent<TilemapRenderer>();

            // Component order MUST mirror WorldGridBuilder.CreateTilemapLayer:
            //   1. TilemapCollider2D (no usedByComposite yet)
            //   2. CompositeCollider2D (auto-adds Rigidbody2D via [RequireComponent])
            //   3. usedByComposite = true (only valid once a composite exists on the GO)
            //   4. Rigidbody2D.bodyType = Static
            // Setting usedByComposite=true BEFORE the composite is added silently
            // leaves the TilemapCollider2D unmanaged → the composite produces 0 paths.
            var tmCol = tmGo.AddComponent<TilemapCollider2D>();

            _composite = tmGo.AddComponent<CompositeCollider2D>();
            _composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            _composite.generationType = CompositeCollider2D.GenerationType.Manual;

            tmCol.usedByComposite = true;

            var rb = tmGo.GetComponent<Rigidbody2D>();
            Assert.IsNotNull(rb, "[RequireComponent(Rigidbody2D)] on CompositeCollider2D must auto-add a Rigidbody2D.");
            rb.bodyType = RigidbodyType2D.Static;

            // Invisible Tile with Grid collider — same recipe used by OverlayLoader/WorldLoader.
            // CRITICAL: Tile.colliderType=Grid alone is NOT enough; the Tile must have a
            // Sprite assigned, otherwise TilemapCollider2D treats the cell as empty and
            // the composite produces 0 paths. Reuse the project's existing wall sprite.
            var wallSprite = Resources.Load<Sprite>("Tiles/wall");
            Assert.IsNotNull(wallSprite, "Resources/Tiles/wall sprite must exist for this test.");
            _wallTile = ScriptableObject.CreateInstance<Tile>();
            _wallTile.sprite = wallSprite;
            _wallTile.color = new Color(1f, 1f, 1f, 0f);
            _wallTile.colliderType = Tile.ColliderType.Grid;
            _wallTile.hideFlags = HideFlags.HideAndDontSave;

            // Player-like body: Dynamic RB + BoxCollider2D 0.5x0.3 (matches Player.prefab).
            _playerGo = new GameObject("TestPlayer") { layer = PlayerLayer };
            _playerRb = _playerGo.AddComponent<Rigidbody2D>();
            _playerRb.gravityScale = 0f;
            _playerRb.freezeRotation = true;
            _playerRb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var box = _playerGo.AddComponent<BoxCollider2D>();
            box.size = new Vector2(0.5f, 0.3f);

            // Project's matrix has Player(8) ↔ Default(0) enabled, so the test layer
            // (Default for the tilemap) and Player must collide. Sanity check up front:
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(PlayerLayer, 0),
                "Player layer must collide with Default layer for this test to be meaningful.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_playerGo != null) Object.Destroy(_playerGo);
            if (_gridGo != null) Object.Destroy(_gridGo);
            if (_wallTile != null) Object.Destroy(_wallTile);
            yield return null;
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private void PaintWall(int x, int y) => _tilemap.SetTile(new Vector3Int(x, y, 0), _wallTile);

        /// <summary>
        /// Force the TilemapCollider2D to process queued SetTile changes BEFORE the
        /// composite bake. Calling <c>GenerateGeometry()</c> on the same frame as
        /// <c>SetTile()</c> can race the collider's deferred change-processing pass
        /// and produce a 0-path composite. <c>RefreshAllTiles</c> + a frame yield is
        /// the stable recipe used by the editor's TilePainter undo path.
        /// </summary>
        private IEnumerator BakeComposite()
        {
            _tilemap.RefreshAllTiles();
            yield return null;
            _composite.GenerateGeometry();
            yield return new WaitForFixedUpdate();
        }

        private static IEnumerator StepPhysics(int frames)
        {
            for (int i = 0; i < frames; i++) yield return new WaitForFixedUpdate();
        }

        // ── Tests ────────────────────────────────────────────────────────

        /// <summary>
        /// Painting Tile(colliderType=Grid) cells and calling GenerateGeometry must
        /// produce at least one composite path. Catches the "tiles painted but
        /// nothing blocks" symptom upstream of the physics step.
        /// </summary>
        [UnityTest]
        public IEnumerator PaintedWalls_AfterGenerateGeometry_HasNonZeroPathCount()
        {
            for (int y = -2; y <= 2; y++) PaintWall(5, y);
            yield return BakeComposite();
            Assert.Greater(_composite.pathCount, 0,
                "CompositeCollider2D should have produced at least one path after baking painted wall tiles.");
        }

        /// <summary>
        /// The headline test: a player walking RIGHT into a vertical wall must NOT
        /// pass through it. Replicates the lobby scenario where the user reports
        /// the character ignoring colliders.
        /// </summary>
        [UnityTest]
        public IEnumerator Player_MovingIntoWall_IsBlockedAndDoesNotPenetrate()
        {
            for (int y = -3; y <= 3; y++) PaintWall(5, y);
            yield return BakeComposite();

            _playerGo.transform.position = new Vector3(2f, 0f, 0f);
            yield return new WaitForFixedUpdate();

            // Drive at 5 u/s for ~1.5 s of simulation. Without collision, x → 9.5.
            const float speed = 5f;
            for (int i = 0; i < 90; i++)
            {
                _playerRb.velocity = new Vector2(speed, 0f);
                yield return new WaitForFixedUpdate();
            }

            // Wall left edge is at x=5 (cell 5..6). Player half-width 0.25 → max x = 4.75.
            Assert.Less(_playerGo.transform.position.x, 5f,
                $"Player penetrated the wall. Final x = {_playerGo.transform.position.x:F3}.");
        }

        /// <summary>
        /// Negative control: with NO tiles painted on the Collision tilemap, the
        /// same player must pass through unimpeded. Proves the test rig isn't
        /// blocking by accident (e.g. via the tilemap GameObject itself).
        /// </summary>
        [UnityTest]
        public IEnumerator Player_WithoutWalls_MovesFreely()
        {
            yield return BakeComposite(); // No tiles painted → empty geometry.
            _playerGo.transform.position = new Vector3(2f, 0f, 0f);
            yield return new WaitForFixedUpdate();

            for (int i = 0; i < 60; i++)
            {
                _playerRb.velocity = new Vector2(5f, 0f);
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(_playerGo.transform.position.x, 5f,
                "Player should have travelled past x=5 with no walls in the way.");
        }

        /// <summary>
        /// Regression for the "forgot to bake" bug: tiles are painted but
        /// GenerateGeometry is never called. Without the bake, pathCount must be
        /// 0 and the player must pass through. Confirms that calling
        /// GenerateGeometry is mandatory after runtime SetTile.
        /// </summary>
        [UnityTest]
        public IEnumerator Player_WithUnbakedComposite_PassesThrough()
        {
            for (int y = -3; y <= 3; y++) PaintWall(5, y);
            // INTENTIONALLY skip _composite.GenerateGeometry()
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(0, _composite.pathCount,
                "Sanity: composite must be unbaked at this point.");

            _playerGo.transform.position = new Vector3(2f, 0f, 0f);
            for (int i = 0; i < 60; i++)
            {
                _playerRb.velocity = new Vector2(5f, 0f);
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(_playerGo.transform.position.x, 5f,
                "Without GenerateGeometry the composite should not block — confirming the bake step is what enables collision.");
        }

        /// <summary>
        /// Mirrors the TileEditor "paint a new collider" workflow: painting an
        /// extra wall AFTER the initial bake and rebaking must extend the
        /// blocking region. Guards the runtime edit path.
        /// </summary>
        [UnityTest]
        public IEnumerator Player_BlockedByWallPaintedAfterInitialBake_AfterRebake()
        {
            yield return BakeComposite(); // First bake with no tiles.

            for (int y = -3; y <= 3; y++) PaintWall(7, y);
            yield return BakeComposite(); // Re-bake after runtime paint.

            _playerGo.transform.position = new Vector3(2f, 0f, 0f);
            for (int i = 0; i < 120; i++)
            {
                _playerRb.velocity = new Vector2(5f, 0f);
                yield return new WaitForFixedUpdate();
            }

            Assert.Less(_playerGo.transform.position.x, 7f,
                $"Player penetrated wall painted after the first bake. Final x = {_playerGo.transform.position.x:F3}.");
        }

        /// <summary>
        /// Player approaching the wall from the right must also be blocked
        /// (covers the "only one direction works" class of bugs).
        /// </summary>
        [UnityTest]
        public IEnumerator Player_MovingLeftIntoWall_IsBlocked()
        {
            for (int y = -3; y <= 3; y++) PaintWall(5, y);
            yield return BakeComposite();

            _playerGo.transform.position = new Vector3(8f, 0f, 0f);
            yield return new WaitForFixedUpdate();

            for (int i = 0; i < 90; i++)
            {
                _playerRb.velocity = new Vector2(-5f, 0f);
                yield return new WaitForFixedUpdate();
            }

            // Wall right edge at x=6 → player center min ≈ 6 + 0.25 = 6.25.
            Assert.Greater(_playerGo.transform.position.x, 6f,
                $"Player penetrated the wall from the right. Final x = {_playerGo.transform.position.x:F3}.");
        }

        /// <summary>
        /// End-to-end runtime check using the SAME components GameplaySceneSetup
        /// builds: <see cref="Valkur.Gameplay.World.WorldGridBuilder"/> + the real
        /// <see cref="Valkur.Gameplay.World.OverlayLoader"/> against the actual
        /// shipped <c>lobby.overlay.json</c>. After loading + baking, the lobby's
        /// Collision composite must have non-zero <c>pathCount</c>. This is the
        /// authoritative check for the user-reported bug "the lobby colliders
        /// don't block the player".
        /// </summary>
        [UnityTest]
        public IEnumerator LobbyOverlay_AtRuntime_ProducesBlockingCollisionGeometry()
        {
            var builderGo = new GameObject("RuntimeWorldGridBuilder");
            var builder = builderGo.AddComponent<Valkur.Gameplay.World.WorldGridBuilder>();
            // Awake on AddComponent already calls BuildGrid; calling again is a no-op.
            yield return null;

            var collision = builder.GetTilemap(
                Valkur.Gameplay.World.TilemapLayerSetup.TilemapLayer.Collision);
            Assert.IsNotNull(collision, "Runtime grid did not produce a Collision tilemap.");

            Valkur.Gameplay.World.OverlayLoader.LoadOverlay("lobby.overlay.json", builder, 0, 0);
            yield return new WaitForFixedUpdate();

            var lobbyComposite = collision.GetComponent<CompositeCollider2D>();
            Assert.IsNotNull(lobbyComposite, "Collision tilemap is missing CompositeCollider2D.");
            lobbyComposite.GenerateGeometry();
            yield return new WaitForFixedUpdate();

            int painted = 0;
            var bounds = collision.cellBounds;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                    if (collision.HasTile(new Vector3Int(x, y, 0))) painted++;

            Assert.Greater(painted, 0, "Lobby overlay produced no painted Collision cells.");
            Assert.Greater(lobbyComposite.pathCount, 0,
                $"Lobby Collision composite has 0 paths after bake despite {painted} painted cells. " +
                "This reproduces the user-reported bug: tiles are present but nothing blocks the player.");

            Object.Destroy(builderGo);
        }

        /// <summary>
        /// Regression for the user-reported bug "lobby colliders don't work unless
        /// I press Auto-Generate from Walls". <see cref="Valkur.Gameplay.Bootstrap.GameplaySceneSetup.Start"/>
        /// loads the world AND rebakes composites synchronously on the same frame —
        /// no <c>yield</c>, no <c>RefreshAllTiles</c>. Without explicit refresh, the
        /// TilemapCollider2D has not yet processed the queued <c>SetTile</c> calls
        /// when <c>GenerateGeometry</c> runs, so the composite has 0 paths and the
        /// player walks through the walls. <see cref="GameplaySceneSetup.RebakeTilemapColliders"/>
        /// MUST refresh tiles before baking.
        /// </summary>
        [UnityTest]
        public IEnumerator SameFrame_PaintThenBake_WithoutRefresh_HasZeroPaths_RegressionGuard()
        {
            // Paint on the frame we set up the test rig — simulating the cold-start
            // race in GameplaySceneSetup (LoadWorld → RebakeTilemapColliders, no yield).
            for (int y = -3; y <= 3; y++) PaintWall(5, y);

            // NO RefreshAllTiles, NO frame yield: bake immediately.
            _composite.GenerateGeometry();

            // Allow exactly one fixed step so a passing test wouldn't be due to
            // physics not running at all.
            yield return new WaitForFixedUpdate();

            // The historical (buggy) behaviour produced pathCount == 0. We now
            // assert it: this test exists so any future change that re-introduces
            // the same-frame race will trip immediately. If you ever need to
            // change the assertion direction, you must also fix
            // GameplaySceneSetup.RebakeTilemapColliders to refresh + yield.
            Assert.AreEqual(0, _composite.pathCount,
                "Same-frame SetTile + GenerateGeometry should produce 0 paths " +
                "without RefreshAllTiles. If this assertion fails, the fix in " +
                "RebakeTilemapColliders may be unnecessary or Unity's behavior changed.");
        }

        /// <summary>
        /// Positive regression: documents the EXACT recipe used by
        /// <see cref="GameplaySceneSetup.RebakeTilemapColliders"/> after the fix:
        /// paint synchronously, refresh tiles, then bake on the NEXT frame
        /// (a synchronous bake on the same frame still produces 0 paths even
        /// with <c>RefreshAllTiles</c> — the TilemapCollider2D requires a frame
        /// to ingest the changes before the composite can resolve them).
        /// </summary>
        [UnityTest]
        public IEnumerator PaintRefresh_BakeNextFrame_HasNonZeroPaths_FixGuard()
        {
            for (int y = -3; y <= 3; y++) PaintWall(5, y);

            _tilemap.RefreshAllTiles();
            yield return null; // Critical: one frame for TilemapCollider2D to ingest changes.
            _composite.GenerateGeometry();

            yield return new WaitForFixedUpdate();

            Assert.Greater(_composite.pathCount, 0,
                "RefreshAllTiles + 1-frame yield + GenerateGeometry must produce paths. " +
                "This is the recipe GameplaySceneSetup.DeferredRebakeNextFrame relies on.");
        }
    }
}
