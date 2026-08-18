using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Protects the fireball, which is the game's signature spell and the reference every
    /// other effect gets judged against.
    ///
    /// This fixture deliberately does NOT freeze the numbers. A test that asserts
    /// sizeMin == 0.5 fails on every honest retune, and the only thing it teaches is to
    /// update the expected value without reading it — which is worse than no test, because
    /// it converts a safety net into a chore.
    ///
    /// What it asserts instead are the properties that make the effect read as fire at all.
    /// Each one corresponds to a way the effect has actually been broken, or to a trap
    /// documented in the vfx-authoring skill:
    ///
    ///   • A trail layer in local space is carried along by the projectile and leaves
    ///     nothing behind. That is not a tuning mistake, it is the effect not existing.
    ///   • A layer with no alphaOverLife silently falls onto a hardcoded fade and its
    ///     colorOverLife is ignored entirely.
    ///   • A looping layer with no sizeOverLife has the module switched off, so particles
    ///     never taper.
    ///   • An impact whose particles have no speed and no growth is a blink, not a blast —
    ///     which is exactly what explosion_small still is.
    ///   • A cast preset that loops never stops, leaking an emitter per shot.
    ///
    /// Retuning within these bounds is expected and free. Crossing one of them is a
    /// different kind of change, and this fixture makes you say so out loud.
    /// </summary>
    [TestFixture]
    public class FireballSignatureTests
    {
        private const string SPELL_CATALOG    = "Assets/_Project/Data/Catalogs/SpellCatalog.asset";
        private const string PARTICLE_CATALOG = "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        /// <summary>Particles small enough to vanish at 16 PPU. The original trail was 1.5 px.</summary>
        private const float MIN_VISIBLE_SIZE = 0.05f;

        /// <summary>Projectile speed, used to turn a lifespan into a trail length in world units.</summary>
        private const float FIREBALL_SPEED = 16f;

        /// <summary>Shortest streak that still reads as a trail rather than as a halo.</summary>
        private const float MIN_TRAIL_LENGTH = 4f;

        private SpellDefinition _fireball;
        private ParticlePresetCatalog _particles;

        [SetUp]
        public void SetUp()
        {
            var spells = AssetDatabase.LoadAssetAtPath<SpellCatalog>(SPELL_CATALOG);
            _particles = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(PARTICLE_CATALOG);

            Assert.IsNotNull(spells, "SpellCatalog missing — this fixture would silently pass.");
            Assert.IsNotNull(_particles, "ParticlePresetCatalog missing.");

            _fireball = spells.AllSpells.FirstOrDefault(s => s != null && s.spellKey == "fireball");
            Assert.IsNotNull(_fireball, "fireball is not in the SpellCatalog.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private List<ParticleVfxParams> Layers(IEnumerable<string> ids)
        {
            var result = new List<ParticleVfxParams>();
            foreach (var id in ids)
            {
                var def = _particles.GetById(id);
                Assert.IsNotNull(def, $"Preset '{id}' does not resolve in the catalog.");
                Assert.IsNotNull(def.vfx, $"Preset '{id}' has no VFX parameters.");
                result.Add(def.vfx);
            }
            return result;
        }

        private List<ParticleVfxParams> Trail  => Layers(_fireball.CollectVfxPresets());
        private List<ParticleVfxParams> Impact => Layers(_fireball.CollectImpactPresets());
        private List<ParticleVfxParams> Cast   => Layers(_fireball.CollectCastPresets());

        private ParticleVfxParams Layer(string id) => Layers(new[] { id })[0];

        private static float MinChannel(Color c) => Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        private static float MaxChannel(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

        private static float PeakSizeMultiplier(ParticleVfxParams v)
            => v.sizeOverLife == null || v.sizeOverLife.Length == 0
                ? 1f
                : v.sizeOverLife.Max(k => k.value);

        /// <summary>Steady-state live particles for a looping layer.</summary>
        private static float LiveCount(ParticleVfxParams v) => v.loops ? v.emitRate * v.lifespan : 0f;

        // ── Shape of the effect ──────────────────────────────────────────────────

        [Test]
        public void Trail_KeepsItsFourLayers()
        {
            var ids = _fireball.CollectVfxPresets();

            CollectionAssert.AreEqual(
                new[] { "fireball_core", "fireball_wake", "fireball_sparks", "fireball_smoke" },
                ids,
                "Order is draw order, and each layer does a job no other can: an additive core " +
                "for the body, a wake for the streak, sparks for direction, alpha smoke for mass. " +
                "Removing one is not a simplification.");
        }

        [Test]
        public void Impact_KeepsItsFiveLayers()
        {
            Assert.AreEqual(5, _fireball.CollectImpactPresets().Count,
                "Flash, shockwave, burst, debris, smoke. The hit reads as one event only " +
                "because five things happen at once.");
        }

        [Test]
        public void Cast_KeepsItsThreeLayers()
        {
            Assert.AreEqual(3, _fireball.CollectCastPresets().Count,
                "The launch is the beat the player is guaranteed to be watching.");
        }

        // ── The core is the projectile ───────────────────────────────────────────

        [Test]
        public void Core_RidesWithTheProjectile_WhileTheRestIsLeftBehind()
        {
            Assert.IsFalse(Layer("fireball_core").worldSpace,
                "The core IS the projectile. In world space it would be left behind by the " +
                "thing it is supposed to be.");

            foreach (var id in new[] { "fireball_wake", "fireball_sparks", "fireball_smoke" })
                Assert.IsTrue(Layer(id).worldSpace,
                    $"'{id}' must simulate in world space. In local space it is carried along at " +
                    "16 u/s and the trail does not exist — the whole effect moves as a rigid blob.");
        }

        [Test]
        public void Core_IsTheLargestTrailLayer()
        {
            float core = Layer("fireball_core").sizeMax;

            foreach (var id in new[] { "fireball_wake", "fireball_sparks", "fireball_smoke" })
                Assert.GreaterOrEqual(core, Layer(id).sizeMax,
                    $"The core must dominate '{id}'. When the wake outgrows it the fireball " +
                    "stops having a body and becomes a smear.");
        }

        [Test]
        public void Core_IsBornNearWhite()
        {
            var core = Layer("fireball_core");
            Assert.IsNotEmpty(core.colorOverLife, "The core's colour must be authored over life.");

            float min = MinChannel(core.colorOverLife[0].color);
            Assert.GreaterOrEqual(min, 0.7f,
                "A hot core is born near-white and ages into its hue. Starting already saturated " +
                "is the single most common reason a fire effect reads as orange plastic.");
        }

        [Test]
        public void Trail_HasBothLightAndMass()
        {
            var trail = Trail;

            Assert.IsTrue(trail.Any(v => v.additive),
                "Fire emits. With no additive layer it can only ever occlude what is behind it.");
            Assert.IsTrue(trail.Any(v => !v.additive),
                "Smoke blocks. An all-additive stack has no mass and washes to white where " +
                "layers overlap.");
        }

        [Test]
        public void Smoke_IsDarkAndAlphaBlended()
        {
            var smoke = Layer("fireball_smoke");

            Assert.IsFalse(smoke.additive, "Additive smoke brightens what it should be dimming.");
            Assert.IsNotEmpty(smoke.colorOverLife);
            Assert.Less(MaxChannel(smoke.colorOverLife[0].color), 0.6f,
                "Smoke must be visibly darker than the flame it trails. Bright smoke reads as " +
                "more fire and the silhouette disappears.");
        }

        [Test]
        public void Sparks_FallAwayFromTheFlightLine()
        {
            Assert.Greater(Layer("fireball_sparks").gravity, 0f,
                "Gravity on the embers is what makes the direction of travel readable — they " +
                "peel off the flight line instead of hanging around the orb.");
        }

        // ── Traps documented in the vfx-authoring skill ──────────────────────────

        [Test]
        public void EveryLayer_AuthorsItsAlphaOverLife()
        {
            foreach (var (id, v) in AllLayersById())
            {
                Assert.IsNotEmpty(v.alphaOverLife,
                    $"'{id}' has no alphaOverLife. Without it the emitter silently falls onto a " +
                    "hardcoded fade AND ignores colorOverLife entirely — the layer would keep " +
                    "its authored colours in the asset while not using them.");

                Assert.AreEqual(0f, v.alphaOverLife.Last().value, 1e-4f,
                    $"'{id}' must fade to nothing. Particles that pop out of existence at full " +
                    "alpha are the most visible cheapness in any effect.");
            }
        }

        [Test]
        public void EveryLoopingLayer_AuthorsItsSizeOverLife()
        {
            foreach (var (id, v) in AllLayersById())
            {
                if (!v.loops) continue;
                Assert.IsNotEmpty(v.sizeOverLife,
                    $"'{id}' loops and has no sizeOverLife, which switches the module OFF " +
                    "entirely: particles are born and die at the same size with no taper.");
            }
        }

        [Test]
        public void EveryCurve_SpansTheWholeLifetime()
        {
            foreach (var (id, v) in AllLayersById())
            {
                AssertCurveSpans(id, "alphaOverLife", v.alphaOverLife.Select(k => k.time));
                AssertCurveSpans(id, "sizeOverLife", v.sizeOverLife.Select(k => k.time));
                AssertCurveSpans(id, "colorOverLife", v.colorOverLife.Select(k => k.time));
            }
        }

        private static void AssertCurveSpans(string id, string curve, IEnumerable<float> times)
        {
            var list = times.ToList();
            if (list.Count == 0) return;

            Assert.AreEqual(0f, list.First(), 1e-4f,
                $"'{id}'.{curve} must start at t=0; a later first key leaves the opening of the " +
                "particle's life undefined.");
            Assert.AreEqual(1f, list.Last(), 1e-4f,
                $"'{id}'.{curve} must end at t=1, or the tail of the life is extrapolated.");
        }

        [Test]
        public void EveryLayer_IsBigEnoughToSee()
        {
            foreach (var (id, v) in AllLayersById())
                Assert.GreaterOrEqual(v.sizeMax, MIN_VISIBLE_SIZE,
                    $"'{id}' is smaller than {MIN_VISIBLE_SIZE} world units, which is under a " +
                    "pixel at 16 PPU. The original fireball was a stream of 1.5 px specks for " +
                    "exactly this reason.");
        }

        [Test]
        public void EveryLayer_HasATexture()
        {
            foreach (var (id, v) in AllLayersById())
                Assert.AreNotEqual(ParticleTextureShape.None, v.textureShape,
                    $"'{id}' would render as a hard-edged quad. Untextured particles are what " +
                    "the whole procedural texture library exists to avoid.");
        }

        // ── Movement and impact ──────────────────────────────────────────────────

        [Test]
        public void TrailLayers_LiveLongEnoughToLeaveAStreak()
        {
            foreach (var id in new[] { "fireball_wake", "fireball_sparks", "fireball_smoke" })
            {
                float length = Layer(id).lifespan * FIREBALL_SPEED;
                Assert.GreaterOrEqual(length, MIN_TRAIL_LENGTH,
                    $"'{id}' only stretches {length:0.0} units behind the projectile. Below " +
                    $"{MIN_TRAIL_LENGTH} the trail reads as a halo around the orb rather than as " +
                    "something travelling.");
            }
        }

        [Test]
        public void TrailLayers_KeepEmitting()
        {
            foreach (var (id, v) in TrailById())
                Assert.IsTrue(v.loops,
                    $"'{id}' must loop. A one-shot trail emits once at the muzzle and the " +
                    "projectile flies the rest of the way bare.");
        }

        [Test]
        public void Impact_Expands()
        {
            var impact = Impact;

            Assert.IsTrue(impact.Any(v => v.speed > 0f),
                "At least one impact layer must throw its particles outward. explosion_small " +
                "has speed 0, so its 24 particles spawn in a 0.1-unit sphere and sit there " +
                "fading — a blink, not a blast. That is the defect this guards against.");

            Assert.IsTrue(impact.Any(v => PeakSizeMultiplier(v) >= 1.4f),
                "At least one layer must grow substantially over its life. The expanding " +
                "shockwave ring is what gives the hit a size the player can read.");
        }

        [Test]
        public void CastLayers_AreOneShots()
        {
            foreach (var (id, v) in CastById())
                Assert.IsFalse(v.loops,
                    $"'{id}' must be a burst. A looping cast preset never stops: the emitter is " +
                    "spawned unparented at the caster on every shot and nothing ever despawns " +
                    "it, so they accumulate for the whole session.");
        }

        // ── Cost ─────────────────────────────────────────────────────────────────

        [Test]
        public void Trail_StaysWithinItsParticleBudget()
        {
            float live = Trail.Sum(LiveCount);

            Assert.LessOrEqual(live, 120f,
                $"The trail now costs {live:0} live particles per projectile, and the fireball " +
                "allows 20 instances. This ceiling is deliberately above the 60 the " +
                "vfx-authoring skill suggests for a player-attached emitter — the signature " +
                "spell gets to be expensive — but adding another layer without removing one " +
                "should be a decision, not a side effect.");
        }

        [Test]
        public void Impact_StaysWithinItsBurstBudget()
        {
            float peak = Impact.Where(v => !v.loops).Sum(v => v.count);

            Assert.LessOrEqual(peak, 200f,
                $"The impact peaks at {peak:0} particles. Sub-second, so the ceiling is generous, " +
                "but several fireballs landing together multiply it.");
        }

        [Test]
        public void Cast_StaysWithinItsBurstBudget()
        {
            float peak = Cast.Where(v => !v.loops).Sum(v => v.count);

            Assert.LessOrEqual(peak, 100f,
                $"The launch peaks at {peak:0} particles and fires on every cast, including " +
                "every shot of an automatic spell.");
        }

        // ── Enumeration helpers ──────────────────────────────────────────────────

        private IEnumerable<(string id, ParticleVfxParams vfx)> TrailById()
            => _fireball.CollectVfxPresets().Select(id => (id, _particles.GetById(id).vfx));

        private IEnumerable<(string id, ParticleVfxParams vfx)> CastById()
            => _fireball.CollectCastPresets().Select(id => (id, _particles.GetById(id).vfx));

        private IEnumerable<(string id, ParticleVfxParams vfx)> AllLayersById()
            => _fireball.CollectVfxPresets()
                .Concat(_fireball.CollectImpactPresets())
                .Concat(_fireball.CollectCastPresets())
                .Select(id => (id, _particles.GetById(id).vfx));
    }
}
