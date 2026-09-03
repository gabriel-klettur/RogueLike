using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

            foreach (var name in GroundBeds)
            {
                var sr = FindSprite(flame.gameObject, name);
                Assert.AreEqual(vfxId, sr.sortingLayerID,
                    $"'{name}' is the light the fire casts on the ground it burns, and must be " +
                    $"on {SortingConfig.LAYER_VFX} so entities standing in the fire no longer " +
                    "occlude it.");
            }

            foreach (var ps in Emitters(flame))
            {
                var psr = ps.GetComponent<ParticleSystemRenderer>();
                Assert.AreEqual(vfxId, psr.sortingLayerID,
                    $"'{ps.gameObject.name}' must render on {SortingConfig.LAYER_VFX}, above entities.");
            }
        }

        /// <summary>
        /// The two additive beds under the fire. They replaced the halo / glow / core /
        /// hot-core / accent stack, which drew a magic circle where the flames should be.
        /// </summary>
        private static readonly string[] GroundBeds = { "GroundGlow", "GroundHot" };

        /// <summary>
        /// Every emitter, found rather than named. How many layers the fire is built from is a
        /// tuning decision — naming them here would make adding a sixth silently exempt from
        /// the budget rule below.
        /// </summary>
        private static ParticleSystem[] Emitters(ArcaneFlameController flame)
        {
            var found = flame.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            Assert.Greater(found.Length, 0, "the fire is particles — the rig must build some.");
            return found;
        }

        // ── 4. The glow actually glows ───────────────────────────────────────────────────

        [Test]
        public void AdditiveVolume_UsesSupportedSpriteAdditiveShader()
        {
            var flame = CreateFlame(2.5f);

            foreach (var name in GroundBeds)
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

        // ── 5. The flames are not white squares ──────────────────────────────────────────

        [Test]
        public void ParticleRenderers_CarryTexturedMaterial_NotUntexturedSquares()
        {
            var flame = CreateFlame(2.5f);

            foreach (var ps in Emitters(flame))
            {
                var psr = ps.GetComponent<ParticleSystemRenderer>();
                Assert.IsTrue(psr.sharedMaterial != null, ps.gameObject.name + " renderer has no material.");
                Assert.IsTrue(psr.sharedMaterial.mainTexture != null,
                    ps.gameObject.name + " must carry a texture — an untextured shared material " +
                    "draws hard white squares, and a shared static lets an unrelated aura " +
                    "silently retexture it.");
            }
        }

        /// <summary>
        /// The flame layers are the shipped torch's recipe, and the four-stop ramp is the part
        /// that makes fire look like it is COOLING as it rises. Drop either middle stop and it
        /// becomes a coloured smear that fades out — which is what a two-stop gradient draws.
        /// </summary>
        [Test]
        public void FlameLayers_UseTheTorchsFourStopRamp()
        {
            var flame = CreateFlame(2.5f);

            foreach (var name in FlameLayers)
            {
                var ps = FindComponent<ParticleSystem>(flame.gameObject, name);
                Assert.IsTrue(ps.colorOverLifetime.enabled, name + " has no colour ramp at all.");

                var grad = ps.colorOverLifetime.color.gradient;
                Assert.IsNotNull(grad, name + " must ramp through a Gradient, not a flat colour.");
                Assert.GreaterOrEqual(grad.colorKeys.Length, 4,
                    name + " ramps through " + grad.colorKeys.Length + " colours; PP_torch_flame " +
                    "uses four and the middle two are what read as cooling.");

                // Hot to cool, measured on value: the first key must out-shine the last.
                float h, s, v0, v1;
                Color.RGBToHSV(grad.colorKeys[0].color, out h, out s, out v0);
                Color.RGBToHSV(grad.colorKeys[grad.colorKeys.Length - 1].color, out h, out s, out v1);
                Assert.Greater(v0, v1 + 0.3f,
                    name + " starts at value " + v0 + " and ends at " + v1 + " — a flame has to " +
                    "get darker as it rises or it is a light, not a fire.");
            }
        }

        /// <summary>
        /// Flames stay knee-high on a ~2.5 u character: a particle rises `velocity x lifetime`
        /// and is at most `size` across. That is why this rig needs no depth split — nothing in
        /// it is tall enough to paint out a body standing in the fire, which is the problem
        /// VortexFunnelFX had to answer with NECK_CLEAR_HEIGHT.
        /// </summary>
        [Test]
        public void FlamesStayLowEnoughNotToPaintOutAnEntityStandingInThem()
        {
            var flame = CreateFlame(2.5f);

            foreach (var name in FlameLayers)
            {
                var ps = FindComponent<ParticleSystem>(flame.gameObject, name);
                float rise = ps.velocityOverLifetime.y.constantMax * ps.main.startLifetime.constantMax;
                float top = rise + ps.main.startSizeY.constantMax * 0.5f;
                Assert.LessOrEqual(top, 1.6f,
                    name + " reaches " + top.ToString("F2") + " u against a 2.5 u character — at " +
                    "that height the fire covers whoever stands in it.");
            }
        }

        /// <summary>
        /// The silhouette comes from the QUAD and the softness from the TEXTURE, and both
        /// halves are load-bearing. A radially symmetric texture keeps every edge soft — the
        /// first rebuild used hard-edged tongue cut-outs and photographed as a scatter of
        /// violet cones — while the stretched quad is what makes a soft blob read as a flame
        /// instead of a bubble, which is what the round version photographed as.
        /// </summary>
        [Test]
        public void FlameLayers_AreSoftTexturesOnUprightQuads()
        {
            var flame = CreateFlame(2.5f);

            foreach (var name in FlameLayers)
            {
                var ps = FindComponent<ParticleSystem>(flame.gameObject, name);
                var main = ps.main;

                var tex = ps.GetComponent<ParticleSystemRenderer>().sharedMaterial.mainTexture;
                Assert.IsNotNull(tex, name + " has no texture.");
                Assert.AreEqual(tex.width, tex.height,
                    name + " draws a " + tex.width + "x" + tex.height + " texture. The falloff has " +
                    "to be radially symmetric — the shape is the quad's job, not the texture's.");

                Assert.IsTrue(main.startSize3D,
                    name + " draws square quads, so every particle is a bubble. Fire is vertical.");
                Assert.Greater(main.startSizeY.constantMin, main.startSizeX.constantMax * 1.5f,
                    name + " is only " + main.startSizeY.constantMin + " tall against " +
                    main.startSizeX.constantMax + " wide at worst — not upright enough to read " +
                    "as a lick.");

                // A stretched quad that turns stops pointing up, so neither rotation dial may
                // be opened. The waver is the noise module displacing the lick, not spinning it.
                Assert.Less(Mathf.Abs(main.startRotation.constantMax), 0.35f,
                    name + " starts up to " + main.startRotation.constantMax + " rad off vertical.");
                Assert.IsFalse(ps.rotationOverLifetime.enabled,
                    name + " spins its licks over their lifetime, which lies them on their side.");
            }
        }

        /// <summary>The two layers that are the fire itself, as opposed to its embers or smoke.</summary>
        private static readonly string[] FlameLayers = { "FlameBody", "FlameCore" };

        /// <summary>
        /// The fire must not promise ground that does not hurt. The emission disc plus the
        /// noise module's measured reach (~3.67 x strength x lifetime) has to land inside the
        /// damage radius the boundary ring draws.
        /// </summary>
        [Test]
        public void TheFireStaysInsideTheCircleThatDamages()
        {
            const float radius = 2.5f;
            var flame = CreateFlame(radius);

            foreach (var ps in Emitters(flame))
            {
                var noise = ps.noise;
                float wander = noise.enabled
                    ? 3.67f * noise.strengthX.constant * ps.main.startLifetime.constantMax
                    : 0f;
                float reach = ps.shape.radius + wander;
                Assert.LessOrEqual(reach, radius + 0.05f,
                    ps.gameObject.name + " reaches " + reach.ToString("F2") + " u from the centre " +
                    "of a " + radius + " u damage circle, so it draws fire on ground that is safe.");
            }
        }

        // ── 6. Particle budget ───────────────────────────────────────────────────────────

        [Test]
        public void ParticleBudget_StaysInsideAuraTrailBand_AndPoolsAreNotOversized()
        {
            var flame = CreateFlame(2.5f);
            // AT SUSTAIN, because emission is what the envelope drives. Straight after
            // Initialize every rate reads 0 — that is the ignition ramp seated before the
            // first render, and a budget measured there is a budget measured on an unlit fire.
            Sustain(flame);

            float totalSteady = 0f;
            foreach (var ps in Emitters(flame))
            {
                float steady = ps.emission.rateOverTime.constant * ps.main.startLifetime.constantMax;
                totalSteady += steady;
                Assert.LessOrEqual(ps.main.maxParticles, steady * 2.5f + 12f,
                    $"{ps.gameObject.name} maxParticles ({ps.main.maxParticles}) reserves far more " +
                    $"than its steady-state count ({steady:F1}) — the old rig reserved 400 for ~22.");
            }

            // ABOVE the skill's "player aura / trail <= 60" band and below its "signature
            // spell <= 120", deliberately. Photographed at 49.9 and again at 59.1, the licks
            // stopped overlapping and the patch read as scattered wisps rather than as ground
            // on fire. What pays for it: maxInstances is 1, the field lives five seconds on a
            // seven second cooldown, and there is never a second one on screen.
            Assert.LessOrEqual(totalSteady, 90f,
                $"Combined steady-state live particle count (rate x lifetime = {totalSteady:F1}) " +
                "is past what this rig is allowed. The bands are in vfx-authoring SKILL.md; " +
                "this one sits between 'player aura' and 'signature spell' on purpose.");
        }

        /// <summary>
        /// A bigger patch of fire has MORE flames, not taller ones, and it still has to stay
        /// affordable. The density scale is clamped for exactly this: without the cap, a radius
        /// left in the old pixel scale would have walked the rig straight out of its budget.
        /// </summary>
        [TestCase(1.0f)]
        [TestCase(6.0f)]
        public void ParticleBudget_SurvivesAnyAuthoredRadius(float radius)
        {
            var flame = CreateFlame(radius);
            Sustain(flame);

            float totalSteady = 0f;
            foreach (var ps in Emitters(flame))
                totalSteady += ps.emission.rateOverTime.constant * ps.main.startLifetime.constantMax;

            Assert.LessOrEqual(totalSteady, 140f,
                $"At radius {radius} the rig asks for {totalSteady:F1} live particles.");
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

        // ── 9. The per-frame envelope ──────────────────────────────────────────────────
        //
        // The two tests below are the only ones here that reach past the public surface. What
        // they measure only exists once the controller has run a frame, and EditMode never
        // ticks a MonoBehaviour on its own — so the alternative is a PlayMode fixture for two
        // assertions, or leaving both behaviours unguarded. The fields and methods they touch
        // are the controller's own, in one file each, and named in its doc comments.

        private const BindingFlags Inner = BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>Run the rig forward to full burn: past the ignition ramp, no pulse.</summary>
        private static void Sustain(ArcaneFlameController flame)
            => Tick(flame, age: 2f, remaining: 3f, pulse: 0f);

        /// <summary>Drive one frame of the rig at a chosen age, remaining life and pulse.</summary>
        private static void Tick(ArcaneFlameController flame, float age, float remaining, float pulse)
        {
            var t = typeof(ArcaneFlameController);
            t.GetField("_age", Inner).SetValue(flame, age);
            t.GetField("_remaining", Inner).SetValue(flame, remaining);
            t.GetField("_pulsePhase", Inner).SetValue(flame, pulse);
            t.GetMethod("AnimateVisuals", Inner).Invoke(flame, new object[] { 0.016f });
        }

        /// <summary>Summed alpha of the additive volume — what a pixel at the centre receives.</summary>
        private static float AdditiveAlphaSum(GameObject root)
        {
            float sum = 0f;
            foreach (Transform child in root.transform)
            {
                var sr = child.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sharedMaterial == null) continue;
                if (!sr.sharedMaterial.shader.name.Contains("Additive")) continue;
                sum += sr.color.a;
            }
            return sum;
        }

        [Test]
        public void AConnectingTickBrightensTheVolume_NotOnlyTheLight()
        {
            var flame = CreateFlame(2.5f);

            Tick(flame, age: 2f, remaining: 3f, pulse: 0f);
            float calm = AdditiveAlphaSum(flame.gameObject);

            Tick(flame, age: 2f, remaining: 3f, pulse: 1f);
            float hit = AdditiveAlphaSum(flame.gameObject);

            // The pulse used to move SCALE alone: measured, this sum came back identical at
            // 2.274 before and after a tick, so a hit changed nothing on the volume and read
            // only on the Light2D — which in daylight is the half that reads least.
            Assert.Greater(hit, calm + 0.05f,
                "a connecting tick must brighten the additive volume (calm " + calm +
                " -> hit " + hit + ")");

            // And it must stay a punctuation mark. Additive layers STACK toward white, so the
            // same dial that makes the hit readable washes the arcane colour out of the centre
            // if it is turned up — the failure recorded for VortexFunnelFX's band count.
            Assert.Less(hit, 3f,
                "summed additive alpha " + hit + " blows the centre out to flat white, which " +
                "costs the spell the one thing separating it from the blue vortex at a glance");
        }

        [Test]
        public void BoundaryRingsAreRecycled_NotRebuiltEveryTick()
        {
            var flame = CreateFlame(2.5f);
            var t = typeof(ArcaneFlameController);
            var spawn = t.GetMethod("SpawnBoundaryRing", Inner);
            var animate = t.GetMethod("AnimateBoundaryRings", Inner);

            int Rings() => flame.GetComponentsInChildren<SpriteRenderer>(includeInactive: true)
                .Count(sr => sr.gameObject.name == "BoundaryRing");

            spawn.Invoke(flame, null);
            Assert.AreEqual(1, Rings(), "the first connecting tick must draw a boundary ring");

            // Expire it, then ask for another. One ring is spawned per connecting tick — about
            // eight over a cast — and the old path minted a GameObject + SpriteRenderer for
            // each and destroyed it 0.34 s later.
            animate.Invoke(flame, new object[] { 5f });
            spawn.Invoke(flame, null);

            Assert.AreEqual(1, Rings(),
                "an expired boundary ring must be parked and reused, not destroyed and rebuilt");
        }
    }
}
