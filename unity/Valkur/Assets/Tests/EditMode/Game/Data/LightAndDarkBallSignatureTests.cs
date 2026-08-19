using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Protects the complementary visual identities of Lightball and Darkball while
    /// preserving the layered launch / flight / impact rhythm established by Fireball.
    /// </summary>
    [TestFixture]
    public class LightAndDarkBallSignatureTests
    {
        private const string SpellCatalogPath =
            "Assets/_Project/Data/Catalogs/SpellCatalog.asset";
        private const string ParticleCatalogPath =
            "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        /// <summary>
        /// Shortest lifespan that still reads as a trail rather than a flicker — about two
        /// frames at 60 fps. A perception limit, not a tuning knob, which is why it is the only
        /// absolute duration in this fixture.
        /// </summary>
        private const float MIN_TRAIL_SECONDS = 1f / 30f;

        /// <summary>
        /// How far past the projectile's own flight time a trail layer may live. Above this the
        /// trail is still hanging in the air after the impact has played. Expressed as a
        /// multiple of flight time so it follows any change to speed or range.
        /// </summary>
        private const float MAX_TRAIL_FLIGHTS = 1.5f;

        private SpellDefinition _lightball;
        private SpellDefinition _darkball;
        private ParticlePresetCatalog _particles;

        [SetUp]
        public void SetUp()
        {
            var spells = AssetDatabase.LoadAssetAtPath<SpellCatalog>(SpellCatalogPath);
            _particles = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(ParticleCatalogPath);

            Assert.IsNotNull(spells, "SpellCatalog is missing.");
            Assert.IsNotNull(_particles, "ParticlePresetCatalog is missing.");

            _lightball = spells.AllSpells.FirstOrDefault(s => s != null && s.spellKey == "lightball");
            _darkball = spells.AllSpells.FirstOrDefault(s => s != null && s.spellKey == "darkball");
            Assert.IsNotNull(_lightball, "lightball is not registered in SpellCatalog.");
            Assert.IsNotNull(_darkball, "darkball is not registered in SpellCatalog.");
        }

        [Test]
        public void LightballKeepsItsRadiantLayerStack()
        {
            Assert.AreEqual("Light", _lightball.element);
            CollectionAssert.AreEqual(
                new[] { "lightball_core", "lightball_wake", "lightball_motes", "lightball_radiance" },
                _lightball.CollectVfxPresets());
            CollectionAssert.AreEqual(
                new[]
                {
                    "lightball_impact_flash", "lightball_impact_shockwave",
                    "lightball_impact_burst", "lightball_impact_rays",
                    "lightball_impact_radiance"
                },
                _lightball.CollectImpactPresets());
            CollectionAssert.AreEqual(
                new[] { "lightball_cast_flash", "lightball_cast_ring", "lightball_cast_motes" },
                _lightball.CollectCastPresets());
        }

        [Test]
        public void DarkballKeepsItsVoidLayerStack()
        {
            Assert.AreEqual("Dark", _darkball.element);
            CollectionAssert.AreEqual(
                new[] { "darkball_core", "darkball_wake", "darkball_fragments", "darkball_smoke" },
                _darkball.CollectVfxPresets());
            CollectionAssert.AreEqual(
                new[]
                {
                    "darkball_impact_flash", "darkball_impact_shockwave",
                    "darkball_impact_burst", "darkball_impact_fragments",
                    "darkball_impact_smoke"
                },
                _darkball.CollectImpactPresets());
            CollectionAssert.AreEqual(
                new[] { "darkball_cast_flash", "darkball_cast_ring", "darkball_cast_fragments" },
                _darkball.CollectCastPresets());
        }

        [TestCase("lightball")]
        [TestCase("darkball")]
        public void CoreTravelsWithTheProjectile_AndTheOtherFlightLayersStayBehind(string key)
        {
            var spell = Spell(key);
            var ids = spell.CollectVfxPresets();

            Assert.IsFalse(Layer(ids[0]).worldSpace, "The ball core must travel with the projectile.");

            // Everything below is measured against the spell's OWN flight time, never against
            // an absolute world length.
            //
            // The previous version asserted each trail was 8..25 world units long. Those
            // numbers were Fireball's — 16 u/s over 15 units — and applying them to every ball
            // made two tunables load-bearing that have nothing to do with whether the effect
            // works: a faster, shorter-ranged ball produces a much shorter trail from exactly
            // the same visual design, so Lightball failed at 1.65 u and Darkball at 6.0 u while
            // both looked correct. A designer changing `speed` broke a test about trails.
            //
            // What actually makes a layered trail read is structural and scale-free, so that is
            // what is pinned here.
            float flightTime = spell.speed > 0f ? spell.range / spell.speed : 0f;
            Assert.Greater(flightTime, 0f,
                $"'{key}' has no speed or no range, so there is no flight for a trail to sit in.");

            float previousLifespan = 0f;
            string previousId = ids[0];

            foreach (var id in ids.Skip(1))
            {
                var vfx = Layer(id);
                Assert.IsTrue(vfx.worldSpace, $"'{id}' must remain in world space to form a trail.");
                Assert.IsTrue(vfx.loops, $"'{id}' must emit for the whole flight.");

                // Visible at all. A perception floor rather than an art-direction choice:
                // below roughly two frames the layer strobes instead of trailing, whatever
                // the intent. This is the one absolute here, and it is about eyes, not taste.
                Assert.Greater(vfx.lifespan, MIN_TRAIL_SECONDS,
                    $"'{id}' lives {vfx.lifespan:0.000}s — under about two frames, so it reads " +
                    "as a flicker rather than as a trail.");

                // Does not outlive the projectile. Derived from this spell's own range and
                // speed, so it moves when they do; a trail much longer than the flight is
                // still hanging in the air well after the impact has played.
                Assert.LessOrEqual(vfx.lifespan, flightTime * MAX_TRAIL_FLIGHTS,
                    $"'{id}' lives {vfx.lifespan:0.000}s against a {flightTime:0.000}s flight, so " +
                    "the trail is still on screen long after the ball has landed.");

                // The ladder. Each successive flight layer outliving the last is what gives the
                // trail depth instead of one uniform smear, and it is the property all three
                // ball spells share regardless of how fast or far any of them travels.
                Assert.Greater(vfx.lifespan, previousLifespan,
                    $"'{id}' ({vfx.lifespan:0.000}s) does not outlive '{previousId}' " +
                    $"({previousLifespan:0.000}s). The flight layers are ordered shortest to " +
                    "longest so the trail fades through them; equal or inverted lifespans " +
                    "collapse them into a single band.");

                previousLifespan = vfx.lifespan;
                previousId = id;
            }
        }

        [Test]
        public void LightballIsWhiteGold_NotARecolouredFireball()
        {
            var core = Layer("lightball_core");
            Color born = core.colorOverLife[0].color;
            Assert.GreaterOrEqual(Mathf.Min(born.r, Mathf.Min(born.g, born.b)), 0.95f,
                "The radiant core must be born white-hot.");

            foreach (var (_, vfx) in AllLayers(_lightball))
            foreach (var key in vfx.colorOverLife)
                Assert.GreaterOrEqual(key.color.g + 0.0001f, key.color.r * 0.7f,
                    "Lightball must remain white/gold instead of aging into Fireball orange.");

            Assert.AreEqual(ParticleTextureShape.Star, Layer("lightball_motes").textureShape);
            Assert.AreEqual(ParticleTextureShape.Spark, Layer("lightball_impact_rays").textureShape);
            Assert.IsFalse(Layer("lightball_radiance").additive,
                "A translucent radiance layer gives the luminous stack readable mass.");
        }

        [Test]
        public void DarkballHasAnOpaqueVoidCore_WithAnEmissivePurpleCorona()
        {
            var core = Layer("darkball_core");
            Color born = core.colorOverLife[0].color;
            Assert.Less(Mathf.Max(born.r, Mathf.Max(born.g, born.b)), 0.2f,
                "The centre must read as consumed light, not as another bright projectile.");
            Assert.IsFalse(core.additive, "An additive core cannot become visually black.");

            Assert.IsTrue(Layer("darkball_wake").additive,
                "The violet corona supplies the silhouette around the void core.");
            Assert.IsFalse(Layer("darkball_smoke").additive,
                "Dark smoke must occlude rather than brighten the background.");
            Assert.AreEqual(ParticleTextureShape.Star, Layer("darkball_fragments").textureShape);
            Assert.AreEqual(ParticleTextureShape.Smoke, Layer("darkball_smoke").textureShape);
        }

        [TestCase("lightball")]
        [TestCase("darkball")]
        public void EveryLayerFadesCleanlyAndUsesAProceduralTexture(string key)
        {
            foreach (var (id, vfx) in AllLayers(Spell(key)))
            {
                Assert.IsNotEmpty(vfx.alphaOverLife, $"'{id}' has no authored fade.");
                Assert.AreEqual(0f, vfx.alphaOverLife.Last().value, 0.0001f,
                    $"'{id}' must disappear smoothly instead of popping.");
                Assert.AreNotEqual(ParticleTextureShape.None, vfx.textureShape,
                    $"'{id}' would render as a hard-edged quad.");
                Assert.GreaterOrEqual(vfx.sizeMax, 0.05f,
                    $"'{id}' is too small to remain visible at 16 PPU.");
            }
        }

        [TestCase("lightball")]
        [TestCase("darkball")]
        public void ImpactExpands_AndCastLayersAreFinite(string key)
        {
            var spell = Spell(key);
            var impact = Layers(spell.CollectImpactPresets());

            Assert.IsTrue(impact.Any(v => v.speed > 0f), "The impact must throw particles outward.");
            Assert.IsTrue(impact.Any(v => PeakSizeMultiplier(v) >= 1.4f),
                "The impact needs an expanding shockwave.");

            foreach (var id in spell.CollectCastPresets())
                Assert.IsFalse(Layer(id).loops, $"Cast layer '{id}' would leak one emitter per shot.");
        }

        [TestCase("lightball", 80f)]
        [TestCase("darkball", 120f)]
        public void FlightAndBurstCostsStayWithinBudget(string key, float flightBudget)
        {
            var spell = Spell(key);
            float liveFlight = Layers(spell.CollectVfxPresets()).Sum(v => v.emitRate * v.lifespan);
            float impactBurst = Layers(spell.CollectImpactPresets()).Sum(v => v.count);
            float castBurst = Layers(spell.CollectCastPresets()).Sum(v => v.count);

            Assert.LessOrEqual(liveFlight, flightBudget, $"{key} exceeds its live flight budget.");
            Assert.LessOrEqual(impactBurst, 100f, $"{key} exceeds its impact burst budget.");
            Assert.LessOrEqual(castBurst, 50f, $"{key} exceeds its cast burst budget.");
        }

        private SpellDefinition Spell(string key) => key == "lightball" ? _lightball : _darkball;

        private ParticleVfxParams Layer(string id)
        {
            var definition = _particles.GetById(id);
            Assert.IsNotNull(definition, $"Particle preset '{id}' is not registered.");
            Assert.IsNotNull(definition.vfx, $"Particle preset '{id}' has no VFX parameters.");
            return definition.vfx;
        }

        private List<ParticleVfxParams> Layers(IEnumerable<string> ids)
            => ids.Select(Layer).ToList();

        private IEnumerable<(string id, ParticleVfxParams vfx)> AllLayers(SpellDefinition spell)
            => spell.CollectVfxPresets()
                .Concat(spell.CollectImpactPresets())
                .Concat(spell.CollectCastPresets())
                .Select(id => (id, Layer(id)));

        private static float PeakSizeMultiplier(ParticleVfxParams vfx)
            => vfx.sizeOverLife == null || vfx.sizeOverLife.Length == 0
                ? 1f
                : vfx.sizeOverLife.Max(key => key.value);
    }
}
