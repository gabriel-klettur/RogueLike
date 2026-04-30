// Codifies the runtime collision bake recipe as executable specification.
//
// The lobby walk-through-walls bug was a race between SetTile and
// CompositeCollider2D.GenerateGeometry: same-frame baking yielded pathCount=0
// even though tiles were painted. The fix is a two-stage bake:
//
//   1. SetTile(...) bursts
//   2. tilemap.RefreshAllTiles()  — flushes queued changes synchronously
//   3. yield return null          — wait one frame for TilemapCollider2D
//   4. composite.GenerateGeometry() — produces non-zero paths
//
// These tests pin every variation so a future "optimization" that drops a
// step will fail loudly with a clear diagnostic.

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Valkur.Tests.PlayMode.World
{
    /// <summary>
    /// Behavioural regression suite for the runtime tile→collider bake recipe.
    /// </summary>
    [TestFixture]
    public class CollisionPipelineRegressionPlayTests
    {
        private GameObject _gridGo;
        private Tilemap _tilemap;
        private CompositeCollider2D _composite;
        private Tile _wallTile;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("RegressionGrid");
            var grid = _gridGo.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);

            var tmGo = new GameObject("Collision");
            tmGo.transform.SetParent(_gridGo.transform, false);
            _tilemap = tmGo.AddComponent<Tilemap>();
            tmGo.AddComponent<TilemapRenderer>();

            var tmCol = tmGo.AddComponent<TilemapCollider2D>();
            _composite = tmGo.AddComponent<CompositeCollider2D>();
            _composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            _composite.generationType = CompositeCollider2D.GenerationType.Manual;
            tmCol.usedByComposite = true;

            var rb = tmGo.GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;

            var wallSprite = Resources.Load<Sprite>("Tiles/wall");
            Assert.IsNotNull(wallSprite, "Resources/Tiles/wall must exist.");
            _wallTile = ScriptableObject.CreateInstance<Tile>();
            _wallTile.sprite = wallSprite;
            _wallTile.color = new Color(1f, 1f, 1f, 0f);
            _wallTile.colliderType = Tile.ColliderType.Grid;
            _wallTile.hideFlags = HideFlags.HideAndDontSave;
        }

        [TearDown]
        public void TearDown()
        {
            if (_gridGo != null) Object.Destroy(_gridGo);
            if (_wallTile != null) Object.Destroy(_wallTile);
        }

        private void PaintWall(int x, int y) =>
            _tilemap.SetTile(new Vector3Int(x, y, 0), _wallTile);

        private void PaintColumn(int x, int y0, int y1)
        {
            for (int y = y0; y <= y1; y++) PaintWall(x, y);
        }

        // ── 1. The full recipe: should always succeed ───────────────────────────

        [UnityTest]
        public IEnumerator FullRecipe_RefreshThenYieldThenBake_ProducesPaths()
        {
            PaintColumn(5, -2, 2);
            _tilemap.RefreshAllTiles();
            yield return null;
            _composite.GenerateGeometry();
            yield return new WaitForFixedUpdate();

            Assert.Greater(_composite.pathCount, 0,
                "Recipe must produce non-zero paths.");
        }

        // ── 2. Negative variations: each missing step must fail ────────────────

        [UnityTest]
        public IEnumerator MissingRefresh_BakeOnly_HasZeroPaths()
        {
            PaintColumn(5, -2, 2);
            // No RefreshAllTiles, no yield.
            _composite.GenerateGeometry();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(0, _composite.pathCount,
                "Without RefreshAllTiles + frame yield, same-frame bake must be empty " +
                "(this is the bug we shipped a fix for — keep it pinned).");
        }

        [UnityTest]
        public IEnumerator MissingYield_RefreshThenImmediateBake_HasZeroPaths()
        {
            PaintColumn(5, -2, 2);
            _tilemap.RefreshAllTiles();
            // No yield — immediate bake.
            _composite.GenerateGeometry();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(0, _composite.pathCount,
                "RefreshAllTiles alone is not enough; TilemapCollider2D needs a " +
                "frame to ingest the changes before GenerateGeometry sees them.");
        }

        // ── 3. Two-stage bake: immediate-then-deferred (production pattern) ─────

        [UnityTest]
        public IEnumerator TwoStageBake_ImmediateThenDeferred_RecoversFromZeroPaths()
        {
            // Mimics GameplaySceneSetup.RebakeTilemapColliders():
            //   - immediate refresh + bake (yields 0 paths on first frame),
            //   - deferred coroutine fires next frame and re-bakes (yields > 0).
            PaintColumn(5, -2, 2);

            // Stage 1: immediate (the failing-but-harmless first attempt).
            _tilemap.RefreshAllTiles();
            _composite.GenerateGeometry();
            // Don't assert pathCount here — Unity may produce 0 or >0 depending on
            // internal timing; the deferred stage is what we GUARANTEE.

            // Stage 2: deferred next-frame bake.
            yield return null;
            _tilemap.RefreshAllTiles();
            _composite.GenerateGeometry();
            yield return new WaitForFixedUpdate();

            Assert.Greater(_composite.pathCount, 0,
                "Deferred re-bake stage MUST produce non-zero paths. " +
                "If this fails the GameplaySceneSetup safety net is broken.");
        }

        // ── 4. Idempotency: re-baking the same data must keep working ──────────

        [UnityTest]
        public IEnumerator RepeatedBakes_AreIdempotentAndStable()
        {
            PaintColumn(5, -2, 2);
            _tilemap.RefreshAllTiles();
            yield return null;
            _composite.GenerateGeometry();
            yield return new WaitForFixedUpdate();
            int firstPathCount = _composite.pathCount;
            Assert.Greater(firstPathCount, 0);

            for (int i = 0; i < 3; i++)
            {
                _composite.GenerateGeometry();
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(firstPathCount, _composite.pathCount,
                    $"Bake iteration {i + 1} changed pathCount unexpectedly.");
            }
        }

        // ── 5. Incremental paint after initial bake ─────────────────────────────

        [UnityTest]
        public IEnumerator AddTilesAfterInitialBake_RebakeWithRecipe_PicksUpNewTiles()
        {
            PaintColumn(5, -2, 2);
            _tilemap.RefreshAllTiles();
            yield return null;
            _composite.GenerateGeometry();
            yield return new WaitForFixedUpdate();
            int before = _composite.pathCount;

            PaintColumn(10, -2, 2);
            _tilemap.RefreshAllTiles();
            yield return null;
            _composite.GenerateGeometry();
            yield return new WaitForFixedUpdate();

            Assert.GreaterOrEqual(_composite.pathCount, before,
                "Adding a disjoint wall column must not REDUCE path count.");
            Assert.Greater(_composite.pathCount, 0,
                "Composite must still have geometry after incremental paint.");
        }

        // ── 6. Clearing tiles must remove geometry ──────────────────────────────

        [UnityTest]
        public IEnumerator ClearAllTiles_AfterBake_ProducesZeroPaths()
        {
            PaintColumn(5, -2, 2);
            _tilemap.RefreshAllTiles();
            yield return null;
            _composite.GenerateGeometry();
            yield return new WaitForFixedUpdate();
            Assert.Greater(_composite.pathCount, 0);

            _tilemap.ClearAllTiles();
            _tilemap.RefreshAllTiles();
            yield return null;
            _composite.GenerateGeometry();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(0, _composite.pathCount,
                "After ClearAllTiles + recipe re-bake, composite must be empty.");
        }

        // ── 7. Large-scale paint: the recipe must scale ─────────────────────────

        [UnityTest]
        public IEnumerator LargePaint_30x30Walls_BakesToNonZeroPathsUnderBudget()
        {
            const int N = 30;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int x = 0; x < N; x++)
                for (int y = 0; y < N; y++)
                    PaintWall(x, y);
            _tilemap.RefreshAllTiles();
            yield return null;
            _composite.GenerateGeometry();
            yield return new WaitForFixedUpdate();
            sw.Stop();

            Assert.Greater(_composite.pathCount, 0,
                "Large-scale paint produced 0 paths.");
            // Generous budget — the actual bake on a dev machine is single-digit ms.
            Assert.Less(sw.ElapsedMilliseconds, 1500,
                $"Bake of {N}x{N} walls took {sw.ElapsedMilliseconds} ms — too slow.");
            Debug.Log($"[CollisionPipelineRegression] {N}x{N} bake: " +
                      $"{sw.ElapsedMilliseconds} ms, paths={_composite.pathCount}.");
        }
    }
}
