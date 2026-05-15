using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Overlay
{
    /// <summary>
    /// Round-trip + legacy-migration coverage for the <c>collisionTags</c> matrix that
    /// the per-visual-layer-collisions feature adds to the overlay JSON schema.
    ///
    /// Why the suite matters: this matrix is the on-disk source of truth for which
    /// visual layer each painted collider applies to. A regression that drops the
    /// field on save, mis-orients the row indexing, or fails to default missing cells
    /// to the wildcard would silently break Move-To-Layer's collider-erase guarantee
    /// AND break the M2 runtime physics-filtering layer that consumes the same data.
    /// </summary>
    [TestFixture]
    public class CollisionTagPersistenceTests
    {
        private const string ZONE = "zone_test_collision_tags";

        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _zoneGo;
        private ZoneManager _zones;
        private TileOverlayPersistence _persistence;
        private Tile _wallTile;
        private CollisionTagMap _tagMap;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _zoneGo = new GameObject("ZoneManager");
            _zones = _zoneGo.AddComponent<ZoneManager>();
            _zones.AddZone(ZONE, new Vector2Int(0, 0), editableInTileEditor: true);

            _persistence = new TileOverlayPersistence(_zones, _grid);
            _tagMap = new CollisionTagMap();
            _persistence.CollisionTagMap = _tagMap;

            _wallTile = ScriptableObject.CreateInstance<Tile>();
            _wallTile.name = "wall";
            var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
            _wallTile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            _wallTile.sprite.name = "wall";
            TileRegistry.Instance.Register("wall", _wallTile);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            TileOverlayPersistence.DeleteOverride(ZONE);
            Object.DestroyImmediate(_gridGo);
            Object.DestroyImmediate(_zoneGo);
            Object.DestroyImmediate(_wallTile);
            TileRegistry.Instance.Load(null);
        }

        /// <summary>
        /// Author tags in three cells, save the zone, then load the JSON back via the
        /// canonical <see cref="OverlayLoader.ApplyCollisionTagsFromPath"/> path. Every
        /// non-empty tag must round-trip into a fresh map without loss; cells the user
        /// never tagged must still resolve to <see cref="CollisionTagMap.Wildcard"/>.
        /// </summary>
        [Test]
        public void RoundTrip_PreservesEveryAuthoredTag()
        {
            // Paint a collider on three cells of the Collision layer + author tags.
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            collision.SetTile(new Vector3Int(2, 3, 0), _wallTile);
            collision.SetTile(new Vector3Int(4, 5, 0), _wallTile);
            collision.SetTile(new Vector3Int(7, 7, 0), _wallTile);

            _tagMap.Set(new Vector2Int(2, 3), "0");
            _tagMap.Set(new Vector2Int(4, 5), "4");
            _tagMap.Set(new Vector2Int(7, 7), "*");

            _persistence.MarkCellDirty(new Vector3Int(2, 3, 0));
            Assert.IsTrue(_persistence.SaveZone(ZONE));

            string path = TileOverlayPersistence.OverridePathForZone(ZONE);
            Assert.IsTrue(File.Exists(path), "Save must produce the overlay file.");

            // Verify the field exists in the JSON literal.
            string json = File.ReadAllText(path);
            StringAssert.Contains("\"collisionTags\"", json,
                "Save must emit the collisionTags matrix when the tag map has entries.");

            // Fresh map → load via the canonical loader path.
            var roundTrip = new CollisionTagMap();
            int written = OverlayLoader.ApplyCollisionTagsFromPath(path, roundTrip, 0, 0);
            Assert.AreEqual(3, written, "All three authored tags must load.");

            Assert.AreEqual("0", roundTrip.Get(new Vector2Int(2, 3)));
            Assert.AreEqual("4", roundTrip.Get(new Vector2Int(4, 5)));
            Assert.AreEqual("*", roundTrip.Get(new Vector2Int(7, 7)));
            // Random unauthored cell still wildcards.
            Assert.AreEqual(CollisionTagMap.Wildcard, roundTrip.Get(new Vector2Int(0, 0)));
        }

        /// <summary>
        /// THE migration guard. A pre-feature overlay JSON has no <c>collisionTags</c>
        /// field — the loader must treat every collision cell as <see cref="CollisionTagMap.Wildcard"/>
        /// so the legacy "applies to every entity" runtime behaviour stays intact.
        /// Failing here would silently make every collider on every legacy zone
        /// stop blocking the player after M2 ships.
        /// </summary>
        [Test]
        public void LegacyOverlayWithoutField_LoadsAsWildcardOnly()
        {
            // Save a zone with NO tag entries. The persistence layer must not emit
            // the field (because the map is empty / wildcards by default).
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            collision.SetTile(new Vector3Int(1, 1, 0), _wallTile);
            _persistence.MarkCellDirty(new Vector3Int(1, 1, 0));
            Assert.IsTrue(_persistence.SaveZone(ZONE));

            string path = TileOverlayPersistence.OverridePathForZone(ZONE);
            string json = File.ReadAllText(path);
            StringAssert.DoesNotContain("\"collisionTags\"", json,
                "Empty tag map must NOT emit the collisionTags field — keeps legacy JSON byte-identical.");

            // Loading that file into a fresh map must leave it empty; every read
            // resolves to wildcard regardless of whether a collider sits there.
            var freshMap = new CollisionTagMap();
            int written = OverlayLoader.ApplyCollisionTagsFromPath(path, freshMap, 0, 0);
            Assert.AreEqual(0, written);
            Assert.AreEqual(CollisionTagMap.Wildcard, freshMap.Get(new Vector2Int(1, 1)));
        }

        /// <summary>
        /// Save must skip the <c>collisionTags</c> field entirely when the user has
        /// painted colliders without ever touching the tag picker. Verifies the
        /// <see cref="CollisionTagMap.HasAnyInRect"/> guard inside the persistence
        /// layer so legacy-shaped JSONs stay diff-clean for unchanged zones.
        /// </summary>
        [Test]
        public void SaveWithEmptyTagMap_DoesNotEmitField()
        {
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            collision.SetTile(new Vector3Int(0, 0, 0), _wallTile);
            _persistence.MarkCellDirty(new Vector3Int(0, 0, 0));
            _persistence.SaveZone(ZONE);

            string json = File.ReadAllText(TileOverlayPersistence.OverridePathForZone(ZONE));
            StringAssert.DoesNotContain("\"collisionTags\"", json);
        }

        /// <summary>
        /// Apply…FromPath must be a no-op on a missing file rather than NRE'ing —
        /// matches the pattern used by ApplyTerrainsFromPath for the parallel
        /// auto-tile feature, and lets the boot path call the loader unconditionally
        /// for every zone without first probing the disk.
        /// </summary>
        [Test]
        public void ApplyFromPath_MissingFile_ReturnsZeroAndDoesNotThrow()
        {
            string nonexistent = Path.Combine(Application.persistentDataPath,
                "MapOverrides", "definitely_does_not_exist.overlay.json");
            int written = OverlayLoader.ApplyCollisionTagsFromPath(nonexistent, _tagMap, 0, 0);
            Assert.AreEqual(0, written);
        }

        // ════════════════════════════════════════════════════════════════════
        // M1.10 — Multi-tag CSV round-trip + canonicalisation
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Multi-tag CSV must survive a save → reload through the same overlay
        /// JSON pipeline. The schema is unchanged (still <c>string[h,w]</c>) —
        /// only the per-cell payload widens from "*"/"0".."8" to canonical CSV.
        /// </summary>
        [Test]
        public void RoundTrip_MultiTagCsv_PreservesEveryAuthoredCombo()
        {
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            collision.SetTile(new Vector3Int(1, 1, 0), _wallTile);
            collision.SetTile(new Vector3Int(2, 2, 0), _wallTile);
            collision.SetTile(new Vector3Int(3, 3, 0), _wallTile);
            collision.SetTile(new Vector3Int(4, 4, 0), _wallTile);

            _tagMap.Set(new Vector2Int(1, 1), "0");           // legacy single
            _tagMap.Set(new Vector2Int(2, 2), "0,2,5");       // canonical multi
            _tagMap.Set(new Vector2Int(3, 3), "*");           // wildcard
            _tagMap.Set(new Vector2Int(4, 4), "5,2,0,2");     // raw — canonicalised on Set

            _persistence.MarkCellDirty(new Vector3Int(1, 1, 0));
            Assert.IsTrue(_persistence.SaveZone(ZONE));

            string path = TileOverlayPersistence.OverridePathForZone(ZONE);
            var roundTrip = new CollisionTagMap();
            int written = OverlayLoader.ApplyCollisionTagsFromPath(path, roundTrip, 0, 0);
            Assert.AreEqual(4, written, "All four authored cells must round-trip.");

            Assert.AreEqual("0",       roundTrip.Get(new Vector2Int(1, 1)));
            Assert.AreEqual("0,2,5",   roundTrip.Get(new Vector2Int(2, 2)));
            Assert.AreEqual("*",       roundTrip.Get(new Vector2Int(3, 3)));
            Assert.AreEqual("0,2,5",   roundTrip.Get(new Vector2Int(4, 4)),
                "Raw '5,2,0,2' must canonicalise before being persisted; reload sees '0,2,5'.");
        }

        /// <summary>
        /// The on-disk JSON must contain the CSV literal so diffs in version
        /// control remain human-readable and tools can grep tag usage without
        /// instantiating the map.
        /// </summary>
        [Test]
        public void SavedJson_LiterallyContains_CanonicalCsv()
        {
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            collision.SetTile(new Vector3Int(2, 2, 0), _wallTile);
            _tagMap.Set(new Vector2Int(2, 2), "5,2,0,2");
            _persistence.MarkCellDirty(new Vector3Int(2, 2, 0));
            _persistence.SaveZone(ZONE);

            string json = File.ReadAllText(TileOverlayPersistence.OverridePathForZone(ZONE));
            StringAssert.Contains("\"0,2,5\"", json,
                "Multi-tag cells must serialise to their canonical CSV form, " +
                "not the raw author-supplied input.");
        }

        /// <summary>
        /// A single-tag map (M1 legacy) must keep loading unchanged after the
        /// multi-tag schema extension. The parser path is identical — Set()
        /// just routes single digits through Canonicalize which leaves them alone.
        /// </summary>
        [Test]
        public void LegacySingleTagOverlay_LoadsWithoutChange()
        {
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            collision.SetTile(new Vector3Int(0, 0, 0), _wallTile);
            _tagMap.Set(new Vector2Int(0, 0), "4");
            _persistence.MarkCellDirty(new Vector3Int(0, 0, 0));
            _persistence.SaveZone(ZONE);

            var roundTrip = new CollisionTagMap();
            OverlayLoader.ApplyCollisionTagsFromPath(
                TileOverlayPersistence.OverridePathForZone(ZONE), roundTrip, 0, 0);

            Assert.AreEqual("4", roundTrip.Get(new Vector2Int(0, 0)));
        }
    }
}
