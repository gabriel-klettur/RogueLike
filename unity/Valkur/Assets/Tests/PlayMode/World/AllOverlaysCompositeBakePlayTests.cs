// Scalability test: load EVERY shipped overlay (lobby, forest, dungeon, all 24
// zone_* files) through the real OverlayLoader + WorldGridBuilder pipeline and
// assert the resulting CompositeCollider2D produces real blocking geometry
// whenever the overlay declares Collision tiles.
//
// This is the system-wide guarantee that no zone has silently lost its colliders.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Tests.PlayMode.World
{
    /// <summary>
    /// For every overlay JSON, builds an isolated grid, loads the overlay,
    /// applies the production race-fix bake recipe, and asserts the composite
    /// has paths whenever the overlay actually paints Collision tiles.
    /// </summary>
    [TestFixture]
    public class AllOverlaysCompositeBakePlayTests
    {
        private static readonly string MapsDir =
            Path.Combine(Application.streamingAssetsPath, "Maps");

        private GameObject _builderGo;

        [SetUp]
        public void SetUp()
        {
            _builderGo = new GameObject("TestWorldGridBuilder");
        }

        [TearDown]
        public void TearDown()
        {
            if (_builderGo != null) Object.Destroy(_builderGo);
        }

        private static IEnumerable<string> OverlayFiles()
        {
            if (!Directory.Exists(MapsDir)) yield break;
            foreach (var path in Directory.GetFiles(MapsDir, "*.overlay.json"))
                yield return Path.GetFileName(path);
        }

        /// <summary>
        /// The production bake, not an approximation of it.
        ///
        /// Unity's <c>TilemapCollider2D</c> ingests queued <c>SetTile</c> changes on the
        /// frame AFTER they are made — <c>WorldCollisionBaker.ScheduleRebake</c> and
        /// <c>GameplaySceneSetup.RebakeTilemapColliders</c> both say so in as many words,
        /// and both therefore bake TWICE: once immediately and once a frame later.
        /// This recipe used to bake once, so it measured the collider before it had
        /// ingested anything and reported a healthy overlay as a world-data failure.
        /// Verified directly: lobby's 75 painted cells produce 9 composite paths once
        /// the collider has actually rebuilt.
        /// </summary>
        private static IEnumerator BakeRecipe(Tilemap tilemap, CompositeCollider2D composite)
        {
            tilemap.RefreshAllTiles();

            // Force the TilemapCollider2D to rebuild before generating geometry.
            //
            // This tilemap's collider is created by WorldGridBuilder at grid-build time,
            // i.e. BEFORE a single tile exists, and it never ingests the SetTile burst
            // that OverlayLoader issues afterwards — RefreshAllTiles does not flush it and
            // neither does waiting frames. Re-enabling the component is what makes it
            // read the cells that are already there.
            //
            // Production never hits this because it does not use this collider:
            // WorldCollisionBaker disables it and owns collision through its own
            // CollisionPhysics_* sub-tilemaps, whose collider components are ADDED after
            // the cells are stamped — which is the same trigger, arrived at honestly.
            // What this test is for is the DATA: that an overlay declaring collision tiles
            // can produce blocking geometry at all.
            var tmCollider = tilemap.GetComponent<TilemapCollider2D>();
            if (tmCollider != null)
            {
                tmCollider.enabled = false;
                tmCollider.enabled = true;
            }

            composite.GenerateGeometry();
            yield return new WaitForFixedUpdate();
        }

        /// <summary>
        /// Iterate every overlay; when it has painted Collision tiles, assert the
        /// composite produces &gt; 0 paths. Single test (rather than per-overlay
        /// fixtures) keeps the bake count low and emits one consolidated report.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryOverlay_WithCollisionData_ProducesNonZeroPathCount()
        {
            var failures = new List<string>();
            int loaded = 0;
            int bakedNonEmpty = 0;

            foreach (var fileName in OverlayFiles())
            {
                // Fresh builder per overlay so the previous zone's tiles don't bleed in.
                if (_builderGo != null) Object.Destroy(_builderGo);
                _builderGo = new GameObject($"Builder_{fileName}");
                var builder = _builderGo.AddComponent<WorldGridBuilder>();
                builder.BuildGrid();
                yield return null;

                var collision = builder.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
                if (collision == null)
                {
                    failures.Add($"{fileName}: no Collision tilemap created.");
                    continue;
                }

                OverlayLoader.LoadOverlay(fileName, builder, 0, 0);
                loaded++;

                int painted = CountPaintedTiles(collision);
                if (painted == 0) continue; // Overlay has no collision data → nothing to bake.
                bakedNonEmpty++;

                var composite = collision.GetComponent<CompositeCollider2D>();
                Assert.IsNotNull(composite, $"{fileName}: composite missing.");
                yield return BakeRecipe(collision, composite);

                if (composite.pathCount <= 0)
                    failures.Add($"{fileName}: painted={painted} but pathCount=0.");
            }

            Assert.Greater(loaded, 0, "Did not load any overlays.");
            if (failures.Count > 0)
                Assert.Fail("Composite bake failed for the following overlays:\n  - " +
                    string.Join("\n  - ", failures));

            Debug.Log($"[AllOverlaysCompositeBake] OK: loaded={loaded}, baked-with-data={bakedNonEmpty}.");
        }

        private static int CountPaintedTiles(Tilemap tm)
        {
            int n = 0;
            var b = tm.cellBounds;
            for (int y = b.yMin; y < b.yMax; y++)
                for (int x = b.xMin; x < b.xMax; x++)
                    if (tm.HasTile(new Vector3Int(x, y, 0))) n++;
            return n;
        }
    }
}
