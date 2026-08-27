using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// End-to-end pins for the rebuilt <see cref="ArcaneFlameController"/>. The controller's
    /// geometry constants and <c>ElementalSprites</c> are <c>internal</c> to
    /// <c>Valkur.Gameplay</c> and this test assembly cannot see them (and must not gain an
    /// <c>InternalsVisibleTo</c>) — so every test here drives the PUBLIC surface
    /// (<see cref="ArcaneFlameController.Initialize"/>) and inspects the resulting child
    /// GameObjects, exactly the way CLAUDE.md's Corner16 lesson says a geometry/visual
    /// mismatch has to be caught: it is internally consistent while disagreeing, so nothing
    /// fails loudly until something reads the actual runtime shape.
    ///
    /// None of these tests call <c>Update()</c> — EditMode never ticks a MonoBehaviour on its
    /// own, and everything asserted here (child transforms, materials, sorting layers, the
    /// two Light2Ds) is set once by <c>Initialize</c> at build time.
    /// </summary>
    public class ArcaneFlameControllerTests
    {
        // Ring sprite's measured bright-band peak, given by the task brief as a fact about
        // the texture (not read from the internal ArcaneFlameController constant it mirrors,
        // ElementalSprites.RingPx) — so this test still catches a regression to that constant.
        private const float RingBrightBandNormalized = 0.78f;
        private const float TexelTolerance = 1f / 16f;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            // Building the rig creates SpriteRenderers/ParticleSystems/Light2Ds from scratch
            // in EditMode; suppress the renderer-init noise per the skill's gotcha #4.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private ArcaneFlameController CreateFlame(float radius)
        {
            var go = new GameObject("ArcaneFlameUnderTest");
            _spawned.Add(go);
            var flame = go.AddComponent<ArcaneFlameController>();
            flame.Initialize(
                duration: 5f,
                radius: radius,
                damagePerTick: 5,
                tickPeriod: 1f,
                targetLayers: 0,
                caster: null,
                element: SpellElement.Arcane);
            return flame;
        }

        private static T FindComponent<T>(GameObject root, string path) where T : Component
        {
            var t = root.transform.Find(path);
            Assert.IsTrue(t != null, $"Expected child '{path}' was not found under {root.name}.");
            var c = t.GetComponent<T>();
            Assert.IsTrue(c != null, $"Child '{path}' has no {typeof(T).Name}.");
            return c;
        }

        private static SpriteRenderer FindSprite(GameObject root, string path)
            => FindComponent<SpriteRenderer>(root, path);

        // ── 1. THE HEADLINE: crest == damage radius, at any scale ──────────────────────

        [TestCase(1.0f)]
        [TestCase(2.5f)]
        [TestCase(6.0f)]
        public void RingCrest_MatchesDamageRadius_AtAnyScale(float radius)
        {
            var flame = CreateFlame(radius);

            var runeStatic = FindSprite(flame.gameObject, "RuneStatic");
            var runeSpin = FindSprite(flame.gameObject, "Rune");

            // Every ElementalSprites sprite is exactly 1x1 world unit, so localScale.x is the
            // child's world DIAMETER and `* 0.5` is its world radius; the bright band sits at
            // RingBrightBandNormalized of THAT radius.
            float staticCrest = runeStatic.transform.localScale.x * 0.5f * RingBrightBandNormalized;
            float spinCrest = runeSpin.transform.localScale.x * 0.5f * RingBrightBandNormalized;

            Assert.That(staticCrest, Is.EqualTo(radius).Within(TexelTolerance),
                $"RuneStatic's bright band must sit on the damage radius ({radius}) within a " +
                "texel. Before this fix the crest sat at 60% of the damage circle, leaving 46% " +
                "of the hurting area with no readable pixel.");
            Assert.That(spinCrest, Is.EqualTo(radius).Within(TexelTolerance),
                $"Rune's bright band must sit on the damage radius ({radius}) within a texel.");
        }

        // ── 2. Root is never scaled ─────────────────────────────────────────────────────

        [TestCase(1.0f)]
        [TestCase(2.5f)]
        [TestCase(6.0f)]
        public void Root_NeverScaled_AtAnyRadius(float radius)
        {
            var flame = CreateFlame(radius);

            Assert.AreEqual(Vector3.one, flame.transform.localScale,
                "The root must stay at identity scale. Every child carries an absolute world " +
                "size derived from the radius; a scaled root is what made the old light render " +
                "2.5x its authored radius.");
        }

        // ── 3. The volume sits above entities, decals sit on the floor ──────────────────

        [Test]
        public void Layers_GroundDecalsOnFloor_VolumeAndParticlesOnVfx()
        {
            var flame = CreateFlame(2.5f);

            int floorId = SortingLayer.NameToID(SortingConfig.LAYER_FLOOR_DECALS);
            int vfxId = SortingLayer.NameToID(SortingConfig.LAYER_VFX);

            // The scorch is the ONLY ground mark. Being occluded by a wall is correct for it.
            var scorch = FindSprite(flame.gameObject, "Scorch");
            Assert.AreEqual(floorId, scorch.sortingLayerID,
                $"'Scorch' is a ground stain and must be on {SortingConfig.LAYER_FLOOR_DECALS}.");

            // The boundary rings are NOT ground decals, and this is a gameplay decision.
            // They are the only thing telling the player where the damage stops. Measured in
            // the shipped town, tree `Canopy` renderers sit on WallsTop (sorting value 8) and
            // building `Footprint` on WallsBottom (5), both far above FloorDecals (3) — on the
            // floor the ring rendered as a CRESCENT with its right half swallowed by a
            // building, recreating dynamically the very failure the crest fix removed.
            foreach (var name in new[] { "RuneStatic", "Rune" })
            {
                var sr = FindSprite(flame.gameObject, name);
                Assert.AreEqual(vfxId, sr.sortingLayerID,
                    $"'{name}' marks the damage boundary and must be on {SortingConfig.LAYER_VFX} " +
                    "so no wall, tree or building can hide where the fire hurts.");
            }

            foreach (var name in new[] { "Halo", "Glow", "Core", "HotCore", "Accent" })
            {
                var sr = FindSprite(flame.gameObject, name);
                Assert.AreEqual(vfxId, sr.sortingLayerID,
                    $"'{name}' is part of the additive volume and must be on " +
                    $"{SortingConfig.LAYER_VFX} so entities standing in the fire no longer " +
                    "occlude it.");
            }

            var motesRenderer = FindComponent<ParticleSystemRenderer>(flame.gameObject, "Motes");
            var hazeRenderer = FindComponent<ParticleSystemRenderer>(flame.gameObject, "HazeParticles");
            Assert.AreEqual(vfxId, motesRenderer.sortingLayerID,
                $"Motes must render on {SortingConfig.LAYER_VFX}, above entities.");
            Assert.AreEqual(vfxId, hazeRenderer.sortingLayerID,
                $"HazeParticles must render on {SortingConfig.LAYER_VFX}, above entities.");
        }

        // ── 4. The glow actually glows ───────────────────────────────────────────────────

        [Test]
        public void AdditiveVolume_UsesSupportedSpriteAdditiveShader()
        {
            var flame = CreateFlame(2.5f);

            foreach (var name in new[] { "Halo", "Glow", "Core", "HotCore", "Accent" })
            {
                var sr = FindSprite(flame.gameObject, name);
                var shader = sr.sharedMaterial != null ? sr.sharedMaterial.shader : null;
                Assert.IsTrue(shader != null, $"'{name}' has no material/shader.");
                Assert.AreEqual("Valkur/SpriteAdditive", shader.name,
                    $"'{name}' must carry the additive shader so it brightens rather than " +
                    "just alpha-blending.");
                Assert.IsTrue(shader.isSupported,
                    $"Valkur/SpriteAdditive must compile and be supported for '{name}' to " +
                    "render at all.");
            }
        }

        // ── 5. The motes are not white squares ───────────────────────────────────────────

        [Test]
        public void ParticleRenderers_CarryTexturedMaterial_NotUntexturedSquares()
        {
            var flame = CreateFlame(2.5f);

            var motesRenderer = FindComponent<ParticleSystemRenderer>(flame.gameObject, "Motes");
            var hazeRenderer = FindComponent<ParticleSystemRenderer>(flame.gameObject, "HazeParticles");

            Assert.IsTrue(motesRenderer.sharedMaterial != null, "Motes renderer has no material.");
            Assert.IsTrue(motesRenderer.sharedMaterial.mainTexture != null,
                "Motes must carry a texture — an untextured shared material draws hard white " +
                "squares, and a shared static lets an unrelated aura silently retexture it.");
            Assert.IsTrue(hazeRenderer.sharedMaterial != null, "HazeParticles renderer has no material.");
            Assert.IsTrue(hazeRenderer.sharedMaterial.mainTexture != null,
                "HazeParticles must carry a texture — same untextured-square failure mode.");
        }

        // ── 6. Particle budget ───────────────────────────────────────────────────────────

        [Test]
        public void ParticleBudget_StaysInsideAuraTrailBand_AndPoolsAreNotOversized()
        {
            var flame = CreateFlame(2.5f);

            var motes = FindComponent<ParticleSystem>(flame.gameObject, "Motes");
            var haze = FindComponent<ParticleSystem>(flame.gameObject, "HazeParticles");

            float motesSteady = motes.emission.rateOverTime.constant * motes.main.startLifetime.constant;
            float hazeSteady = haze.emission.rateOverTime.constant * haze.main.startLifetime.constant;
            float totalSteady = motesSteady + hazeSteady;

            Assert.LessOrEqual(totalSteady, 60f,
                $"Combined steady-state live particle count (rate x lifetime = {totalSteady:F1}) " +
                "must stay inside the 'player aura / trail <= 60' band from " +
                "vfx-authoring SKILL.md.");

            Assert.LessOrEqual(motes.main.maxParticles, motesSteady * 2.5f,
                $"Motes maxParticles ({motes.main.maxParticles}) reserves far more than its " +
                $"steady-state count ({motesSteady:F1}) — the old rig reserved 400 for ~22.");
            Assert.LessOrEqual(haze.main.maxParticles, hazeSteady * 2.5f,
                $"HazeParticles maxParticles ({haze.main.maxParticles}) reserves far more than " +
                $"its steady-state count ({hazeSteady:F1}).");
        }

        // ── 7. The light is built to the project's recipe ───────────────────────────────

        [Test]
        public void Light_BuiltToBodyPlusAdditiveCoreRecipe()
        {
            var flame = CreateFlame(2.5f);

            var lights = flame.GetComponentsInChildren<Light2D>(true);
            Assert.AreEqual(2, lights.Length,
                "Exactly two Light2Ds: a multiply body and an additive core.");

            var body = FindComponent<Light2D>(flame.gameObject, "FlameLight");
            var core = FindComponent<Light2D>(flame.gameObject, "FlameLight/Core");

            foreach (var l in lights)
            {
                Assert.AreEqual(Light2D.LightType.Point, l.lightType,
                    $"'{l.name}' must be a Point light, matching the world's torch/lamp recipe.");
                Assert.That(l.falloffIntensity, Is.InRange(0f, 1f),
                    $"'{l.name}' falloffIntensity must be inside [0,1].");
                Assert.That(l.transform.lossyScale.x, Is.EqualTo(1f).Within(0.001f),
                    $"'{l.name}' must not be counter-scaled — the root and every ancestor stay " +
                    "at identity, so no ellipse and no counter-scale math is needed.");
            }

            Assert.AreEqual(0, body.blendStyleIndex,
                "The body light must be on blend style 0 (Multiply) — it tints the ambient " +
                "buffer the way every other world light does.");
            Assert.AreEqual(1, core.blendStyleIndex,
                "The 'Core' child light must be on blend style 1 (Additive) — it is what makes " +
                "a multiply-buffer light read as emissive rather than as a stain.");
        }

        // ── 8. ISpellEffectDissipates is honoured ───────────────────────────────────────

        [Test]
        public void ISpellEffectDissipates_BeginDissipate_TakesOwnershipOnLiveInstance()
        {
            var flame = CreateFlame(2.5f);

            var dissipates = flame as ISpellEffectDissipates;
            Assert.IsNotNull(dissipates,
                "ArcaneFlameController must implement ISpellEffectDissipates, so an eviction " +
                "by SpellEffectRegistry closes it gracefully instead of cutting it in one frame.");
            Assert.IsTrue(dissipates.BeginDissipate(0.28f),
                "BeginDissipate must return true on a live, active instance — the registry " +
                "relies on that to skip its own Destroy and let the effect own its close.");
        }
    }
}
