using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Protects Iceball's visual identity. Fireball remains the structural benchmark,
    /// while these checks keep Iceball recognisably cold, crystalline, readable at its
    /// much higher travel speed, and safe to cast repeatedly.
    /// </summary>
    [TestFixture]
    public class IceballSignatureTests
    {
        private const string SpellCatalogPath =
            "Assets/_Project/Data/Catalogs/SpellCatalog.asset";
        private const string ParticleCatalogPath =
            "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        private SpellDefinition _iceball;
        private ParticlePresetCatalog _particles;

        [SetUp]
        public void SetUp()
        {
            var spells = AssetDatabase.LoadAssetAtPath<SpellCatalog>(SpellCatalogPath);
            _particles = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(ParticleCatalogPath);

            Assert.IsNotNull(spells, "SpellCatalog is missing.");
            Assert.IsNotNull(_particles, "ParticlePresetCatalog is missing.");

            _iceball = spells.AllSpells.FirstOrDefault(s => s != null && s.spellKey == "iceball");
            Assert.IsNotNull(_iceball, "iceball is not registered in SpellCatalog.");
        }

        [Test]
        public void KeepsFireballsLayeredVisualRhythm()
        {
            CollectionAssert.AreEqual(
                new[] { "iceball_core", "iceball_wake", "iceball_shards", "iceball_mist" },
                _iceball.CollectVfxPresets());

            CollectionAssert.AreEqual(
                new[]
                {
                    "iceball_impact_flash",
                    "iceball_impact_shockwave",
                    "iceball_impact_burst",
                    "iceball_impact_shards",
                    "iceball_impact_mist"
                },
                _iceball.CollectImpactPresets());

            CollectionAssert.AreEqual(
                new[] { "iceball_cast_flash", "iceball_cast_ring", "iceball_cast_shards" },
                _iceball.CollectCastPresets());
        }

        [Test]
        public void CoreTravelsWithProjectile_WhileTrailFreezesTheAirBehindIt()
        {
            Assert.IsFalse(Layer("iceball_core").worldSpace,
                "The bright core is the projectile and must travel with it.");

            foreach (var id in new[] { "iceball_wake", "iceball_shards", "iceball_mist" })
            {
                var layer = Layer(id);
                Assert.IsTrue(layer.worldSpace, $"'{id}' must be left in world space.");
                Assert.IsTrue(layer.loops, $"'{id}' must emit for the whole flight.");

                float spatialLength = layer.lifespan * _iceball.speed;
                Assert.GreaterOrEqual(spatialLength, 4f,
                    $"'{id}' only leaves {spatialLength:0.0} world units at Iceball's speed.");
            }
        }

        [Test]
        public void PaletteReadsAsLuminousIce()
        {
            var core = Layer("iceball_core");
            Assert.IsNotEmpty(core.colorOverLife);

            Color born = core.colorOverLife[0].color;
            Assert.GreaterOrEqual(Mathf.Min(born.r, Mathf.Min(born.g, born.b)), 0.85f,
                "Iceball needs a near-white frozen core, not a flat blue disc.");

            foreach (var (_, vfx) in AllLayers())
            foreach (var key in vfx.colorOverLife)
                Assert.GreaterOrEqual(key.color.b + 0.0001f, key.color.r,
                    "Iceball's authored gradients must remain blue/cyan dominant.");
        }

        [Test]
        public void UsesCrystalsAndMist_NotRecolouredFireSmoke()
        {
            Assert.AreEqual(ParticleTextureShape.Star, Layer("iceball_shards").textureShape);
            Assert.AreEqual(ParticleTextureShape.Star, Layer("iceball_impact_burst").textureShape);
            Assert.AreEqual(ParticleTextureShape.Spark, Layer("iceball_impact_shards").textureShape);

            var mist = Layer("iceball_mist");
            Assert.AreEqual(ParticleTextureShape.Smoke, mist.textureShape);
            Assert.IsFalse(mist.additive, "Frost mist supplies translucent mass behind the light.");
            Assert.IsTrue(Trail.Any(v => v.additive), "Iceball still needs emissive light layers.");
        }

        [Test]
        public void EveryLayerFadesCleanlyAndHasAProceduralTexture()
        {
            foreach (var (id, vfx) in AllLayers())
            {
                Assert.IsNotEmpty(vfx.alphaOverLife, $"'{id}' has no authored fade.");
                Assert.AreEqual(0f, vfx.alphaOverLife.Last().value, 0.0001f,
                    $"'{id}' must disappear smoothly instead of popping.");
                Assert.AreNotEqual(ParticleTextureShape.None, vfx.textureShape,
                    $"'{id}' would render as an untextured quad.");
                Assert.GreaterOrEqual(vfx.sizeMax, 0.05f,
                    $"'{id}' is smaller than a visible particle at 16 PPU.");
            }
        }

        [Test]
        public void ImpactExpandsAndAllLaunchEffectsAreOneShots()
        {
            Assert.IsTrue(Impact.Any(v => v.speed > 0f),
                "The frozen impact must throw fragments out from the contact point.");
            Assert.IsTrue(Impact.Any(v => PeakSizeMultiplier(v) >= 1.4f),
                "The freeze shockwave must visibly expand.");

            foreach (var (id, vfx) in CastById())
                Assert.IsFalse(vfx.loops, $"Cast layer '{id}' would leak one emitter per shot.");
        }

        [Test]
        public void ParticleCostStaysWithinSignatureSpellBudgets()
        {
            float liveTrail = Trail.Where(v => v.loops).Sum(v => v.emitRate * v.lifespan);
            float impactBurst = Impact.Where(v => !v.loops).Sum(v => v.count);
            float castBurst = Cast.Where(v => !v.loops).Sum(v => v.count);

            Assert.LessOrEqual(liveTrail, 120f, "Iceball's flight exceeds its live-particle budget.");
            Assert.LessOrEqual(impactBurst, 100f, "Iceball's impact exceeds its burst budget.");
            Assert.LessOrEqual(castBurst, 50f, "Iceball's launch exceeds its burst budget.");
        }

        private ParticleVfxParams Layer(string id)
        {
            var definition = _particles.GetById(id);
            Assert.IsNotNull(definition, $"Particle preset '{id}' is not registered.");
            Assert.IsNotNull(definition.vfx, $"Particle preset '{id}' has no VFX parameters.");
            return definition.vfx;
        }

        private List<ParticleVfxParams> Layers(IEnumerable<string> ids)
            => ids.Select(Layer).ToList();

        private List<ParticleVfxParams> Trail => Layers(_iceball.CollectVfxPresets());
        private List<ParticleVfxParams> Impact => Layers(_iceball.CollectImpactPresets());
        private List<ParticleVfxParams> Cast => Layers(_iceball.CollectCastPresets());

        private IEnumerable<(string id, ParticleVfxParams vfx)> CastById()
            => _iceball.CollectCastPresets().Select(id => (id, Layer(id)));

        private IEnumerable<(string id, ParticleVfxParams vfx)> AllLayers()
            => _iceball.CollectVfxPresets()
                .Concat(_iceball.CollectImpactPresets())
                .Concat(_iceball.CollectCastPresets())
                .Select(id => (id, Layer(id)));

        private static float PeakSizeMultiplier(ParticleVfxParams vfx)
            => vfx.sizeOverLife == null || vfx.sizeOverLife.Length == 0
                ? 1f
                : vfx.sizeOverLife.Max(key => key.value);
    }
}
