using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.Buildings;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Integration tests that exercise the full FILL TOOL — OPTIONS pipeline end-to-end:
    ///
    ///   raw flood-fill cells
    ///     → BuildingsFillPlacementStrategy (Uniform / Groves / Noise)
    ///     → BuildingsFillSpacingFilter
    ///     → BuildingsFillSizeCalculator (per accepted cell)
    ///
    /// These tests do not require a live BuildingsRuntimeEditor instance; they
    /// reproduce the same composition that <c>UpdateFillHover</c> + <c>CommitFill</c>
    /// perform inside the editor, on a synthetic tilemap.
    /// </summary>
    [TestFixture]
    public class BuildingsFillPipelineIntegrationTests
    {
        // ── Test rig ──────────────────────────────────────────────────────────────

        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();
        }

        /// <summary>Grid + Tilemap with cellSize=(1,1,0) so 1 cell == 1 world unit.</summary>
        private Tilemap CreateTilemap()
        {
            var gridGo = new GameObject("Grid");
            _sceneObjects.Add(gridGo);
            var grid = gridGo.AddComponent<Grid>();
            grid.cellSize = Vector3.one;

            var tmGo = new GameObject("Tilemap");
            tmGo.transform.SetParent(gridGo.transform, false);
            _sceneObjects.Add(tmGo);
            return tmGo.AddComponent<Tilemap>();
        }

        /// <summary>Build an N×N grid of cells.</summary>
        private static HashSet<Vector3Int> MakeGrid(int size)
        {
            var set = new HashSet<Vector3Int>();
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    set.Add(new Vector3Int(x, y, 0));
            return set;
        }

        /// <summary>The full pipeline as run inside CommitFill.</summary>
        private static (List<Vector3Int> cells, List<Vector2Int> sizes) RunPipeline(
            HashSet<Vector3Int> rawCells,
            int spacingTiles,
            BuildingsFillOptionsValidator__Mode mode,
            int groveCount, float groveSpread,
            float noiseScale, float noiseThreshold,
            bool randomSize, int sizeMinPct, int sizeMaxPct,
            Vector2Int templateScale,
            int seed,
            Tilemap tilemap)
        {
            // 1. Strategy
            HashSet<Vector3Int> postStrategy;
            Dictionary<Vector3Int, float> sizeHints = null;
            switch (mode)
            {
                case BuildingsFillOptionsValidator__Mode.Groves:
                {
                    var r = BuildingsFillPlacementStrategy.ApplyGroves(
                        rawCells, groveCount, groveSpread, seed);
                    postStrategy = r.cells;
                    if (randomSize) sizeHints = r.sizeHints;
                    break;
                }
                case BuildingsFillOptionsValidator__Mode.Noise:
                    postStrategy = BuildingsFillPlacementStrategy.ApplyNoise(
                        rawCells, noiseScale, noiseThreshold, seed);
                    break;
                default:
                    postStrategy = BuildingsFillPlacementStrategy.ApplyUniform(rawCells);
                    break;
            }

            // 2. Spacing filter (no pre-existing buildings).
            var accepted = BuildingsFillSpacingFilter.Apply(
                postStrategy, spacingTiles, tilemap, new List<Vector2>());

            // 3. Size calculator (uses a single seeded RNG, just like CommitFill).
            var rng = new System.Random(seed);
            var sizes = new List<Vector2Int>(accepted.Count);
            foreach (var cell in accepted)
            {
                float? hint = null;
                if (sizeHints != null && sizeHints.TryGetValue(cell, out float h))
                    hint = h;
                sizes.Add(BuildingsFillSizeCalculator.ComputeScaleOverride(
                    randomSize, sizeMinPct, sizeMaxPct, templateScale, hint, rng));
            }

            return (accepted, sizes);
        }

        // ── Local enum mirroring BuildingsRuntimeEditor.FillPlacementMode ─────────
        // (the editor's enum is private — re-declared here so tests don't need
        // accessor changes to a runtime singleton).
        private enum BuildingsFillOptionsValidator__Mode { Uniform, Groves, Noise }

        // ═════════════════════════════════════════════════════════════════════════
        //  Uniform mode
        // ═════════════════════════════════════════════════════════════════════════

        [Test]
        public void Pipeline_UniformMode_AllCellsReachSpacingFilter()
        {
            var tm = CreateTilemap();
            var grid = MakeGrid(10);

            // spacing=1 → no rejection by spacing filter; uniform passes everything.
            var (accepted, _) = RunPipeline(
                grid, spacingTiles: 1,
                mode: BuildingsFillOptionsValidator__Mode.Uniform,
                groveCount: 0, groveSpread: 0f,
                noiseScale: 0f, noiseThreshold: 0f,
                randomSize: false, sizeMinPct: 100, sizeMaxPct: 100,
                templateScale: new Vector2Int(64, 64),
                seed: 1, tilemap: tm);

            Assert.That(accepted.Count, Is.EqualTo(grid.Count),
                "Uniform mode + spacing=1 must accept every input cell.");
        }

        [Test]
        public void Pipeline_UniformMode_SpacingThreeProducesSubgrid()
        {
            var tm = CreateTilemap();
            var grid = MakeGrid(10); // 100 cells

            var (accepted, _) = RunPipeline(
                grid, spacingTiles: 3,
                mode: BuildingsFillOptionsValidator__Mode.Uniform,
                groveCount: 0, groveSpread: 0f,
                noiseScale: 0f, noiseThreshold: 0f,
                randomSize: false, sizeMinPct: 100, sizeMaxPct: 100,
                templateScale: new Vector2Int(64, 64),
                seed: 1, tilemap: tm);

            // Greedy 3-tile spacing on a 10×10 grid gives roughly (10/3)² ≈ 11–16 cells.
            Assert.That(accepted.Count, Is.LessThan(grid.Count));
            Assert.That(accepted.Count, Is.GreaterThan(0));
        }

        // ═════════════════════════════════════════════════════════════════════════
        //  Groves mode
        // ═════════════════════════════════════════════════════════════════════════

        [Test]
        public void Pipeline_GrovesMode_RejectsCellsFarFromClusters()
        {
            var tm = CreateTilemap();
            var grid = MakeGrid(40); // 1600 cells

            var (accepted, _) = RunPipeline(
                grid, spacingTiles: 1,
                mode: BuildingsFillOptionsValidator__Mode.Groves,
                groveCount: 2, groveSpread: 4f, // small spread → tight clusters
                noiseScale: 0f, noiseThreshold: 0f,
                randomSize: false, sizeMinPct: 100, sizeMaxPct: 100,
                templateScale: new Vector2Int(64, 64),
                seed: 7, tilemap: tm);

            // Tight clusters on a large grid → most cells rejected.
            Assert.That(accepted.Count, Is.LessThan(grid.Count / 4),
                $"Expected Groves to reject >75% of cells (1600), got {accepted.Count} accepted.");
            Assert.That(accepted.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Pipeline_GrovesPlusRandomSize_HintsProduceSizeVariation()
        {
            var tm = CreateTilemap();
            var grid = MakeGrid(30);

            var (accepted, sizes) = RunPipeline(
                grid, spacingTiles: 1,
                mode: BuildingsFillOptionsValidator__Mode.Groves,
                groveCount: 3, groveSpread: 6f,
                noiseScale: 0f, noiseThreshold: 0f,
                randomSize: true, sizeMinPct: 50, sizeMaxPct: 200,
                templateScale: new Vector2Int(64, 64),
                seed: 11, tilemap: tm);

            Assert.That(accepted.Count, Is.GreaterThan(5),
                "Need a reasonable sample size to assert variation.");

            // Verify sizes are NOT all the same — Groves+RandomSize should produce variety.
            var distinctWidths = new HashSet<int>();
            foreach (var s in sizes) distinctWidths.Add(s.x);
            Assert.That(distinctWidths.Count, Is.GreaterThan(3),
                "Expected per-tree size variation — got identical sizes everywhere.");

            // Every produced size must respect [50%, 200%] bounds for a 64×64 template:
            // → [32, 128] pixels, with rounding.
            foreach (var s in sizes)
            {
                Assert.That(s.x, Is.InRange(31, 129));
                Assert.That(s.y, Is.InRange(31, 129));
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        //  Noise mode
        // ═════════════════════════════════════════════════════════════════════════

        [Test]
        public void Pipeline_NoiseMode_HighThreshold_AcceptsFewCells()
        {
            var tm = CreateTilemap();
            var grid = MakeGrid(50); // 2500 cells

            var (accepted, _) = RunPipeline(
                grid, spacingTiles: 1,
                mode: BuildingsFillOptionsValidator__Mode.Noise,
                groveCount: 0, groveSpread: 0f,
                noiseScale: 0.15f, noiseThreshold: 0.85f,
                randomSize: false, sizeMinPct: 100, sizeMaxPct: 100,
                templateScale: new Vector2Int(64, 64),
                seed: 13, tilemap: tm);

            Assert.That(accepted.Count, Is.LessThan(grid.Count / 5),
                $"Noise threshold=0.85 should reject most cells; got {accepted.Count}/2500.");
        }

        [Test]
        public void Pipeline_NoiseMode_LowThreshold_KeepsMostCells()
        {
            var tm = CreateTilemap();
            var grid = MakeGrid(20);

            var (accepted, _) = RunPipeline(
                grid, spacingTiles: 1,
                mode: BuildingsFillOptionsValidator__Mode.Noise,
                groveCount: 0, groveSpread: 0f,
                noiseScale: 0.15f, noiseThreshold: 0.05f,
                randomSize: false, sizeMinPct: 100, sizeMaxPct: 100,
                templateScale: new Vector2Int(64, 64),
                seed: 13, tilemap: tm);

            Assert.That(accepted.Count, Is.GreaterThan(grid.Count * 80 / 100),
                $"Noise threshold=0.05 should keep most cells; got {accepted.Count}/400.");
        }

        // ═════════════════════════════════════════════════════════════════════════
        //  Determinism + ordering invariants
        // ═════════════════════════════════════════════════════════════════════════

        [Test]
        public void Pipeline_SameSeed_ProducesIdenticalResult()
        {
            var tm = CreateTilemap();
            var grid = MakeGrid(20);

            var (cellsA, sizesA) = RunPipeline(
                grid, 2,
                BuildingsFillOptionsValidator__Mode.Groves,
                3, 6f, 0f, 0f,
                true, 70, 130,
                new Vector2Int(64, 64),
                seed: 42, tilemap: tm);

            var (cellsB, sizesB) = RunPipeline(
                grid, 2,
                BuildingsFillOptionsValidator__Mode.Groves,
                3, 6f, 0f, 0f,
                true, 70, 130,
                new Vector2Int(64, 64),
                seed: 42, tilemap: tm);

            Assert.That(cellsB, Is.EqualTo(cellsA), "Same seed → same accepted cells.");
            Assert.That(sizesB, Is.EqualTo(sizesA), "Same seed → same per-cell sizes.");
        }

        [Test]
        public void Pipeline_StrategyRunsBeforeSpacing()
        {
            // Verify ordering: the strategy must subsample BEFORE the spacing filter sees
            // the cells. A pathological Noise threshold (rejects everything) should produce
            // an empty result regardless of spacing.
            var tm = CreateTilemap();
            var grid = MakeGrid(20);

            var (accepted, _) = RunPipeline(
                grid, spacingTiles: 1,
                mode: BuildingsFillOptionsValidator__Mode.Noise,
                groveCount: 0, groveSpread: 0f,
                noiseScale: 0.5f, noiseThreshold: 1f, // threshold==1 → noise > 1 never true
                randomSize: false, sizeMinPct: 100, sizeMaxPct: 100,
                templateScale: new Vector2Int(64, 64),
                seed: 1, tilemap: tm);

            Assert.That(accepted, Is.Empty,
                "Strategy must run first — a fully-rejecting Noise filter must yield zero output.");
        }

        [Test]
        public void Pipeline_SpacingTwo_RejectsAdjacentAcceptances()
        {
            var tm = CreateTilemap();
            var grid = MakeGrid(6);

            var (accepted, _) = RunPipeline(
                grid, spacingTiles: 2,
                mode: BuildingsFillOptionsValidator__Mode.Uniform,
                groveCount: 0, groveSpread: 0f,
                noiseScale: 0f, noiseThreshold: 0f,
                randomSize: false, sizeMinPct: 100, sizeMaxPct: 100,
                templateScale: new Vector2Int(64, 64),
                seed: 1, tilemap: tm);

            // Verify pairwise distance ≥ 2 (the spacing in tiles == world units).
            for (int i = 0; i < accepted.Count; i++)
            {
                for (int j = i + 1; j < accepted.Count; j++)
                {
                    var a = tm.GetCellCenterWorld(accepted[i]);
                    var b = tm.GetCellCenterWorld(accepted[j]);
                    float d = Vector2.Distance(a, b);
                    // NUnit's .Within() only attaches to Is.EqualTo. For a
                    // ≥-with-epsilon comparison, subtract the tolerance
                    // directly from the bound — semantically identical.
                    Assert.That(d, Is.GreaterThanOrEqualTo(2f - 1e-4f),
                        $"Cells {accepted[i]} and {accepted[j]} are only {d} apart.");
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        //  Random size: distribution across pipeline
        // ═════════════════════════════════════════════════════════════════════════

        [Test]
        public void Pipeline_UniformPlusRandomSize_AllSizesRespectBounds()
        {
            var tm = CreateTilemap();
            var grid = MakeGrid(12);

            var (_, sizes) = RunPipeline(
                grid, spacingTiles: 1,
                mode: BuildingsFillOptionsValidator__Mode.Uniform,
                groveCount: 0, groveSpread: 0f,
                noiseScale: 0f, noiseThreshold: 0f,
                randomSize: true, sizeMinPct: 80, sizeMaxPct: 120,
                templateScale: new Vector2Int(100, 100),
                seed: 99, tilemap: tm);

            // Each axis must be in [80, 120] pixels (with rounding tolerance ±1).
            foreach (var s in sizes)
            {
                Assert.That(s.x, Is.InRange(79, 121));
                Assert.That(s.y, Is.InRange(79, 121));
            }
        }

        [Test]
        public void Pipeline_RandomSizeOff_AllSizesAreZero()
        {
            // Convention: scaleOverride == Vector2Int.zero means "use template's scale".
            var tm = CreateTilemap();
            var grid = MakeGrid(8);

            var (_, sizes) = RunPipeline(
                grid, spacingTiles: 1,
                mode: BuildingsFillOptionsValidator__Mode.Uniform,
                groveCount: 0, groveSpread: 0f,
                noiseScale: 0f, noiseThreshold: 0f,
                randomSize: false, sizeMinPct: 50, sizeMaxPct: 200,
                templateScale: new Vector2Int(100, 100),
                seed: 99, tilemap: tm);

            foreach (var s in sizes)
                Assert.That(s, Is.EqualTo(Vector2Int.zero),
                    "RandomSize OFF must always produce Vector2Int.zero (template-default sentinel).");
        }
    }
}
