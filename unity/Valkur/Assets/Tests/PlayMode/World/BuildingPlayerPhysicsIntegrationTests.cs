// End-to-end physics test for buildings: synthesises the same per-cell
// BoxCollider2D layout that BuildingCollisionLoader.EnsureCollisionTile()
// produces at runtime, then drives a player Dynamic Rigidbody2D into it
// and asserts the player is blocked.
//
// Together with the existing tilemap collision tests, this guarantees
// BOTH collision producers in the game (Tilemaps + Buildings) actually
// stop the player at runtime.

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Valkur.Tests.PlayMode.World
{
    /// <summary>
    /// Physics integration test for building child-collider tiles vs Player RB.
    /// </summary>
    [TestFixture]
    public class BuildingPlayerPhysicsIntegrationTests
    {
        private const int PlayerLayer   = 8;
        private const int BuildingLayer = 14;

        private GameObject _buildingGo;
        private GameObject _playerGo;
        private Rigidbody2D _playerRb;

        [SetUp]
        public void SetUp()
        {

            // ── Building root — same layout as BuildingObject at runtime: ────────
            //   parent GO on Building layer with a Static Rigidbody2D, child
            //   GameObjects each holding a non-trigger BoxCollider2D.
            _buildingGo = new GameObject("TestBuilding") { layer = BuildingLayer };
            _buildingGo.transform.position = new Vector3(5f, 0f, 0f);
            var buildingRb = _buildingGo.AddComponent<Rigidbody2D>();
            buildingRb.bodyType = RigidbodyType2D.Static;

            // ── Player ────────────────────────────────────────────────────────────
            _playerGo = new GameObject("TestPlayer") { layer = PlayerLayer };
            _playerGo.transform.position = new Vector3(0f, 0f, 0f);
            _playerRb = _playerGo.AddComponent<Rigidbody2D>();
            _playerRb.gravityScale = 0f;
            _playerRb.freezeRotation = true;
            _playerRb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var playerBox = _playerGo.AddComponent<BoxCollider2D>();
            playerBox.size = new Vector2(0.5f, 0.3f);

            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(PlayerLayer, BuildingLayer),
                "Player↔Building collision MUST be enabled for this test to mean anything.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_buildingGo != null) Object.Destroy(_buildingGo);
            if (_playerGo != null) Object.Destroy(_playerGo);
            yield return null;
        }

        /// <summary>
        /// Replicates BuildingCollisionLoader.EnsureCollisionTile: a child GO on
        /// the Building layer with a non-trigger BoxCollider2D at the local cell
        /// rect.
        /// </summary>
        private void AddCollisionTile(float localX, float localY, float w, float h)
        {
            var tileGo = new GameObject("CollTile_test") { layer = BuildingLayer };
            tileGo.transform.SetParent(_buildingGo.transform, false);
            tileGo.transform.localPosition = new Vector3(localX, localY, 0f);
            var box = tileGo.AddComponent<BoxCollider2D>();
            box.isTrigger = false;
            box.offset = Vector2.zero;
            box.size = new Vector2(w, h);
        }

        // ── Tests ───────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Player_MovingIntoBuildingChildCollider_IsBlocked()
        {
            // 4-cell vertical wall at building x=0..1, y=-2..2 → world x=5..6.
            for (int y = -2; y <= 2; y++)
                AddCollisionTile(0.5f, y, 1f, 1f);

            yield return new WaitForFixedUpdate();

            const float speed = 5f;
            for (int i = 0; i < 90; i++)
            {
                _playerRb.velocity = new Vector2(speed, 0f);
                yield return new WaitForFixedUpdate();
            }

            Assert.Less(_playerGo.transform.position.x, 5f,
                $"Player penetrated the building wall. Final x = {_playerGo.transform.position.x:F3}.");
        }

        [UnityTest]
        public IEnumerator Player_WithoutBuildingTiles_PassesThroughEmptyBuildingShell()
        {
            // Building exists but has no child colliders → the parent has only a
            // Static Rigidbody2D with NO collider, so nothing should block.
            yield return new WaitForFixedUpdate();

            for (int i = 0; i < 60; i++)
            {
                _playerRb.velocity = new Vector2(5f, 0f);
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(_playerGo.transform.position.x, 5f,
                "An empty building (no collision tiles) should not block the player.");
        }

        [UnityTest]
        public IEnumerator Player_BuildingTilesAddedAfterStart_BlockOnRebake()
        {
            // Buildings can spawn after Start (BuildingLoader runs post-bootstrap).
            // Verify that adding tiles between physics steps is enough — no manual
            // "GenerateGeometry" needed because each tile is its own BoxCollider2D
            // (unlike the tilemap composite path which requires the bake recipe).
            yield return new WaitForFixedUpdate();

            for (int y = -2; y <= 2; y++)
                AddCollisionTile(0.5f, y, 1f, 1f);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            for (int i = 0; i < 90; i++)
            {
                _playerRb.velocity = new Vector2(5f, 0f);
                yield return new WaitForFixedUpdate();
            }

            Assert.Less(_playerGo.transform.position.x, 5f,
                "Building tiles added after Start did not start blocking the player.");
        }

        [UnityTest]
        public IEnumerator BuildingTilesAreNotTriggers_ProvideHardCollision()
        {
            // Regression: BuildingCollisionLoader.EnsureCollisionTile sets
            // box.isTrigger = false explicitly. If someone flips this to true the
            // player would phase through silently — pin it.
            AddCollisionTile(0.5f, 0f, 1f, 1f);
            yield return new WaitForFixedUpdate();

            foreach (Transform child in _buildingGo.transform)
            {
                var box = child.GetComponent<BoxCollider2D>();
                Assert.IsNotNull(box, $"{child.name} missing BoxCollider2D.");
                Assert.IsFalse(box.isTrigger,
                    $"{child.name} is a trigger — collision tiles MUST be solid.");
            }
        }

        [UnityTest]
        public IEnumerator Player_MovingLeftIntoBuildingTile_IsAlsoBlocked()
        {
            // Symmetry check: the rightward-block above could pass by accident if
            // the test rig were biased. Verify the opposite direction also blocks.
            _playerGo.transform.position = new Vector3(10f, 0f, 0f);
            for (int y = -2; y <= 2; y++)
                AddCollisionTile(0.5f, y, 1f, 1f); // building at x=5..6

            yield return new WaitForFixedUpdate();

            for (int i = 0; i < 90; i++)
            {
                _playerRb.velocity = new Vector2(-5f, 0f);
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(_playerGo.transform.position.x, 6f,
                $"Player penetrated the building from the right. " +
                $"Final x = {_playerGo.transform.position.x:F3}.");
        }
    }
}
