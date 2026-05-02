using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;
using Valkur.Gameplay.World.Chunks;

namespace Valkur.Tests.EditMode.Game.World.Chunks
{
    /// <summary>
    /// Phase 2.5 acceptance: the full chunk pipeline assembled — biome
    /// (procedural), provider (DiffOverlay), streamer (radius policy),
    /// painter (Tilemap) — produces visible tiles on a real Unity
    /// Tilemap. Drives the streamer through three focus positions to
    /// prove enter / exit transitions paint and clear correctly.
    ///
    /// This is the "screen" half of Phase 2: Phase 2 proved the brain
    /// (deterministic chunks + diffs + persistence); this test proves
    /// the eyes (the brain's output reaches a Tilemap that Unity can
    /// render).
    /// </summary>
    [TestFixture]
    public class ChunkStreamingEndToEndTests
    {
        private const int ChunkSize  = 4;
        private const int LayerCount = 1;
        private const long Seed      = 42L;

        private GameObject _gridGo;
        private Tilemap _tilemap;
        private DictionaryTileIdTable _idTable;
        private Tile _grass, _dirt;
        private TilemapChunkPainter _painter;
        private DiffOverlayChunkProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("StreamE2EGrid");
            _gridGo.AddComponent<Grid>();
            var tmGo = new GameObject("StreamE2ETilemap");
            tmGo.transform.SetParent(_gridGo.transform);
            _tilemap = tmGo.AddComponent<Tilemap>();
            tmGo.AddComponent<TilemapRenderer>();

            _idTable = new DictionaryTileIdTable();
            _idTable.Register("grass");
            _idTable.Register("dirt");
            _grass = ScriptableObject.CreateInstance<Tile>(); _grass.name = "grass";
            _dirt  = ScriptableObject.CreateInstance<Tile>(); _dirt.name  = "dirt";

            var biome  = new NoiseSplitBiome("split", "grass", "dirt");
            var router = new SingleBiomeRouter(biome);
            _provider = new DiffOverlayChunkProvider(
                router, new EmptyDeltaSource(),
                worldSeed: Seed, chunkSize: ChunkSize, layerCount: LayerCount, tiles: _idTable);

            var resolver = new TileIdTableResolver(_idTable, NameLookup);
            _painter = new TilemapChunkPainter(new[] { _tilemap }, resolver, ChunkSize);
        }

        [TearDown]
        public void TearDown()
        {
            if (_gridGo != null) Object.DestroyImmediate(_gridGo);
            if (_grass  != null) Object.DestroyImmediate(_grass);
            if (_dirt   != null) Object.DestroyImmediate(_dirt);
        }

        private TileBase NameLookup(string n) => n == "grass" ? (TileBase)_grass
                                              : n == "dirt"  ? (TileBase)_dirt
                                              : null;

        // Count painted (non-null) cells inside the rectangle that the
        // Chebyshev radius would cover at the given focus.
        private int CountPaintedInsideRadius(int focusCx, int focusCy, int radius)
        {
            int painted = 0;
            int minX = (focusCx - radius) * ChunkSize;
            int minY = (focusCy - radius) * ChunkSize;
            int maxX = (focusCx + radius + 1) * ChunkSize;
            int maxY = (focusCy + radius + 1) * ChunkSize;
            for (int y = minY; y < maxY; y++)
                for (int x = minX; x < maxX; x++)
                    if (_tilemap.GetTile(new Vector3Int(x, y, 0)) != null) painted++;
            return painted;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void Pipeline_AtFocusZero_PaintsAllChunksInRadius()
        {
            var streamer = new ChunkStreamer(_provider, _painter, activeRadius: 1);
            streamer.SyncTo(new ChunkCoord(WorldId.Base, 0, 0));

            // Radius 1 -> 3x3 chunks visible. Each chunk 4x4 -> 16 cells.
            // NoiseSplitBiome paints both grass and dirt: every cell holds
            // ONE of the two real tiles, so painted-count == total cells.
            int totalCells = 9 * (ChunkSize * ChunkSize);
            int painted = CountPaintedInsideRadius(0, 0, radius: 1);
            Assert.AreEqual(totalCells, painted,
                "Every cell of every chunk in the active radius must hold a " +
                "non-null tile after the streamer's first SyncTo — proves the " +
                "biome -> provider -> streamer -> painter -> Tilemap chain works " +
                "end-to-end on a real Unity Tilemap.");
        }

        [Test]
        public void Pipeline_FocusMoves_OldChunksAreCleared()
        {
            var streamer = new ChunkStreamer(_provider, _painter, activeRadius: 1);

            // Activate the 3x3 block at (0,0).
            streamer.SyncTo(new ChunkCoord(WorldId.Base, 0, 0));

            // Move focus far enough that the old block leaves the radius
            // entirely — chunk (0,0) should be cleared.
            streamer.SyncTo(new ChunkCoord(WorldId.Base, 10, 0));

            // Cells inside the chunk (0,0) footprint must now be null
            // (the old block was Hidden, which clears).
            int phantomCells = 0;
            for (int y = 0; y < ChunkSize; y++)
                for (int x = 0; x < ChunkSize; x++)
                    if (_tilemap.GetTile(new Vector3Int(x, y, 0)) != null) phantomCells++;

            Assert.AreEqual(0, phantomCells,
                "When focus moves out of a chunk's radius, the painter must " +
                "clear that chunk so the player sees fresh terrain instead of " +
                "ghost tiles from the old position.");
        }

        [Test]
        public void Pipeline_HideAll_LeavesTilemapBlank()
        {
            var streamer = new ChunkStreamer(_provider, _painter, activeRadius: 2);
            streamer.SyncTo(new ChunkCoord(WorldId.Base, 0, 0));
            int beforeHide = CountPaintedInsideRadius(0, 0, radius: 2);
            Assert.Greater(beforeHide, 0, "Sanity: streamer painted some tiles before HideAll.");

            streamer.HideAll();

            int afterHide = CountPaintedInsideRadius(0, 0, radius: 2);
            Assert.AreEqual(0, afterHide,
                "HideAll clears the Tilemap so a world swap does not leak " +
                "the previous dimension's chunks into the next one.");
        }
    }
}
