using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Covers <see cref="ParticleBoundsHandles"/> — the geometry behind the F1 editor's resize
    /// handles: which edge the cursor is on, and what dragging it does to the instance's size.
    ///
    /// This is the whole testable surface of the feature by design. The interaction around it
    /// is a state machine driven by a live mouse over a live scene, which a test can only
    /// approximate; the arithmetic is where the defects are — a ratio taken against a floored
    /// extent, an edge that grows the wrong way, an emitter that fails to slide when its
    /// opposite edge is pinned, a reach solve that divides by zero on a preset whose particles
    /// do not move. Every one of those is reachable from here.
    /// </summary>
    [TestFixture]
    public class ParticleBoundsHandlesTests
    {
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Fixtures ─────────────────────────────────────────────────────────────

        /// <summary>A 2 x 1 spawn box that does not move, so the emission geometry is exact.</summary>
        private ParticlePresetDefinition BoxPreset()
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _created.Add(def);
            def.id = "bounds_test_box";
            def.displayName = def.id;
            def.vfx = new ParticleVfxParams
            {
                kind = "aura",
                loops = true,
                spawnWidth = 2f,
                spawnHeight = 1f,
                radius = 0.5f,
                directionDegrees = -1f,
                speed = 0f,
                lifespan = 1f,
                sizeMin = 0f,
                sizeMax = 0f,
                gravity = 0f,
                useGravityVector = false,
                noiseEnabled = false,
                swayAmp = 0f,
            };
            def.layers = new List<ParticlePresetDefinition>();
            return def;
        }

        /// <summary>Same box, with particles that are thrown outward — so it has a reach.</summary>
        private ParticlePresetDefinition MovingPreset()
        {
            var def = BoxPreset();
            def.id = "bounds_test_moving";
            def.vfx.speed = 1f;
            def.vfx.lifespan = 2f;
            return def;
        }

        // ── Edge picking ─────────────────────────────────────────────────────────

        [Test]
        public void PickEdge_TakesTheNearestSideWithinTolerance()
        {
            var box = ParticleFootprint.Rect(1f, 0.5f);

            Assert.AreEqual(ParticleBoundsEdge.Right,
                ParticleBoundsHandles.PickEdge(box, Vector2.zero, new Vector2(0.96f, 0f), 0.1f));
            Assert.AreEqual(ParticleBoundsEdge.Left,
                ParticleBoundsHandles.PickEdge(box, Vector2.zero, new Vector2(-1.04f, 0f), 0.1f));
            Assert.AreEqual(ParticleBoundsEdge.Top,
                ParticleBoundsHandles.PickEdge(box, Vector2.zero, new Vector2(0f, 0.52f), 0.1f));
            Assert.AreEqual(ParticleBoundsEdge.Bottom,
                ParticleBoundsHandles.PickEdge(box, Vector2.zero, new Vector2(0f, -0.48f), 0.1f));
        }

        [Test]
        public void PickEdge_IgnoresTheMiddleOfTheBox()
        {
            var box = ParticleFootprint.Rect(1f, 0.5f);

            Assert.AreEqual(ParticleBoundsEdge.None,
                ParticleBoundsHandles.PickEdge(box, Vector2.zero, Vector2.zero, 0.1f),
                "The interior is not a handle — dragging from it would be an ambiguous gesture " +
                "with the move-the-emitter drag that already lives on this cursor.");
        }

        [Test]
        public void PickEdge_DoesNotOfferAnEdgesInfiniteExtension()
        {
            var box = ParticleFootprint.Rect(1f, 0.5f);

            // On the line x = 1, but a long way above the box.
            Assert.AreEqual(ParticleBoundsEdge.None,
                ParticleBoundsHandles.PickEdge(box, Vector2.zero, new Vector2(1f, 4f), 0.1f));
        }

        [Test]
        public void PickEdge_FollowsTheBoxesOffsetCentre()
        {
            // A drifting field's box hangs below its emitter; its top edge is not at +hh.
            var box = ParticleFootprint.Rect(new Vector2(0f, -2f), 1f, 0.5f);

            Assert.AreEqual(ParticleBoundsEdge.Top,
                ParticleBoundsHandles.PickEdge(box, Vector2.zero, new Vector2(0f, -1.5f), 0.1f));
            Assert.AreEqual(ParticleBoundsEdge.None,
                ParticleBoundsHandles.PickEdge(box, Vector2.zero, new Vector2(0f, 0.5f), 0.1f));
        }

        [Test]
        public void PickBox_PrefersTheEmissionBoxWhenBothAreUnderTheCursor()
        {
            var emission = ParticleFootprint.Rect(1f, 1f);
            var reach = ParticleFootprint.Rect(1.02f, 1.02f);

            Assert.AreEqual(ParticleBoundsBox.Emission,
                ParticleBoundsHandles.PickBox(emission, reach, Vector2.zero, new Vector2(1f, 0f), 0.1f),
                "The reach box is the larger of the two and passes under the cursor far more " +
                "often; letting it win a tie would make the inner handles unreachable.");
        }

        [Test]
        public void PickBox_TakesTheReachBoxWhenOnlyItIsUnderTheCursor()
        {
            var emission = ParticleFootprint.Rect(1f, 1f);
            var reach = ParticleFootprint.Rect(3f, 3f);

            Assert.AreEqual(ParticleBoundsBox.Reach,
                ParticleBoundsHandles.PickBox(emission, reach, Vector2.zero, new Vector2(3f, 0f), 0.1f));
            Assert.AreEqual(ParticleBoundsBox.None,
                ParticleBoundsHandles.PickBox(emission, reach, Vector2.zero, new Vector2(2f, 0f), 0.1f));
        }

        // ── Dragging the emission box ────────────────────────────────────────────

        [Test]
        public void DragEmissionEdge_PinsTheOppositeSide_AndSlidesTheEmitter()
        {
            var preset = BoxPreset();          // half extents 1 x 0.5

            var drag = ParticleBoundsHandles.DragEmissionEdge(
                preset, 1f, ParticleInstanceOverrides.None,
                ParticleBoundsEdge.Right, Vector2.zero, new Vector2(3f, 0f),
                symmetric: false, snap: 0f);

            Assert.IsTrue(drag.Changed);
            Assert.AreEqual(2f, drag.Overrides.spawnScaleX, 1e-4f,
                "Left edge pinned at -1, right edge dragged to 3: the box is now 4 wide " +
                "against a preset that is 2.");
            Assert.AreEqual(1f, drag.OriginDelta.x, 1e-4f,
                "A box centred on its emitter can only keep one edge still by moving the " +
                "emitter half the growth.");
            Assert.AreEqual(1f, drag.Overrides.spawnScaleY, 1e-4f, "The other axis is untouched.");
        }

        [Test]
        public void DragEmissionEdge_Symmetric_GrowsBothSides_AndLeavesTheEmitter()
        {
            var preset = BoxPreset();

            var drag = ParticleBoundsHandles.DragEmissionEdge(
                preset, 1f, ParticleInstanceOverrides.None,
                ParticleBoundsEdge.Right, Vector2.zero, new Vector2(3f, 0f),
                symmetric: true, snap: 0f);

            Assert.AreEqual(3f, drag.Overrides.spawnScaleX, 1e-4f);
            Assert.AreEqual(Vector2.zero, drag.OriginDelta);
        }

        [Test]
        public void DragEmissionEdge_KeepsFollowingTheCursorWhenTheBoxIsTiny()
        {
            // The shrink bug: the drawn box used to floor at 0.22 while the emission carried on
            // down, so past that point the handle stopped tracking the cursor and the ratios
            // went wherever the floor put them.
            var preset = BoxPreset();          // half extents 1 x 0.5
            var current = new ParticleInstanceOverrides(0.08f, 1f, 1f);   // half-width 0.08

            var drag = ParticleBoundsHandles.DragEmissionEdge(
                preset, 1f, current, ParticleBoundsEdge.Right, Vector2.zero,
                new Vector2(0.1f, 0f), symmetric: true, snap: 0f);

            Assert.AreEqual(0.1f, drag.Overrides.spawnScaleX, 1e-4f,
                "A base half-width of 1 and an edge at 0.1 is a ratio of 0.1. It used to stop " +
                "following the cursor here, because the drawn box floored at 0.22 and the " +
                "handle was picked off that floor.");
        }

        [Test]
        public void DragEmissionEdge_MeasuresAgainstTheUnpaddedPreset()
        {
            // A leaf strip is 0.1 units tall. Ratios are taken against the RAW emission extent,
            // never against a padded or floored one — otherwise this drag would resize the
            // strip several times too slowly.
            var preset = BoxPreset();
            preset.vfx.kind = "falling_leaf";
            preset.vfx.spawnWidth = 0f;
            preset.vfx.spawnHeight = 0f;

            var drag = ParticleBoundsHandles.DragEmissionEdge(
                preset, 1f, ParticleInstanceOverrides.None,
                ParticleBoundsEdge.Top, Vector2.zero, new Vector2(0f, 0.05f),
                symmetric: true, snap: 0f);

            Assert.AreEqual(1f, drag.Overrides.spawnScaleY, 1e-3f,
                "Dragging the top edge to exactly where the strip already ends must be a " +
                "no-op ratio, not a fourfold shrink.");
        }

        [Test]
        public void DragEmissionEdge_Snaps_ToTheArtTexelGrid()
        {
            var preset = BoxPreset();

            var drag = ParticleBoundsHandles.DragEmissionEdge(
                preset, 1f, ParticleInstanceOverrides.None,
                ParticleBoundsEdge.Right, Vector2.zero, new Vector2(2.97f, 0f),
                symmetric: true, snap: 0.25f);

            Assert.AreEqual(3f, drag.Overrides.spawnScaleX, 1e-4f,
                "2.97 snaps to 3.00 on a quarter-unit grid before the ratio is taken.");
        }

        [Test]
        public void DragEmissionEdge_Clamps_AndStopsSlidingTheEmitterWhenItDoes()
        {
            var preset = BoxPreset();

            var drag = ParticleBoundsHandles.DragEmissionEdge(
                preset, 1f, ParticleInstanceOverrides.None,
                ParticleBoundsEdge.Right, Vector2.zero, new Vector2(500f, 0f),
                symmetric: false, snap: 0f);

            Assert.AreEqual(ParticleInstanceOverrides.MaxRatio, drag.Overrides.spawnScaleX, 1e-4f);
            Assert.AreEqual(Vector2.zero, drag.OriginDelta,
                "Once the size stops following the cursor the emitter must stop too, or it " +
                "keeps walking across the map while the box stays put.");

        }

        [Test]
        public void DragEmissionEdge_DraggedPastItsOppositeEdge_StopsAtTheMinimumInsteadOfFlipping()
        {
            // Pull the right edge left, past the left one. |target - pinned| keeps growing on
            // the far side, so without a stop the box turned inside out: the field grew
            // leftward and the emitter jumped with it.
            var preset = BoxPreset();          // half extents 1 x 0.5, left edge at -1

            var drag = ParticleBoundsHandles.DragEmissionEdge(
                preset, 1f, ParticleInstanceOverrides.None,
                ParticleBoundsEdge.Right, Vector2.zero, new Vector2(-5f, 0f),
                symmetric: false, snap: 0f);

            Assert.AreEqual(ParticleInstanceOverrides.MinRatio, drag.Overrides.spawnScaleX, 1e-4f,
                "The smallest legal size, not a wider one on the wrong side of the pin.");

            float newHalf = ParticleFootprint.EmissionHalfExtents(preset, 1f, drag.Overrides).x;
            float pinnedEdge = drag.OriginDelta.x - newHalf;
            Assert.AreEqual(-1f, pinnedEdge, 1e-3f,
                "And the edge that was pinned is still exactly where it was.");
        }

        [Test]
        public void DragEmissionEdge_ScalesWithTheInstanceScaleMultiplier()
        {
            var preset = BoxPreset();          // 2 x 1 at scale 1, so 4 x 2 at scale 2

            var drag = ParticleBoundsHandles.DragEmissionEdge(
                preset, 2f, ParticleInstanceOverrides.None,
                ParticleBoundsEdge.Right, Vector2.zero, new Vector2(4f, 0f),
                symmetric: true, snap: 0f);

            Assert.AreEqual(2f, drag.Overrides.spawnScaleX, 1e-4f,
                "At scale 2 the preset's own half-width is 2, so an edge at 4 is twice it.");
        }

        // ── Dragging the reach box ───────────────────────────────────────────────

        [Test]
        public void DragReachEdge_PutsTheEdgeWhereTheCursorIs()
        {
            var preset = MovingPreset();

            var drag = ParticleBoundsHandles.DragReachEdge(
                preset, 1f, ParticleInstanceOverrides.None,
                ParticleBoundsEdge.Right, Vector2.zero, new Vector2(6f, 0f), snap: 0f);

            Assert.IsTrue(drag.Changed);

            var resized = ParticleFootprint.Of(preset, 1f, drag.Overrides);
            Assert.AreEqual(6f, resized.Max.x, 0.05f,
                "The reach solve fits a line through two sampled ratios; landing the edge on " +
                "the cursor is the whole contract.");
        }

        [Test]
        public void DragReachEdge_ShrinksAsWellAsGrows()
        {
            var preset = MovingPreset();
            var wide = new ParticleInstanceOverrides(1f, 1f, 4f);

            var drag = ParticleBoundsHandles.DragReachEdge(
                preset, 1f, wide, ParticleBoundsEdge.Right, Vector2.zero, new Vector2(2.5f, 0f), snap: 0f);

            Assert.Less(drag.Overrides.reachScale, wide.reachScale);
            var resized = ParticleFootprint.Of(preset, 1f, drag.Overrides);
            Assert.AreEqual(2.5f, resized.Max.x, 0.05f);
        }

        [Test]
        public void DragReachEdge_LeavesTheEmitterWhereItIs()
        {
            var preset = MovingPreset();

            var drag = ParticleBoundsHandles.DragReachEdge(
                preset, 1f, ParticleInstanceOverrides.None,
                ParticleBoundsEdge.Left, Vector2.zero, new Vector2(-6f, 0f), snap: 0f);

            Assert.AreEqual(Vector2.zero, drag.OriginDelta,
                "Reach grows outward in every direction at once, so there is no opposite edge " +
                "to pin and nothing to compensate for by moving the emitter.");
        }

        [Test]
        public void DragReachEdge_OnAPresetWhoseParticlesDoNotMove_ChangesNothing()
        {
            var preset = BoxPreset();          // speed 0, no drift, no noise

            var drag = ParticleBoundsHandles.DragReachEdge(
                preset, 1f, ParticleInstanceOverrides.None,
                ParticleBoundsEdge.Right, Vector2.zero, new Vector2(9f, 0f), snap: 0f);

            Assert.IsFalse(drag.Changed,
                "With no motion terms the reach box is the emission box at every ratio; " +
                "solving for one would divide by ~0 and hand back a ratio of thousands.");
            Assert.AreEqual(1f, drag.Overrides.reachScale, 1e-4f);
        }

        [Test]
        public void DragReachEdge_KeepsTheEmissionRatiosItWasGiven()
        {
            var preset = MovingPreset();
            var current = new ParticleInstanceOverrides(1.5f, 0.75f, 1f);

            var drag = ParticleBoundsHandles.DragReachEdge(
                preset, 1f, current, ParticleBoundsEdge.Top, Vector2.zero, new Vector2(0f, 5f), snap: 0f);

            Assert.AreEqual(1.5f, drag.Overrides.spawnScaleX, 1e-4f);
            Assert.AreEqual(0.75f, drag.Overrides.spawnScaleY, 1e-4f);
        }

        // ── Snap helper ──────────────────────────────────────────────────────────

        [Test]
        public void Snap_RoundsToTheGrid_AndAZeroStepDragsFree()
        {
            Assert.AreEqual(0.25f, ParticleBoundsHandles.Snap(0.27f, 0.0625f), 1e-4f);
            Assert.AreEqual(0.27f, ParticleBoundsHandles.Snap(0.27f, 0f), 1e-4f);
        }
    }
}
