using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// The firework RIG — what actually gets drawn. <see cref="FireworkSpellDataTests"/> covers
    /// the authored half.
    ///
    /// <para>Every assertion here guards something that has already gone wrong at least once in
    /// this project, usually in another effect: an additive layer quietly built on the alpha
    /// material, a layer count that turned out to be a brightness dial, a sorting order written
    /// as a literal and then outgrown by the stack under it, a trail emitted in local space so
    /// that nothing is ever left behind, and a light destroyed on a hard cut.</para>
    /// </summary>
    public class FireworkVisualContractTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            // EditMode: renderer.material leaks and Destroy-in-edit-mode both log, and neither
            // is what this fixture is measuring.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
        }

        private FireworkBurstFX Burst(float radius = 3.4f)
        {
            var fx = FireworkBurstFX.Spawn(Vector3.zero, FireworkPalette.From(Color.white), radius);
            _root = fx.gameObject;
            return fx;
        }

        /// <summary>
        /// Launch a shell and adopt it for teardown. Also clears the two objects the launch
        /// leaves loose in the scene — the mortar sparks and the muzzle flash are deliberately
        /// NOT children of the shell, because they belong to the place it left from rather than
        /// to the thing that has already gone.
        /// </summary>
        private FireworkShellController Shell(Vector2 aim, float distance = 6.5f)
        {
            var shell = FireworkShellController.Launch(
                Vector3.zero, aim, FireworkPalette.From(Color.white),
                flightDistance: distance, flightSpeed: 9f, burstRadius: 3.4f);
            _root = shell.gameObject;
            ClearLooseLaunchObjects();
            return shell;
        }

        private static void ClearLooseLaunchObjects()
        {
            foreach (var ps in Object.FindObjectsOfType<ParticleSystem>())
                if (ps.name == "LaunchSparks") Object.DestroyImmediate(ps.gameObject);
            foreach (var l in Object.FindObjectsOfType<Light2D>())
                if (l.name == "MuzzleFlash") Object.DestroyImmediate(l.gameObject);
        }

        private static ParticleSystem Child(FireworkBurstFX fx, string name)
        {
            var t = fx.transform.Find(name);
            Assert.IsNotNull(t, $"'{name}' is missing from the burst rig — the fixture would " +
                                "assert on nothing.");
            var ps = t.GetComponent<ParticleSystem>();
            Assert.IsNotNull(ps, $"'{name}' carries no ParticleSystem.");
            return ps;
        }

        // ── Blending ───────────────────────────────────────────────────────────

        /// <summary>
        /// Every light-emitting layer must be additive. On <c>Sprite-Unlit-Default</c> the
        /// brightest pixel a star can produce is its own colour, so a shell built there cannot
        /// blow out — and worse, a blend-mode assignment against that shader compiles, logs
        /// nothing and does nothing.
        /// </summary>
        [Test]
        public void EveryLightLayerIsAdditive()
        {
            var fx = Burst();

            foreach (var sr in fx.GetComponentsInChildren<SpriteRenderer>())
                Assert.AreSame(ElementalSprites.SharedAdditiveMaterial, sr.sharedMaterial,
                    $"'{sr.name}' is not on the additive material. Alpha is COVERAGE there and " +
                    "the colour is the brightness dial; on the unlit material neither is true.");

            var stars = Child(fx, "Stars").GetComponent<ParticleSystemRenderer>();
            Assert.AreSame(ParticleMaterialCache.Get(ElementalSprites.SparkleStar.texture, true),
                stars.sharedMaterial,
                "The stars must be on the ADDITIVE particle material.");
        }

        /// <summary>
        /// The embers are the one opaque layer, and that is the whole difference between "the
        /// sky is being lit" and "something is burning up there". Folding them into the additive
        /// stack would not dim them, it would make them VANISH — a dark chip added to a bright
        /// pixel changes almost nothing. <c>KiAuraFX</c> and <c>VortexFunnelFX</c> record the
        /// same rule for their ground debris.
        /// </summary>
        [Test]
        public void TheEmbersAreTheOneOpaqueLayer()
        {
            var fx = Burst();
            var embers = Child(fx, "Embers").GetComponent<ParticleSystemRenderer>();

            Assert.AreSame(ParticleMaterialCache.Get(ElementalSprites.Sparkle.texture, false),
                embers.sharedMaterial,
                "The embers must NOT be additive.");
            Assert.AreNotSame(ParticleMaterialCache.Get(ElementalSprites.Sparkle.texture, true),
                embers.sharedMaterial);
        }

        // ── Brightness vs count ────────────────────────────────────────────────

        /// <summary>
        /// On an additive stack a pixel receives the SUM of everything over it, so a layer count
        /// is a brightness dial unless something divides it out. Measured on the vortex: raising
        /// its bands from 9 to 18 took the summed alpha from 3.99 to 7.97 and washed a red
        /// effect out to white.
        /// </summary>
        [Test]
        public void TheStarCountIsAResolutionDialAndNotABrightnessOne()
        {
            int reference = FireworkBurstFX.STAR_ALPHA_REFERENCE_COUNT;

            float Summed(int count) => FireworkBurstFX.StarAlphaFor(count) * count;

            float atReference = Summed(reference);

            // Only counts AT OR ABOVE the reference are pinned. Below it the division wants a
            // per-star alpha over 1, which does not exist — a shell with half the stars really
            // is dimmer, and that is physics rather than a defect. The direction that matters
            // is the one that goes wrong silently: MORE layers must not mean more light.
            Assert.AreEqual(atReference, Summed(reference * 2), 0.01f,
                "Doubling the star count doubled the shell's summed alpha. Divide the per-star " +
                "alpha by the count, or STARS is a brightness knob wearing a resolution label.");
            Assert.AreEqual(atReference, Summed(reference * 4), 0.01f,
                "Quadrupling the star count changed the shell's brightness.");
        }

        [Test]
        public void TheShippedStarCountSitsAtItsAlphaReference()
        {
            Assert.AreEqual(FireworkBurstFX.STAR_ALPHA_REFERENCE_COUNT, FireworkBurstFX.STARS,
                "Not a hard requirement, but a shipped count away from its reference means the " +
                "tuned brightness is being reached through the division rather than directly. " +
                "If that is deliberate, move the reference.");
        }

        // ── Sorting ────────────────────────────────────────────────────────────

        /// <summary>
        /// The shell opens above the rooftops, and VFX sorts UNDER WallsTop in this project's
        /// ladder — the trap <c>LightningBoltFX</c> fell into, where every bolt drew beneath the
        /// wall tops it was supposed to light.
        /// </summary>
        [Test]
        public void TheBurstDrawsAboveTheRooftops()
        {
            var fx = Burst();

            foreach (var sr in fx.GetComponentsInChildren<SpriteRenderer>())
                Assert.AreEqual(SortingConfig.LAYER_OVERHEAD, sr.sortingLayerName,
                    $"'{sr.name}' is on '{sr.sortingLayerName}'. A burst seven units up that " +
                    "renders under a roof reads as being behind the building.");

            foreach (var pr in fx.GetComponentsInChildren<ParticleSystemRenderer>())
                Assert.AreEqual(SortingConfig.LAYER_OVERHEAD, pr.sortingLayerName,
                    $"'{pr.name}' is on '{pr.sortingLayerName}'.");
        }

        /// <summary>
        /// The embers fall in FRONT of the shell, and the flash sits over everything. Both are
        /// derived from the layer under them rather than written as literals — a hand-maintained
        /// order is right until the stack beneath it grows, which is how the vortex sank its
        /// near-side debris behind its own funnel.
        /// </summary>
        [Test]
        public void TheSortingOrdersAreDerivedAndStrictlyStacked()
        {
            Assert.Less(FireworkBurstFX.ORDER_RING, FireworkBurstFX.ORDER_STAR);
            Assert.Less(FireworkBurstFX.ORDER_STAR, FireworkBurstFX.ORDER_EMBER,
                "The embers must clear the star layer, or the material scraps sink behind the " +
                "light they are supposed to be falling out of.");
            Assert.Less(FireworkBurstFX.ORDER_EMBER, FireworkBurstFX.ORDER_FLASH);

            var fx = Burst();
            Assert.AreEqual(FireworkBurstFX.ORDER_STAR,
                Child(fx, "Stars").GetComponent<ParticleSystemRenderer>().sortingOrder);
            Assert.AreEqual(FireworkBurstFX.ORDER_EMBER,
                Child(fx, "Embers").GetComponent<ParticleSystemRenderer>().sortingOrder);
        }

        // ── Geometry ───────────────────────────────────────────────────────────

        /// <summary>
        /// <c>ElementalSprites.Ring</c>'s bright band peaks at normalized radius 0.78, so a ring
        /// meant to land ON a world radius is scaled by <c>radius / 0.39</c>. The shockwave is
        /// the rig's only hard contour and its whole job is to say how big the shell is.
        /// </summary>
        [Test]
        public void TheShockwaveLandsOnTheStarRadius()
        {
            const float radius = 3.4f;
            float span = FireworkBurstFX.RingSpanFor(radius);

            // The sprite is 1x1 world unit, so span IS its drawn diameter and the band sits at
            // 0.78 of its half-width.
            float drawnBandRadius = span * 0.5f * 0.78f;
            Assert.AreEqual(radius, drawnBandRadius, 0.02f,
                $"The ring's bright band drew at {drawnBandRadius:F3} against a {radius} radius. " +
                "Scaling it by the radius directly would put the contour at 0.39 of where the " +
                "stars actually reach.");
        }

        // ── Light ──────────────────────────────────────────────────────────────

        /// <summary>
        /// URP hardcodes a blend style as purely multiplicative or purely additive, so one light
        /// cannot both illuminate a surface and glow over it. The multiply BODY lights the
        /// world; the additive CORE is what makes it read as emissive rather than as a stain.
        /// </summary>
        [Test]
        public void TheBurstLightsTheWorldAndGlowsOverIt()
        {
            var fx = Burst();
            var lights = fx.GetComponentsInChildren<Light2D>();

            Assert.AreEqual(2, lights.Length,
                "The burst needs a multiply body AND an additive core.");
            Assert.IsTrue(lights.Any(l => l.blendStyleIndex == 0),
                "No multiply light: the burst adds a glow and lights nothing.");
            Assert.IsTrue(lights.Any(l => l.blendStyleIndex == 1),
                "No additive light: the burst stains the ambient buffer instead of glowing.");

            foreach (var l in lights)
            {
                Assert.AreEqual(Light2D.LightType.Point, l.lightType);
                Assert.IsFalse(l.shadowsEnabled,
                    "URP derives a 2D caster's shape from Renderer bounds, so every building " +
                    "throws a hard rectangle. Shadows are off project-wide for this reason.");
            }
        }

        /// <summary>
        /// The version this replaces held a fixed intensity and then called
        /// <c>Destroy(lightGo, 0.20f)</c> — a square pulse that ended while the effect it lit
        /// was still on screen. The light has to outlive the white flash at minimum.
        /// </summary>
        [Test]
        public void TheLightOutlivesTheFlashItBelongsTo()
        {
            float lightSeconds = FireworkBurstFX.STAR_LIFETIME * FireworkBurstFX.LIGHT_LIFE_FRACTION;

            Assert.Greater(lightSeconds, 0.5f,
                $"The burst light lasts {lightSeconds:F2}s. A light that is gone in a fifth of a " +
                "second leaves a lit effect sitting in unlit air.");
            Assert.LessOrEqual(lightSeconds, FireworkBurstFX.STAR_LIFETIME,
                "The light must not outlast the stars either, or the sky stays bright after the " +
                "shell has burned out.");
        }

        // ── The climb ──────────────────────────────────────────────────────────

        /// <summary>
        /// World space is what makes a trail a trail. The preset this replaces left
        /// <c>worldSpace</c> at its default, so its twelve particles travelled WITH the
        /// projectile and nothing was ever left behind.
        /// </summary>
        [Test]
        public void TheShellsTrailIsEmittedIntoWorldSpace()
        {
            var shell = Shell(Vector2.right);

            var trailT = shell.transform.Find("Trail");
            Assert.IsNotNull(trailT, "The shell has no trail.");
            var trail = trailT.GetComponent<ParticleSystem>();

            Assert.AreEqual(ParticleSystemSimulationSpace.World, trail.main.simulationSpace,
                "A local-space trail on a moving emitter is a blob being dragged, not a trail.");
            Assert.IsTrue(trail.main.loop,
                "A one-shot trail emits once at the spawn frame and the rest of the climb is bare.");
            Assert.Greater(trail.emission.rateOverDistance.constant, 0f,
                "Emitting over DISTANCE is what keeps the trail's density honest at any climb " +
                "speed; a rate over time draws the same density however fast the shell moves.");

        }

        // ── Aim ────────────────────────────────────────────────────────────────

        /// <summary>
        /// The shell goes where the cursor points. <c>ctx.Direction</c> is already the mouse
        /// bearing for a player — <c>PlayerFacingResolver</c> derives it from
        /// <c>MouseInputManager</c> — and the version this replaces threw all of it away except
        /// the sign of x, which it used as a 35 % nudge on a climb that was always straight up.
        /// Aiming moved the burst by a couple of units and could never move it down or behind.
        /// </summary>
        [Test]
        public void TheShellFliesTowardsTheAim()
        {
            const float distance = 6.5f;

            var east = Shell(Vector2.right, distance);
            Assert.AreEqual(distance, east.BurstPosition.x, 0.01f,
                "Aimed east, the shell must burst a full flight distance east.");
            Assert.AreEqual(0f, east.BurstPosition.y, 0.01f);
            Object.DestroyImmediate(_root); _root = null;

            var up = Shell(Vector2.up, distance);
            Assert.AreEqual(distance, up.BurstPosition.y, 0.01f);
            Assert.AreEqual(0f, up.BurstPosition.x, 0.01f);
            Object.DestroyImmediate(_root); _root = null;

            // The case the old implementation could not express at all.
            var downLeft = Shell(new Vector2(-1f, -1f), distance);
            Assert.Less(downLeft.BurstPosition.x, 0f,
                "Aimed behind the caster, the shell still flew forward.");
            Assert.Less(downLeft.BurstPosition.y, 0f,
                "Aimed downward, the shell still climbed. The aim's y was being discarded.");
        }

        /// <summary>
        /// An aim of zero — a monster with no bearing, a console command — must still produce a
        /// firework rather than a shell that bursts in the caster's lap.
        /// </summary>
        [Test]
        public void AZeroAimFallsBackToStraightUp()
        {
            var shell = Shell(Vector2.zero);
            Assert.Greater(shell.BurstPosition.y, 1f,
                "A zero aim resolved to nowhere instead of to up.");
        }

        /// <summary>
        /// The bow is what keeps a mortar from being a bullet, and it is scaled by how HORIZONTAL
        /// the aim is: a shot straight up has no straight line to bow away from.
        /// </summary>
        [Test]
        public void TheArcBowsMostWhenTheAimIsFlattest()
        {
            var flat = Shell(Vector2.right);
            float flatBow = flat.ArcBow;
            Object.DestroyImmediate(_root); _root = null;

            var vertical = Shell(Vector2.up);
            Assert.AreEqual(0f, vertical.ArcBow, 0.01f,
                "A vertical shot must not bow — there is nothing to bow away from.");
            Assert.Greater(flatBow, 1f,
                $"A flat shot bowed only {flatBow:F2} units, which reads as a straight line.");
        }

        /// <summary>
        /// The report has to arrive AFTER the picture, and later the further away the shell
        /// burst. It is most of what makes the burst read as happening out there rather than on
        /// the lens.
        /// </summary>
        [Test]
        public void TheReportIsLaterThanTheBurstAndScalesWithHeight()
        {
            Assert.Greater(FireworkShellController.REPORT_DELAY_PER_UNIT, 0f,
                "A report that lands on the same frame as the flash says the shell went off " +
                "next to the player's ear.");

            float near = 3f * FireworkShellController.REPORT_DELAY_PER_UNIT;
            float far = 9f * FireworkShellController.REPORT_DELAY_PER_UNIT;
            Assert.Greater(far, near, "A more distant shell must be heard later.");
            Assert.Less(far, 0.35f,
                $"A {far:F2}s delay at 9 units is long enough to read as a bug rather than as " +
                "distance.");
        }

        // ── The sky ────────────────────────────────────────────────────────────

        /// <summary>
        /// A burst seven units up cannot light the world with its own point light — it has to
        /// reach the Global Light 2D, which is the same conclusion the storm strike came to.
        /// The envelope is a fast attack and a slow release: a symmetric one reads as a lamp.
        /// </summary>
        [Test]
        public void TheSkyFlashRampsUpFastAndDecaysSlowly()
        {
            Burst();   // Spawn pulses the sky as part of Build.

            Assert.Greater(SkyFlash.Flash01, 0f, "The burst did not light the sky at all.");

            float peak = SkyFlash.Flash01;
            SkyFlash.Tick(0.03f);
            float early = SkyFlash.Flash01;
            SkyFlash.Tick(0.30f);
            float late = SkyFlash.Flash01;

            Assert.Greater(early, late, "The flash must decay, not hold.");
            Assert.Greater(late, 0f, "It decayed to nothing in a third of a second — that is a cut.");

            SkyFlash.Tick(5f);
            Assert.AreEqual(0f, SkyFlash.Flash01, 1e-5f,
                "A sky flash that never returns to zero leaves the world permanently brighter.");
            Assert.Less(peak, 1.01f);
        }

        /// <summary>
        /// A volley must build to a glow rather than chop itself off. A second pulse keeps
        /// whichever is brighter AT THIS INSTANT — comparing authored peaks instead would let a
        /// fading bright pulse refuse a new one that is currently brighter.
        /// </summary>
        [Test]
        public void ASecondShellDoesNotCutTheFirstOneOff()
        {
            SkyFlash.Tick(10f);   // clear anything a previous test left running

            SkyFlash.Pulse(Color.white, 0.8f, 0.5f);
            SkyFlash.Tick(0.30f);
            float fading = SkyFlash.Flash01;

            SkyFlash.Pulse(Color.white, 0.5f, 0.5f);
            Assert.GreaterOrEqual(SkyFlash.Flash01, fading,
                "A dimmer second shell made the sky darker than the one already fading.");

            SkyFlash.Tick(10f);
        }
    }
}
