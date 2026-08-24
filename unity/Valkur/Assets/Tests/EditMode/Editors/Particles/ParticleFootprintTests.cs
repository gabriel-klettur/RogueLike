using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Covers <see cref="ParticleFootprint"/> — the area the F1 editor draws its outline
    /// around and hit-tests clicks against.
    ///
    /// Three defects in a row taught this type what it has to be:
    ///
    ///  1. One fixed 0.45-unit circle for every preset, describing the emitter's ORIGIN.
    ///  2. Sized to the EMISSION shape, which is where particles are born and not where they
    ///     end up — a leaf field drifts every leaf 1.1 units below its spawn box.
    ///  3. Predicted from the preset alone, which cannot know which speed each particle drew
    ///     from its random range. Over the whole catalog that ran 70% wide on spark presets
    ///     and, when it was tightened, left 34 presets short of their own particles.
    ///
    /// So there are two paths. <see cref="ParticleFootprint.OfLive"/> MEASURES the running
    /// systems and is what the editor draws; <see cref="ParticleFootprint.Of"/> PREDICTS from
    /// the preset and stands in for the frame or two before any particle exists. A prediction
    /// bounds the worst case of every module it models and is generous on purpose —
    /// <c>ParticleFootprintCoverageTests</c> is what holds both to actually containing the
    /// particles, over the whole catalog.
    ///
    /// Every predicted extent below therefore carries <see cref="Padded"/>: the safety margin
    /// the prediction adds because each of its terms is a model of a Unity module rather than
    /// the module itself.
    /// </summary>
    [TestFixture]
    public class ParticleFootprintTests
    {
        private const string CATALOG_PATH =
            "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        /// <summary>Mirrors PredictionMargin / PredictionMarginFloor in the type under test.</summary>
        private static float Padded(float halfExtent)
            => halfExtent + Mathf.Max(0.06f, halfExtent * 0.12f);

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

        /// <summary>
        /// A preset that does not move, does not spin and has no size, so a test can measure
        /// one term at a time. ParticleVfxParams defaults are NOT inert — speed 2 over the
        /// default one-second life sweeps two whole units — so every case has to zero them
        /// explicitly or it measures the sweep instead of the shape.
        /// </summary>
        private static ParticleVfxParams Static(string kind)
            => new ParticleVfxParams
            {
                kind = kind,
                loops = true,
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
                startRotationJitterDegrees = 0f,
                rotationSpeedDegrees = 0f,
            };

        private ParticlePresetDefinition Preset(ParticleVfxParams vfx,
                                                params ParticlePresetDefinition[] layers)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _created.Add(def);
            def.id = "footprint_test";
            def.displayName = def.id;
            def.vfx = vfx;
            def.layers = new List<ParticlePresetDefinition>(layers);
            return def;
        }

        private static ParticlePresetDefinition Shipped(string id)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(CATALOG_PATH);
            Assert.IsTrue(catalog != null, $"ParticlePresetCatalog not found at {CATALOG_PATH}.");
            var preset = catalog.GetById(id);
            Assert.IsTrue(preset != null, $"'{id}' is missing from the catalog.");
            return preset;
        }

        // ── Emission geometry ────────────────────────────────────────────────────

        [Test]
        public void Aura_IsACircleOfItsAuthoredRadius()
        {
            var f = ParticleFootprint.OfParams(Static("aura"), 1f);

            Assert.IsFalse(f.IsRect);
            Assert.AreEqual(Padded(0.5f), f.Radius, 1e-4f);
            Assert.AreEqual(Vector2.zero, f.Center);
            Assert.IsTrue(f.Predicted, "Anything derived from the preset alone is a prediction.");
        }

        [Test]
        public void Portal_PrefersOuterRadiusOverRadius()
        {
            var v = Static("portal");
            v.radius = 0.4f;
            v.outerRadius = 1.1f;

            Assert.AreEqual(Padded(1.1f), ParticleFootprint.OfParams(v, 1f).Radius, 1e-4f,
                "ConfigureShape emits a portal from outerRadius when it is set; the marker " +
                "has to describe the same ring.");
        }

        [Test]
        public void SmokeEmitter_UsesDispersion_AndFallsBackWhenItIsZero()
        {
            var withDispersion = Static("smoke_emitter");
            withDispersion.dispersion = 0.9f;
            Assert.AreEqual(Padded(0.9f), ParticleFootprint.OfParams(withDispersion, 1f).Radius, 1e-4f);

            var without = Static("smoke_emitter");
            Assert.AreEqual(Padded(0.15f), ParticleFootprint.OfParams(without, 1f).Radius, 1e-4f,
                "The kind's own 0.15 fallback, reported at its real size. Small emitters stay " +
                "clickable through Inflated() at the hit test, not by overstating the box.");
        }

        [Test]
        public void FallingLeaf_IsTheWideThinStripItEmitsFrom()
        {
            var f = ParticleFootprint.OfParams(Static("falling_leaf"), 1f);

            Assert.IsTrue(f.IsRect, "falling_leaf emits from a 2 x 0.1 box, not a circle.");
            Assert.AreEqual(Padded(1f), f.HalfWidth, 1e-4f);
            Assert.AreEqual(Padded(0.05f), f.HalfHeight, 1e-4f,
                "The strip is 0.1 units tall and the marker says so. Flooring it would have " +
                "the box claim four times the height the emitter uses.");
        }

        [Test]
        public void PointLikeKinds_KeepTheirRealSize_AndAreMadeClickableByInflating()
        {
            // A dash puff emits from a 0.1-unit circle: a hair over two screen pixels at
            // 16 PPU. The marker reports that, and the hit test asks for the allowance.
            var f = ParticleFootprint.OfParams(Static("dash"), 1f);
            Assert.AreEqual(Padded(0.1f), f.Radius, 1e-4f);

            var clickable = f.Inflated(ParticleFootprint.MinHalfExtent);
            Assert.IsTrue(clickable.Contains(new Vector2(0.3f, 0f)),
                "A two-pixel emitter still has to be selectable.");
            Assert.IsFalse(f.Contains(new Vector2(0.3f, 0f)),
                "But the box itself must not claim that area.");
        }

        [Test]
        public void ShrinkingFar_KeepsTheBoxOnTheEmitter_InsteadOfStoppingAtAFloor()
        {
            // The resize bug this replaced: dragging a field's width right down, the drawn box
            // stopped at the floor while the emission kept shrinking, so the handle came off
            // the cursor and the outline described an area four times the real one.
            var v = Static("aura");
            v.spawnWidth = 3f;
            v.spawnHeight = 4f;

            var tiny = ParticleFootprint.OfParams(
                ParticleOverrideApplier.Apply(v, new ParticleInstanceOverrides(0.05f, 0.05f, 1f)), 1f);

            Assert.Less(tiny.HalfWidth, 0.15f,
                "At a twentieth of its authored width the box has to be a twentieth wide.");
        }

        [Test]
        public void SpawnArea_BeatsTheKind_AndIsHalvedIntoExtents()
        {
            var v = Static("aura");
            v.spawnWidth = 2.2f;
            v.spawnHeight = 1.8f;

            var f = ParticleFootprint.OfParams(v, 1f);

            Assert.IsTrue(f.IsRect, "An authored spawn box overrides the kind's circle.");
            Assert.AreEqual(Padded(1.1f), f.HalfWidth, 1e-4f);
            Assert.AreEqual(Padded(0.9f), f.HalfHeight, 1e-4f);
        }

        [Test]
        public void Heading_WithoutAnArea_IsTheConeBase()
        {
            var v = Static("aura");
            v.directionDegrees = 90f;
            v.dispersion = 0.35f;

            var f = ParticleFootprint.OfParams(v, 1f);

            Assert.IsFalse(f.IsRect);
            Assert.AreEqual(Padded(0.35f), f.Radius, 1e-4f,
                "A heading turns the shape into a cone of `dispersion`; the throw along it " +
                "is speed, which the sweep accounts for separately.");
        }

        [Test]
        public void ScaleMultiplier_GrowsTheFootprintWithTheEffect()
        {
            var v = Static("aura");
            v.radius = 0.62f;

            Assert.AreEqual(Padded(1.24f), ParticleFootprint.OfParams(v, 2f).Radius, 1e-4f,
                "Every radius in a preset is multiplied by the instance's scale, so a 2x " +
                "portal marked at 1x draws its outline inside the effect.");
        }

        // ── The sweep ────────────────────────────────────────────────────────────

        [Test]
        public void ConstantDrift_HangsTheAreaDownstream_InsteadOfGrowingItBothWays()
        {
            // A leaf field: emits from a box, then carries every leaf downward for its
            // whole life. The area below the spawner is covered; the area above it is not.
            var v = Static("aura");
            v.spawnWidth = 3f;
            v.spawnHeight = 4f;
            v.lifespan = 2f;
            v.useGravityVector = true;
            v.gravityVector = new Vector2(0f, -0.55f);

            var f = ParticleFootprint.OfParams(v, 1f);
            float marginY = Mathf.Max(0.06f, ((2f + 3.1f) * 0.5f) * 0.12f);

            Assert.AreEqual(-0.55f, f.Center.y, 1e-4f,
                "0.55 u/s for 2 s is 1.1 units of travel, added to the bottom edge only — " +
                "so the covered area's centre sits half that below the emitter.");
            Assert.AreEqual(2f + marginY, f.Max.y, 1e-4f, "Nothing travels upward.");
            Assert.AreEqual(-3.1f - marginY, f.Min.y, 1e-4f, "The bottom edge follows the leaves down.");
            Assert.AreEqual(Padded(1.5f), f.HalfWidth, 1e-4f, "Vertical drift must not widen the box.");
        }

        [Test]
        public void ScalarGravity_FallsAsHalfGTSquared()
        {
            var v = Static("aura");
            v.lifespan = 2f;
            v.gravity = 1f;

            // main.gravityModifier = gravity / 9.81 against Unity's -9.81 g, so the
            // acceleration is exactly `gravity` and the drop is 1/2 a t^2 = 2 units.
            var f = ParticleFootprint.OfParams(v, 1f);
            float marginY = Mathf.Max(0.06f, ((0.5f + 2.5f) * 0.5f) * 0.12f);

            Assert.AreEqual(-2.5f - marginY, f.Min.y, 1e-3f,
                "A 0.5-radius aura falling 2 units reaches 2.5 below the emitter.");
        }

        [Test]
        public void Speed_ExpandsInEveryDirection_BecauseTheThrowFollowsTheShapeNormal()
        {
            var v = Static("aura");
            v.speed = 0.5f;
            v.lifespan = 2f;

            var f = ParticleFootprint.OfParams(v, 1f);

            Assert.IsFalse(f.IsRect, "A symmetric throw off a circle stays a circle.");
            Assert.AreEqual(Padded(1.5f), f.Radius, 1e-4f,
                "startSpeed is a random 0..speed and the prediction has to bound the fastest " +
                "particle, so the whole 0.5 u/s for 2 s is reserved on top of the 0.5 radius. " +
                "Reserving only the average left 34 catalog presets short of their particles.");
        }

        [Test]
        public void ParticleSize_CountsAsCoveredArea()
        {
            var v = Static("aura");
            v.sizeMax = 0.6f;
            v.sizeAspect = 2f;

            var f = ParticleFootprint.OfParams(v, 1f);

            Assert.AreEqual(Padded(0.5f + 0.6f), f.HalfWidth, 1e-4f,
                "A quad centred on the emission edge sticks out by half its width, doubled " +
                "here by sizeAspect.");
            Assert.AreEqual(Padded(0.5f + 0.3f), f.HalfHeight, 1e-4f);
        }

        [Test]
        public void SpinningQuads_ReachTheirOwnDiagonal()
        {
            var still = Static("aura");
            still.sizeMax = 0.8f;

            var spinning = Static("aura");
            spinning.sizeMax = 0.8f;
            spinning.rotationSpeedDegrees = 40f;

            Assert.Greater(ParticleFootprint.OfParams(spinning, 1f).Radius,
                           ParticleFootprint.OfParams(still, 1f).Radius,
                "A square quad at 45 degrees is its diagonal wide, and Unity's own particle " +
                "bounds do not account for rotation at all.");
        }

        [Test]
        public void SizeOverLife_UsesTheCurvesPeak_IncludingItsOvershoot()
        {
            var v = Static("aura");
            v.sizeMax = 0.4f;
            v.sizeOverLife = new[]
            {
                new Keyframe2D(0f, 0.5f),
                new Keyframe2D(0.5f, 1.5f),   // the moment the quad is largest
                new Keyframe2D(1f, 0.2f),
            };

            // ParticleEmitter hands Unity a bare `new AnimationCurve(keys)`, which smooths the
            // tangents — so the curve rises ABOVE its own key value between keys. Taking the
            // key instead of sampling the curve is what left the pollen presets short.
            float atKeyValue = Padded(0.5f + (0.5f * 0.4f * 1.5f));

            Assert.GreaterOrEqual(ParticleFootprint.OfParams(v, 1f).Radius, atKeyValue - 1e-4f);
        }

        [Test]
        public void InwardPull_DoesNotGrowTheArea_ButOutwardPushDoes()
        {
            var inward = Static("aura");
            inward.radialSpeed = -2f;
            inward.lifespan = 2f;
            Assert.AreEqual(Padded(0.5f), ParticleFootprint.OfParams(inward, 1f).Radius, 1e-4f,
                "Particles drawn toward the centre never leave the emission ring.");

            // Radial pull is a constant velocity, not a random range, so the whole of it is
            // travelled: 0.5 u/s for 2 s is a full unit outward.
            var outward = Static("aura");
            outward.radialSpeed = 0.5f;
            outward.lifespan = 2f;
            Assert.AreEqual(Padded(1.5f), ParticleFootprint.OfParams(outward, 1f).Radius, 1e-4f);
        }

        [Test]
        public void Orbit_TurnsABoxIntoTheDiscItSweeps()
        {
            var v = Static("aura");
            v.spawnWidth = 6f;      // half 3
            v.spawnHeight = 8f;     // half 4
            v.orbitalSpeedDegrees = 90f;

            var f = ParticleFootprint.OfParams(v, 1f);

            Assert.AreEqual(Padded(5f), f.Radius, 1e-4f,
                "A corner 3,4 from the centre traces a circle of radius 5 as the field turns.");
        }

        [Test]
        public void Noise_IsBoundedAsAWalk_NotAsAFixedOffset()
        {
            var quiet = Static("aura");

            var noisy = Static("aura");
            noisy.noiseEnabled = true;
            noisy.noiseStrength = 0.3f;

            var longLived = Static("aura");
            longLived.noiseEnabled = true;
            longLived.noiseStrength = 0.3f;
            longLived.lifespan = 8f;

            float quietR = ParticleFootprint.OfParams(quiet, 1f).Radius;
            float noisyR = ParticleFootprint.OfParams(noisy, 1f).Radius;
            float longR = ParticleFootprint.OfParams(longLived, 1f).Radius;

            Assert.Greater(noisyR, quietR + 0.3f,
                "Unity's noise module treats its strength as an amplitude it can exceed; " +
                "reserving one strength left the haze presets outside their marker.");
            Assert.Greater(longR, noisyR,
                "The field scrolls, so the displacement keeps accumulating for as long as " +
                "the particle lives — the allowance has to grow with lifespan.");
        }

        [Test]
        public void RunawaySweep_IsCappedAndSaysSo()
        {
            // A projectile trail: 16 u/s for 3 s sweeps 48 units, an outline larger than the
            // screen and useless for grabbing anything.
            var v = Static("aura");
            v.speed = 16f;
            v.lifespan = 3f;

            var f = ParticleFootprint.OfParams(v, 1f);

            Assert.AreEqual(ParticleFootprint.MaxHalfExtent, f.Radius, 1e-4f);
            Assert.IsTrue(f.Clipped,
                "A clipped prediction does NOT bound the effect, and the flag is how the " +
                "coverage guard knows not to hold it to one.");
        }

        // ── Composites ───────────────────────────────────────────────────────────

        [Test]
        public void Composite_CoversTheUnionOfItsLayers()
        {
            var wideLayer = Static("aura");
            wideLayer.spawnWidth = 2.2f;
            wideLayer.spawnHeight = 1.8f;

            var root = Preset(Static("aura"), Preset(wideLayer));

            var f = ParticleFootprint.Of(root, 1f);

            Assert.IsTrue(f.IsRect, "A circle unioned with a box must become a box — a " +
                "circle around a wide strip claims area the emitter never touches.");
            Assert.AreEqual(Padded(1.1f), f.HalfWidth, 1e-4f);
            Assert.AreEqual(Padded(0.9f), f.HalfHeight, 1e-4f);
        }

        [Test]
        public void Composite_EnvelopesLayersThatDriftDifferentWays()
        {
            var rises = Static("aura");
            rises.lifespan = 2f;
            rises.useGravityVector = true;
            rises.gravityVector = new Vector2(0f, 0.5f);       // +1 over its life

            var falls = Static("aura");
            falls.lifespan = 2f;
            falls.useGravityVector = true;
            falls.gravityVector = new Vector2(0f, -0.5f);      // -1 over its life

            var f = ParticleFootprint.Of(Preset(rises, Preset(falls)), 1f);
            float edge = ParticleFootprint.OfParams(rises, 1f).Max.y;

            Assert.AreEqual(edge, f.Max.y, 1e-4f, "The rising layer sets the top edge.");
            Assert.AreEqual(-edge, f.Min.y, 1e-4f, "The falling layer sets the bottom edge.");
        }

        [Test]
        public void Composite_IgnoresNullAndSelfReferencingLayers()
        {
            var root = Preset(Static("aura"));
            root.layers.Add(null);
            root.layers.Add(root);

            var f = ParticleFootprint.Of(root, 1f);

            Assert.AreEqual(Padded(0.5f), f.Radius, 1e-4f,
                "The emitter skips both when it builds the stack, so neither adds area.");
        }

        [Test]
        public void NullPreset_FallsBackToTheDefaultMarker()
        {
            var f = ParticleFootprint.Of(null, 1f);

            Assert.AreEqual(ParticleFootprint.Default.Radius, f.Radius, 1e-4f);
            Assert.IsFalse(f.IsRect);
            Assert.IsTrue(f.Predicted);
        }

        // ── Containment and area (the picking rules) ─────────────────────────────

        [Test]
        public void Contains_TreatsARectAsARect_NotAsItsBoundingCircle()
        {
            var f = ParticleFootprint.Rect(1.1f, 0.4f);

            Assert.IsTrue(f.Contains(new Vector2(1.0f, 0.3f)));
            Assert.IsFalse(f.Contains(new Vector2(0.9f, 0.9f)),
                "A corner outside the box must not select the emitter just because it is " +
                "within the box's longest extent.");
        }

        [Test]
        public void Contains_FollowsTheOffsetCentre_SoADriftedAreaIsClickableWhereItIsDrawn()
        {
            var f = ParticleFootprint.Rect(new Vector2(0f, -1.1f), 1.5f, 2.55f);

            Assert.IsTrue(f.Contains(new Vector2(0f, -3.0f)),
                "Clicking the leaves where they have fallen to must select the emitter.");
            Assert.IsFalse(f.Contains(new Vector2(0f, 2.0f)),
                "Nothing drifts upward, so the space above the emitter is not its area.");
        }

        [Test]
        public void Area_RanksTheSmallFootprintFirst_SoAHazeCannotSwallowIt()
        {
            // The hit test picks the smallest footprint containing the cursor; without that
            // ordering a two-unit haze layer eats every precise emitter placed inside it.
            var haze = ParticleFootprint.Rect(1.1f, 0.9f);
            var mote = ParticleFootprint.Circle(0.3f);

            Assert.Less(mote.Area, haze.Area);
        }

        // ── Measured against the running systems ─────────────────────────────────

        /// <summary>Builds a live emitter and runs it long enough to reach steady state.</summary>
        private ParticleEmitter RunEmitter(string presetId, float scale, float seconds)
        {
            var go = new GameObject("FootprintProbe_" + presetId);
            _created.Add(go);

            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(Shipped(presetId), scale);

            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                // Outside play mode a system that has never played swallows Simulate.
                ps.Play();
                ps.Simulate(seconds, true, false, true);
            }

            return emitter;
        }

        [Test]
        public void OfLive_MeasuresWhatIsOnScreen_InsteadOfTheWorstCasePrediction()
        {
            // The portal stack's sparks are authored at 2.6 u/s over a 1.6 s life, but
            // startSpeed is a random 0..speed, so almost no particle takes the full 4 units
            // the prediction has to reserve.
            var emitter = RunEmitter("portal_oval_core_soft", 2f, 6f);

            Bounds live;
            Assert.IsTrue(emitter.TryGetLiveBounds(out live), "The probe emitted nothing.");

            var measured = ParticleFootprint.OfLive(emitter);
            var predicted = ParticleFootprint.Of(emitter.Preset, emitter.ScaleMultiplier);

            Assert.IsFalse(measured.Predicted, "A measurement must not claim to be a prediction.");
            Assert.Less(measured.Area, predicted.Area * 0.8f,
                "For a preset with a wide random speed range the prediction must reserve far " +
                "more than the effect uses; OfLive exists so the DRAWN marker is the effect.");
        }

        [Test]
        public void OfLive_CoversTheParticlesOfADriftingField()
        {
            var emitter = RunEmitter("falling_leaf_30s", 1f, 6f);

            Bounds live;
            Assert.IsTrue(emitter.TryGetLiveBounds(out live), "The probe emitted nothing.");

            var f = ParticleFootprint.OfLive(emitter);
            Vector3 origin = emitter.transform.position;

            Assert.IsTrue(f.Contains((Vector2)(live.min - origin)));
            Assert.IsTrue(f.Contains((Vector2)(live.max - origin)));
            Assert.IsFalse(f.Predicted, "Particles are alive, so this must be a measurement.");

            // Where the box SITS is not asserted here on purpose. A measurement is a sample of
            // wherever this preset's particles happen to be, and a field that drifts a unit
            // over a four-unit spawn box can, on an unlucky frame, be centred anywhere inside
            // it. The deterministic prediction is where the drift direction is pinned — see
            // ShippedLeafFall_CoversTheLeavesBelowItsSpawnBox — and the catalog-wide coverage
            // guard is what holds the measurement to containing its particles.
        }

        [Test]
        public void OfLive_FallsBackToThePrediction_WhileNothingIsAliveYet()
        {
            var go = new GameObject("FootprintProbe_idle");
            _created.Add(go);
            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(Shipped("falling_leaf_30s"), 1f);

            Bounds unused;
            Assert.IsFalse(emitter.TryGetLiveBounds(out unused),
                "A freshly applied preset has emitted nothing yet — that is the case this " +
                "fallback exists for.");

            var f = ParticleFootprint.OfLive(emitter);
            var predicted = ParticleFootprint.Of(emitter.Preset, emitter.ScaleMultiplier);

            Assert.AreEqual(predicted.HalfWidth, f.HalfWidth, 1e-4f);
            Assert.AreEqual(predicted.HalfHeight, f.HalfHeight, 1e-4f);
            Assert.IsTrue(f.Predicted,
                "The outline reads this flag to cut to the first real measurement instead of " +
                "easing a worst-case box down at the shrink rate.");
        }

        // ── The shipped data ─────────────────────────────────────────────────────

        [Test]
        public void ShippedLeafFall_CoversTheLeavesBelowItsSpawnBox()
        {
            // The screenshot case. Emission box 3 x 4, drift (0.08, -0.55) for 2 s, leaf quads
            // 0.55 across: leaves reach roughly 3.4 units below the emitter, well past the
            // 2.0 the spawn box alone claimed.
            var f = ParticleFootprint.Of(Shipped("falling_leaf_30s"), 1f);

            Assert.IsTrue(f.IsRect);
            Assert.Less(f.Min.y, -3f,
                "A leaf that has fallen for its whole life is a unit below the spawn box, " +
                "and it used to land outside its own outline.");
            Assert.IsTrue(f.Contains(new Vector2(0f, -3f)));
            Assert.Greater(f.Max.y, 2f, "The top edge is the spawn box plus half a leaf.");
            Assert.Greater(f.HalfWidth, 1.5f, "Sideways drift and the quad's own width count too.");
            Assert.Less(f.Center.y, 0f,
                "Leaves fall, so the area hangs below the spawner rather than straddling it.");
        }

        [Test]
        public void ShippedPollenField_CoversEveryLayerItSpawnsWith()
        {
            var pollen = Shipped("flowers_pollen_soft");
            var f = ParticleFootprint.Of(pollen, 1f);

            Assert.IsTrue(f.IsRect);

            foreach (var layer in pollen.layers)
            {
                if (layer == null || layer.vfx == null) continue;
                var own = ParticleFootprint.OfParams(layer.vfx, 1f);
                Assert.IsTrue(f.Contains(own.Min) && f.Contains(own.Max),
                    $"Layer '{layer.id}' sweeps outside the composite marker that is supposed " +
                    "to contain it.");
            }

            // Pollen rises: the drift is +Y on every layer, so the area leans upward — unless
            // the prediction ran into the handle cap, which clips both sides equally and
            // therefore erases the lean. A clipped box says outright that it is not a bound,
            // so there is nothing left to assert about where it sits.
            if (!f.Clipped) Assert.Greater(f.Center.y, 0f);
            else Assert.AreEqual(ParticleFootprint.MaxHalfExtent, f.HalfHeight, 1e-3f);
        }
    }
}
