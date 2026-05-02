using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Buildings;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Unit tests for <see cref="BuildingsFillPlacementStrategy"/>.
    ///
    /// All three placement strategies are pure-static and require no scene objects,
    /// so every test can run without a live editor session.
    /// </summary>
    [TestFixture]
    public class BuildingsFillPlacementStrategyTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates a grid of cells from (0,0) to (size-1, size-1).
        /// </summary>
        private static HashSet<Vector3Int> MakeGrid(int size)
        {
            var set = new HashSet<Vector3Int>();
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    set.Add(new Vector3Int(x, y, 0));
            return set;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  ApplyUniform
        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void ApplyUniform_ReturnsAllCells()
        {
            var input = new List<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(2, 0, 0),
                new Vector3Int(3, 0, 0),
                new Vector3Int(4, 0, 0),
            };

            var result = BuildingsFillPlacementStrategy.ApplyUniform(input);

            Assert.AreEqual(5, result.Count,
                "ApplyUniform must return all 5 input cells.");
            foreach (var c in input)
                Assert.IsTrue(result.Contains(c), $"Cell {c} must be in the result.");
        }

        [Test]
        public void ApplyUniform_NullInput_ReturnsEmpty()
        {
            var result = BuildingsFillPlacementStrategy.ApplyUniform(null);

            Assert.IsNotNull(result, "ApplyUniform must never return null.");
            Assert.AreEqual(0, result.Count, "Null input must yield empty result.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  ApplyGroves
        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void ApplyGroves_Deterministic_WithSeed()
        {
            var grid = MakeGrid(20);
            const int seed = 42;

            var resultA = BuildingsFillPlacementStrategy.ApplyGroves(grid, clusterCount: 4, spreadTiles: 5f, seed: seed);
            var resultB = BuildingsFillPlacementStrategy.ApplyGroves(grid, clusterCount: 4, spreadTiles: 5f, seed: seed);

            Assert.AreEqual(resultA.cells.Count, resultB.cells.Count,
                "Same seed must produce the same cell count.");
            Assert.That(resultA.cells, Is.EquivalentTo(resultB.cells),
                "Same seed must produce identical output cell sets.");
        }

        [Test]
        public void ApplyGroves_DifferentSeeds_DifferentOutput()
        {
            // Use a 100×100 grid to make collision between two different seeds extremely unlikely.
            var grid = MakeGrid(100);

            var setA = BuildingsFillPlacementStrategy.ApplyGroves(grid, clusterCount: 5, spreadTiles: 8f, seed: 1).cells;
            var setB = BuildingsFillPlacementStrategy.ApplyGroves(grid, clusterCount: 5, spreadTiles: 8f, seed: 99999).cells;

            // The two outputs will almost certainly differ on a 100×100 grid.
            Assert.That(setA, Is.Not.EquivalentTo(setB),
                "Different seeds should produce different outputs on a large grid.");
        }

        [Test]
        public void ApplyGroves_OutputSubsetOfInput()
        {
            var grid = MakeGrid(30);

            var result = BuildingsFillPlacementStrategy.ApplyGroves(grid, clusterCount: 3, spreadTiles: 6f, seed: 7);

            foreach (var c in result.cells)
                Assert.IsTrue(grid.Contains(c),
                    $"Output cell {c} must be a member of the input set.");
        }

        [Test]
        public void ApplyGroves_HintsInRange01()
        {
            var grid = MakeGrid(30);

            var result = BuildingsFillPlacementStrategy.ApplyGroves(grid, clusterCount: 3, spreadTiles: 6f, seed: 13);

            foreach (var kvp in result.sizeHints)
            {
                Assert.GreaterOrEqual(kvp.Value, 0f,
                    $"Hint for cell {kvp.Key} must be >= 0.");
                Assert.LessOrEqual(kvp.Value, 1f,
                    $"Hint for cell {kvp.Key} must be <= 1.");
            }
        }

        [Test]
        public void ApplyGroves_ZeroClusters_ReturnsEmpty()
        {
            var grid = MakeGrid(20);

            var result = BuildingsFillPlacementStrategy.ApplyGroves(grid, clusterCount: 0, spreadTiles: 5f, seed: 0);

            Assert.AreEqual(0, result.cells.Count,
                "clusterCount = 0 must return an empty cell set.");
        }

        [Test]
        public void ApplyGroves_NullInput_ReturnsEmpty()
        {
            var result = BuildingsFillPlacementStrategy.ApplyGroves(null, clusterCount: 3, spreadTiles: 5f, seed: 0);

            Assert.IsNotNull(result.cells,     "cells must never be null.");
            Assert.IsNotNull(result.sizeHints, "sizeHints must never be null.");
            Assert.AreEqual(0, result.cells.Count, "Null input must yield empty result.");
        }

        [Test]
        public void ApplyGroves_SmallInput_ReturnsCellsWithHintOne()
        {
            // 2 input cells → degenerate path: all returned with hint = 1.
            var input = new List<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
            };

            var result = BuildingsFillPlacementStrategy.ApplyGroves(input, clusterCount: 2, spreadTiles: 4f, seed: 5);

            Assert.AreEqual(2, result.cells.Count, "Degenerate input (≤2 cells) must return all cells.");
            foreach (var c in input)
            {
                Assert.IsTrue(result.cells.Contains(c), $"Cell {c} must be present.");
                Assert.IsTrue(result.sizeHints.ContainsKey(c), $"Cell {c} must have a size hint.");
                Assert.AreEqual(1f, result.sizeHints[c], 0.001f, $"Hint for degenerate cell {c} must be 1.");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  ApplyNoise
        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void ApplyNoise_HighThreshold_RejectsMost()
        {
            // threshold=0.95 over a 50×50 grid (2500 cells).
            // Perlin noise is in [0,1] and only ~5% of samples should exceed 0.95
            // in a well-spread noise field. We allow up to 10% as a conservative bound.
            var grid = MakeGrid(50);
            int total = grid.Count; // 2500

            var result = BuildingsFillPlacementStrategy.ApplyNoise(grid, noiseScale: 0.15f, threshold: 0.95f, seed: 42);

            float acceptRate = (float)result.Count / total;
            Assert.Less(acceptRate, 0.10f,
                $"threshold=0.95 should reject most cells; acceptance rate was {acceptRate:P1}.");
        }

        [Test]
        public void ApplyNoise_LowThreshold_KeepsMost()
        {
            // threshold=0.05 over a 50×50 grid.
            // Nearly all Perlin samples exceed 0.05, so ≥90% should be accepted.
            var grid = MakeGrid(50);
            int total = grid.Count; // 2500

            var result = BuildingsFillPlacementStrategy.ApplyNoise(grid, noiseScale: 0.15f, threshold: 0.05f, seed: 42);

            float acceptRate = (float)result.Count / total;
            Assert.GreaterOrEqual(acceptRate, 0.90f,
                $"threshold=0.05 should keep most cells; acceptance rate was {acceptRate:P1}.");
        }

        [Test]
        public void ApplyNoise_OutputSubsetOfInput()
        {
            var grid = MakeGrid(20);

            var result = BuildingsFillPlacementStrategy.ApplyNoise(grid, noiseScale: 0.20f, threshold: 0.40f, seed: 77);

            foreach (var c in result)
                Assert.IsTrue(grid.Contains(c),
                    $"Output cell {c} must be a member of the input set.");
        }

        [Test]
        public void ApplyNoise_NullInput_ReturnsEmpty()
        {
            var result = BuildingsFillPlacementStrategy.ApplyNoise(null, noiseScale: 0.2f, threshold: 0.4f, seed: 0);

            Assert.IsNotNull(result, "ApplyNoise must never return null.");
            Assert.AreEqual(0, result.Count, "Null input must yield empty result.");
        }

        [Test]
        public void ApplyNoise_ThresholdAtOrAboveOne_ReturnsEmpty()
        {
            var grid = MakeGrid(10);

            var result = BuildingsFillPlacementStrategy.ApplyNoise(grid, noiseScale: 0.2f, threshold: 1f, seed: 0);

            Assert.AreEqual(0, result.Count,
                "threshold >= 1 must reject all cells.");
        }

        [Test]
        public void ApplyNoise_Deterministic_WithSeed()
        {
            var grid = MakeGrid(25);
            const int seed = 123;

            var resultA = BuildingsFillPlacementStrategy.ApplyNoise(grid, noiseScale: 0.18f, threshold: 0.45f, seed: seed);
            var resultB = BuildingsFillPlacementStrategy.ApplyNoise(grid, noiseScale: 0.18f, threshold: 0.45f, seed: seed);

            Assert.That(resultA, Is.EquivalentTo(resultB),
                "Same seed must produce identical output for ApplyNoise.");
        }
    }
}
