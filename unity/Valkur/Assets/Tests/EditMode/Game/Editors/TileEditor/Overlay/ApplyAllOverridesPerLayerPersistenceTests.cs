using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.Editors.TileEditor.Overlay
{
    /// <summary>
    /// End-to-end persistence guard for the per-visual-layer collisions stack.
    /// The earlier tests (<c>CollisionTagPersistenceTests</c>, <c>LayerJumpPersistenceTests</c>)
    /// prove the matrices round-trip in isolation; this fixture proves the
    /// <see cref="TileOverlayPersistence.ApplyAllOverrides"/> world-load path
    /// actually invokes the two <c>OverlayLoader.Apply*FromPath</c> methods so
    /// authored tags + jumps survive a save / reload cycle in production.
    ///
    /// The bug this guards against was: both Apply methods existed but had
    /// zero callers. Saves emitted the matrices; loads silently dropped them.
    /// After F8 → paint → save → reload, every painted collider behaved as
    /// wildcard and every authored layer jump was lost.
    /// </summary>
    [TestFixture]
    public class ApplyAllOverridesPerLayerPersistenceTests
    {
        private const string ZONE = "zone_test_apply_all_overrides_persist";

        private GameObject _gridGo;
        private WorldGridBuilder _grid;
        private GameObject _zoneGo;
        private ZoneManager _zones;
        private GameObject _tileEditorGo;
        private TileEditorManager _tileEditor;
        private Tile _wallTile;

        [SetUp]
        public void SetUp()
        {
            // Defensive: previous test may have left a singleton instance alive.
            if (TileEditorManager.HasInstance)
                Object.DestroyImmediate(TileEditorManager.Instance.gameObject);

            _gridGo = new GameObject("WorldGridBuilder");
            _grid = _gridGo.AddComponent<WorldGridBuilder>();
            _grid.BuildGrid();

            _zoneGo = new GameObject("ZoneManager");
            _zones = _zoneGo.AddComponent<ZoneManager>();
            _zones.AddZone(ZONE, new Vector2Int(0, 0), editableInTileEditor: true);

            // Spawn the singleton — ApplyAllOverrides reads the per-layer maps
            // off of TileEditorManager.Instance.CollisionTags / LayerJumps via
            // the HasInstance guard inside the static loader. In EditMode,
            // AddComponent does not synchronously fire Awake, so force it via
            // reflection (mirrors the pattern in TileEditorIntegrationTests).
            _tileEditorGo = new GameObject("TileEditorManager");
            _tileEditor = _tileEditorGo.AddComponent<TileEditorManager>();
            typeof(TileEditorManager)
                .GetMethod("Awake",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(_tileEditor, null);

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

            // OnSingletonAwake on TileEditorManager spawns LayerJumpTriggerSystem
            // + WorldCollisionBaker via EnsureExists. Destroy those explicitly so
            // their GameObjects don't linger across test fixtures and produce
            // EditMode "Destroy may not be called from edit mode" warnings
            // during later teardown.
            if (LayerJumpTriggerSystem.HasInstance)
                Object.DestroyImmediate(LayerJumpTriggerSystem.Instance.gameObject);
            if (WorldCollisionBaker.HasInstance)
                Object.DestroyImmediate(WorldCollisionBaker.Instance.gameObject);

            if (_tileEditorGo != null) Object.DestroyImmediate(_tileEditorGo);
            Object.DestroyImmediate(_gridGo);
            Object.DestroyImmediate(_zoneGo);
            Object.DestroyImmediate(_wallTile);
            TileRegistry.Instance.Load(null);
        }

        /// <summary>
        /// Author a collision tile + tag + layer-jump in the same zone, persist via
        /// the same <see cref="TileOverlayPersistence"/> instance the editor uses
        /// in production, wipe the in-memory maps, then run
        /// <see cref="TileOverlayPersistence.ApplyAllOverrides"/> as
        /// <see cref="WorldLoader"/> does — the maps must be repopulated from disk.
        ///
        /// Critically uses a separate <see cref="TileOverlayPersistence"/> instance
        /// for save and load to mimic the production flow: the editor saves through
        /// its own persistence instance, then ApplyAllOverrides loads through the
        /// static path which fetches the maps via the TileEditorManager singleton.
        /// </summary>
        [Test]
        public void ApplyAllOverrides_RestoresCollisionTagsAndLayerJumps_AfterReload()
        {
            // Wire the production-flow persistence: it pulls Tags + Jumps off the
            // singleton on save AND on load, so we must mirror the manager's
            // boot wiring (TileEditorManager.cs:213/216).
            var persistence = new TileOverlayPersistence(_zones, _grid);
            persistence.CollisionTagMap = _tileEditor.CollisionTags;
            persistence.LayerJumpMap   = _tileEditor.LayerJumps;

            // Author a collider so MarkCellDirty flushes the zone, plus tags + jumps
            // so the per-layer matrices have something to emit.
            var collision = _grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            collision.SetTile(new Vector3Int(3, 4, 0), _wallTile);

            _tileEditor.CollisionTags.Set(new Vector2Int(3, 4), "2");
            _tileEditor.LayerJumps.Set(new Vector2Int(7, 8), "5");

            persistence.MarkCellDirty(new Vector3Int(3, 4, 0));
            Assert.IsTrue(persistence.SaveZone(ZONE),
                "Save must succeed — required to produce the override JSON ApplyAllOverrides will read.");

            string overridePath = TileOverlayPersistence.OverridePathForZone(ZONE);
            Assert.IsTrue(File.Exists(overridePath), "Override file must exist on disk.");

            // Wipe in-memory maps to simulate a fresh world-load with no prior state.
            _tileEditor.CollisionTags.ClearAll();
            _tileEditor.LayerJumps.ClearAll();
            Assert.AreEqual(0, _tileEditor.CollisionTags.Count,
                "Pre-condition: tag map must be empty before ApplyAllOverrides runs.");
            Assert.AreEqual(0, _tileEditor.LayerJumps.Count,
                "Pre-condition: jump map must be empty before ApplyAllOverrides runs.");

            // Execute the production load path. ApplyAllOverrides should refill
            // both maps from the override JSON we just wrote.
            int appliedZones = TileOverlayPersistence.ApplyAllOverrides(_grid, _zones);
            Assert.AreEqual(1, appliedZones, "ApplyAllOverrides must apply the one zone we authored.");

            Assert.AreEqual("2", _tileEditor.CollisionTags.Get(new Vector2Int(3, 4)),
                "Collision tag must round-trip through ApplyAllOverrides.");
            Assert.AreEqual("5", _tileEditor.LayerJumps.Get(new Vector2Int(7, 8)),
                "Layer jump must round-trip through ApplyAllOverrides.");
        }

        /// <summary>
        /// Pre-feature overlay JSONs have no <c>collisionTags</c> / <c>layerJumps</c>
        /// matrices. <see cref="TileOverlayPersistence.ApplyAllOverrides"/> must
        /// load them without throwing and leave both maps empty — runtime then
        /// defaults to wildcard collisions + no jumps, which is the pre-feature
        /// behaviour preserved across world updates.
        /// </summary>
        [Test]
        public void ApplyAllOverrides_LegacyOverlayWithoutMatrices_LeavesMapsEmpty()
        {
            // Build a minimal "legacy-shape" JSON: layers only, no tags / no jumps.
            // Mirrors the file Python's overlay exporter (and pre-M1 Unity saves) wrote.
            string legacyJson =
                "{\n" +
                "  \"layers\": {\n" +
                "    \"Ground\": [\n" +
                "      [\"\"]\n" +
                "    ]\n" +
                "  }\n" +
                "}";
            string dir = TileOverlayPersistence.OverrideDirectory;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = TileOverlayPersistence.OverridePathForZone(ZONE);
            File.WriteAllText(path, legacyJson);

            int appliedZones = TileOverlayPersistence.ApplyAllOverrides(_grid, _zones);
            Assert.AreEqual(1, appliedZones,
                "Legacy overlay still applies — only the per-layer matrices are missing.");

            Assert.AreEqual(0, _tileEditor.CollisionTags.Count,
                "Missing collisionTags field must leave the map empty (→ wildcard everywhere).");
            Assert.AreEqual(0, _tileEditor.LayerJumps.Count,
                "Missing layerJumps field must leave the map empty (→ no triggers fire).");
        }
    }
}
