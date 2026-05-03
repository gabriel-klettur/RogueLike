using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Spawners;

namespace Valkur.Tests.EditMode.Editors.Spawners
{
    /// <summary>
    /// Pure-logic tests for <see cref="SpawnerHitTester.FindClosestWithinRadius"/>.
    /// The helper underpins both the Alt-toggle hover affordance and the
    /// quick-inspect click in <c>SpawnerEditorManager</c>; if any of these
    /// assertions break, those interactions silently regress.
    /// </summary>
    [TestFixture]
    public class SpawnerHitTesterTests
    {
        // ── Null / empty / degenerate input ───────────────────────────────────

        [Test]
        public void FindClosest_NullList_ReturnsMinusOne()
        {
            int idx = SpawnerHitTester.FindClosestWithinRadius(null, Vector2.zero, 1f);
            Assert.AreEqual(-1, idx);
        }

        [Test]
        public void FindClosest_EmptyList_ReturnsMinusOne()
        {
            int idx = SpawnerHitTester.FindClosestWithinRadius(new List<Vector2>(), Vector2.zero, 1f);
            Assert.AreEqual(-1, idx);
        }

        [Test]
        public void FindClosest_ZeroRadius_ReturnsMinusOneEvenAtSamePosition()
        {
            var positions = new List<Vector2> { Vector2.zero };
            int idx = SpawnerHitTester.FindClosestWithinRadius(positions, Vector2.zero, 0f);
            Assert.AreEqual(-1, idx,
                "Zero radius is treated as a no-op so callers can disable hit testing without removing the call.");
        }

        [Test]
        public void FindClosest_NegativeRadius_ReturnsMinusOne()
        {
            var positions = new List<Vector2> { Vector2.zero };
            int idx = SpawnerHitTester.FindClosestWithinRadius(positions, Vector2.zero, -1f);
            Assert.AreEqual(-1, idx);
        }

        // ── Single-element happy path ─────────────────────────────────────────

        [Test]
        public void FindClosest_OnePoint_CursorInside_ReturnsZero()
        {
            var positions = new List<Vector2> { new Vector2(5f, 5f) };
            int idx = SpawnerHitTester.FindClosestWithinRadius(positions, new Vector2(5.1f, 5.1f), 1f);
            Assert.AreEqual(0, idx);
        }

        [Test]
        public void FindClosest_OnePoint_CursorOnRadiusBoundary_DoesNotMatch()
        {
            // The implementation uses strict less-than to keep tie behaviour stable —
            // a point exactly at the radius is treated as outside. Documenting that.
            var positions = new List<Vector2> { Vector2.zero };
            int idx = SpawnerHitTester.FindClosestWithinRadius(positions, new Vector2(1f, 0f), 1f);
            Assert.AreEqual(-1, idx,
                "Strict less-than: a point exactly at maxDist is excluded.");
        }

        [Test]
        public void FindClosest_OnePoint_CursorOutside_ReturnsMinusOne()
        {
            var positions = new List<Vector2> { new Vector2(0f, 0f) };
            int idx = SpawnerHitTester.FindClosestWithinRadius(positions, new Vector2(10f, 10f), 1f);
            Assert.AreEqual(-1, idx);
        }

        // ── Multi-element selection ───────────────────────────────────────────

        [Test]
        public void FindClosest_MultiplePoints_ReturnsClosestWithinRadius()
        {
            var positions = new List<Vector2>
            {
                new Vector2(10f, 0f),  // 10 units away
                new Vector2( 2f, 0f),  // 2 units away — closest within 5
                new Vector2( 5f, 5f),  // ~7 units away
            };
            int idx = SpawnerHitTester.FindClosestWithinRadius(positions, Vector2.zero, 5f);
            Assert.AreEqual(1, idx);
        }

        [Test]
        public void FindClosest_MultiplePoints_OnlyOneInsideRadius_ReturnsThatOne()
        {
            var positions = new List<Vector2>
            {
                new Vector2( 10f,  0f),
                new Vector2(  0f, 10f),
                new Vector2(  0.3f, 0f),  // only this one is inside 0.5 radius
            };
            int idx = SpawnerHitTester.FindClosestWithinRadius(positions, Vector2.zero, 0.5f);
            Assert.AreEqual(2, idx);
        }

        [Test]
        public void FindClosest_NoneInsideRadius_ReturnsMinusOne()
        {
            var positions = new List<Vector2>
            {
                new Vector2(10f,  0f),
                new Vector2( 0f, 10f),
                new Vector2(-5f, -5f),
            };
            int idx = SpawnerHitTester.FindClosestWithinRadius(positions, Vector2.zero, 1f);
            Assert.AreEqual(-1, idx);
        }

        [Test]
        public void FindClosest_TiesResolveToFirstIndex()
        {
            // Two points equidistant from the cursor — strict less-than keeps the
            // first match so behaviour is deterministic when the editor places
            // overlapping spawners.
            var positions = new List<Vector2>
            {
                new Vector2(0.2f, 0f),
                new Vector2(0f, 0.2f),
            };
            int idx = SpawnerHitTester.FindClosestWithinRadius(positions, Vector2.zero, 1f);
            Assert.AreEqual(0, idx,
                "Ties must resolve to the first matching index for deterministic selection.");
        }

        // ── Robustness ────────────────────────────────────────────────────────

        [Test]
        public void FindClosest_PositiveInfinitySentinelEntriesAreSkipped()
        {
            // SpawnerEditorManager substitutes Vector2.positiveInfinity for null
            // SpawnerInstance entries. Those must never win the hit test.
            var positions = new List<Vector2>
            {
                Vector2.positiveInfinity,
                new Vector2(0.1f, 0f),
                Vector2.positiveInfinity,
            };
            int idx = SpawnerHitTester.FindClosestWithinRadius(positions, Vector2.zero, 1f);
            Assert.AreEqual(1, idx);
        }
    }
}
