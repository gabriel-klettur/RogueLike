using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// The cone breath. Everything here guards something that looked like a working spell
    /// while being the wrong one: a reach carried over from the Python build in its own units,
    /// particles emitted at a mirror of the aim, an untextured material on a particle renderer,
    /// a silhouette drawn as a wire outline, and a sound id that never existed.
    ///
    /// <para>Each measurement is a COMPOSITION, not one half of one: the drawn wedge is
    /// checked against the queried wedge, and the aim against the emitter's own forward — the
    /// discipline the spawner coordinate-space incident prescribes, because both halves of
    /// every one of these defects was internally consistent and disagreed only on screen.</para>
    /// </summary>
    public class FlameConeTests
    {
        private const string Folder = "Assets/_Project/Data/Catalogs/Spells/";
        private const string Key = "flame_breath";

        /// <summary>Camera width in world units at the shipped ortho size and 2:1 viewport.</summary>
        private const float ScreenWidthWorldUnits = 33.33f;

        private static readonly string ScriptsRoot =
            Path.GetFullPath(Path.Combine(Application.dataPath, "_Project", "Scripts"));

        private static string Source(string relative)
            => File.ReadAllText(Path.Combine(ScriptsRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static SpellDefinition Load(string key)
            => AssetDatabase.LoadAssetAtPath<SpellDefinition>(Folder + key + ".asset");

        private FlameConeFX Rig(Vector2 aim, float length, float arc, out GameObject host)
        {
            host = new GameObject("FlameConeProbe");
            _spawned.Add(host);
            var fx = FlameConeFX.Attach(host.transform, aim, length, arc, new Color(1f, 0.48f, 0.12f, 1f));
            // Past the ignition ramp, so what is measured is the fire the player sees rather
            // than the dark frame before it catches.
            int frames = Mathf.CeilToInt(FlameConeFX.IGNITE_SECONDS * 60f) + 10;
            for (int i = 0; i < frames; i++) fx.Tick(1f / 60f, 99f);
            return fx;
        }

        // ── The data ─────────────────────────────────────────────────────────────────

        [Test]
        public void TheReachIsInWorldUnitsAndIsWorthCasting()
        {
            // THE PYTHON-UNITS REGRESSION, fifth sighting. The spell shipped coneLength 16.25
            // against an executor that divided by 16, so the breath reached 1.02 units — three
            // per cent of the screen, shorter than the caster's own sprite is tall. The tell
            // was that the executor's own fallback for an unauthored field was 16.25 WORLD
            // units, sixteen times anything the asset could produce; the ice wall's fallbacks
            // were thirty times larger for exactly the same reason.
            var spell = Load(Key);
            Assert.IsNotNull(spell, Key + " is missing");

            Assert.Greater(spell.coneLength, 2f,
                "coneLength " + spell.coneLength + " reaches barely past the caster. A value "
                + "near 16.25 with a divide in the executor is how this shipped for years.");
            Assert.Less(spell.coneLength, ScreenWidthWorldUnits * 0.5f,
                "coneLength " + spell.coneLength + " is over half a screen. A breath is not a beam.");
        }

        [Test]
        public void TheExecutorDoesNotDivideTheAuthoredReach()
        {
            // The composition, not either half. The asset says world units and the executor
            // must agree, or re-authoring one silently undoes the other.
            var spell = Load(Key);
            var fx = Rig(Vector2.right, spell.coneLength, spell.coneArc, out _);

            Assert.AreEqual(spell.coneLength, fx.Length, 0.001f,
                "The rig was built at " + fx.Length + " from an authored " + spell.coneLength
                + ". A factor of 16 here is the pixel scale coming back.");
        }

        [Test]
        public void TheCooldownOutlastsTheBreath()
        {
            // maxInstances 1 with a cooldown shorter than the duration means the player always
            // has one out AND can evict their own to reposition it — the balance trap the
            // vortex's eight-second field walked into.
            var spell = Load(Key);
            Assert.GreaterOrEqual(spell.cooldownDuration, spell.duration,
                "cooldown " + spell.cooldownDuration + " is shorter than the " + spell.duration
                + "s it breathes for, so the spell is permanently up.");
        }

        [Test]
        public void TheSwatchIsAuthoredSoTheFireIsNotFallbackColoured()
        {
            // Opaque white is the project-wide "nobody authored this" sentinel, and a fire
            // breath that hits it is drawn in KiPalette's pale blue-white fallback.
            var spell = Load(Key);
            Assert.IsFalse(KiPalette.IsUnauthored(spell.particleColor),
                "particleColor is the unauthored sentinel, so the cone falls back off-element.");
            Assert.Greater(spell.particleColor.r, spell.particleColor.b,
                "a flame breath's swatch should be warm; got " + spell.particleColor);
        }

        // ── The aim ──────────────────────────────────────────────────────────────────

        [Test]
        public void ParticlesAreEmittedAlongTheAimInEveryDirection()
        {
            // THE MIRROR BUG. The old controller oriented its emitter with a hand-derived
            // Euler(deg - 90, 90, 0), which reflects the aim about the 45 degree diagonal:
            // measured, east emitted north, north emitted east, and 135 and 315 came out
            // exactly reversed. Only 45 and 225 were right, which is why it survived — someone
            // testing a diagonal would have seen nothing wrong.
            for (int deg = 0; deg < 360; deg += 15)
            {
                var aim = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
                var fx = Rig(aim, 5.5f, 60f, out _);

                Vector3 forward = fx.EmitterRoot.forward;
                Assert.AreEqual(aim.x, forward.x, 0.001f, "emitter x at " + deg + " degrees");
                Assert.AreEqual(aim.y, forward.y, 0.001f, "emitter y at " + deg + " degrees");
            }
        }

        [Test]
        public void TheWedgeIsTurnedAboutZOnlySoItsSpritesFaceTheCamera()
        {
            // A sprite's quad lies in its own XY plane. Handing the sprite parent the emitter's
            // LookRotation would put every slice edge-on to the camera, which is invisible
            // rather than wrong-looking — the failure mode that never gets reported.
            var fx = Rig(new Vector2(0.6f, -0.8f), 5.5f, 60f, out _);
            Vector3 euler = fx.SpriteRoot.localRotation.eulerAngles;

            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, euler.x), 0.001f, "sprite root pitched off the camera plane");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, euler.y), 0.001f, "sprite root yawed off the camera plane");
            Assert.AreEqual(0f, Mathf.DeltaAngle(-53.13f, euler.z), 0.05f, "sprite root is not aimed along the cone");
        }

        // ── The silhouette ───────────────────────────────────────────────────────────

        [Test]
        public void TheWedgeIsFilledAndWidensWithDistance()
        {
            // The old cone was a twelve-point LineRenderer: origin, an arc, back to origin. A
            // strip can draw a boundary and can never fill one, so the whole silhouette was two
            // thin strokes and a curve. A filled wedge means slices, and each has to be wider
            // than the last or the shape is a tube.
            var fx = Rig(Vector2.right, 5.5f, 60f, out var host);

            var near = host.transform.Find("Wedge/Body00");
            var far = host.transform.Find("Wedge/Body" + (fx.SliceCount - 1).ToString("00"));
            Assert.IsNotNull(near, "no near slice");
            Assert.IsNotNull(far, "no far slice");

            Assert.Greater(far.localScale.y, near.localScale.y * 2f,
                "the far slice is " + far.localScale.y + " across against " + near.localScale.y
                + " at the mouth — that is a tube, not a cone.");
            Assert.Greater(far.localPosition.x, near.localPosition.x,
                "the slices are not laid out along the aim");
        }

        [Test]
        public void TheDrawnWidthIsTheQueriedWidth()
        {
            // THE CONTRACT. A rig whose reach is decorative is the failure the vortex's ground
            // ring exists to prevent: the edge the player reads has to be the edge that hurts.
            // Both the slices and ConeBreathController.InsideCone come off HalfWidthAt.
            var fx = Rig(Vector2.right, 5.5f, 60f, out var host);

            for (int i = 0; i < fx.SliceCount; i++)
            {
                var slice = host.transform.Find("Wedge/Body" + i.ToString("00"));
                float drawn = slice.localScale.y * 0.5f;
                float queried = fx.HalfWidthAt(slice.localPosition.x);

                // The flicker breathes the drawn width around the queried one; what must not
                // happen is the two being different SHAPES.
                Assert.AreEqual(queried, drawn, queried * 0.35f + 0.01f,
                    "slice " + i + " is drawn " + drawn + " half-wide where the damage query uses " + queried);
            }
        }

        [Test]
        public void TheStackedBrightnessDoesNotScaleWithTheSliceCount()
        {
            // On an additive stack a pixel receives the SUM of everything over it, so a slice
            // count is a brightness dial unless the per-slice alpha is divided by it. The
            // vortex's bands washed a red funnel out to white when their count doubled.
            var fx = Rig(Vector2.right, 5.5f, 60f, out var host);

            float summed = 0f;
            for (int i = 0; i < fx.SliceCount; i++)
                summed += host.transform.Find("Wedge/Body" + i.ToString("00"))
                              .GetComponent<SpriteRenderer>().color.a;

            Assert.Less(summed, 4.0f,
                "summed body alpha " + summed + " will blow the cone out to white and cost it its colour");
            Assert.Greater(summed, 0.8f, "summed body alpha " + summed + " is too faint to read");
        }

        [Test]
        public void TheRigNeverScalesTheTransformItsLightHangsFrom()
        {
            // A Light2D under a scaled transform renders at authored radius times that scale.
            // That is how the old vortex lit 367 world units off a 21-unit light, and the old
            // cone breath is the same family of bug in the opposite direction.
            var fx = Rig(Vector2.right, 5.5f, 60f, out var host);

            var light = host.transform.Find("Wedge/BreathLight");
            if (light == null) Assert.Ignore("URP Light2D is not resolvable in this context");
            Assert.AreEqual(1f, light.lossyScale.x, 0.001f, "the light's lossy X scale is not 1");
            Assert.AreEqual(1f, light.lossyScale.y, 0.001f, "the light's lossy Y scale is not 1");
            Assert.Greater(light.localPosition.x, fx.Length * 0.2f,
                "the light sits on the caster's hands rather than inside the fire");
        }

        // ── The materials ────────────────────────────────────────────────────────────

        [Test]
        public void EveryParticleRendererCarriesItsOwnTexture()
        {
            // A material handed to a ParticleSystemRenderer must carry a texture: a
            // SpriteRenderer supplies one and a particle renderer does not. The old rig
            // assigned ElementalSprites.SharedUnlitMaterial, whose mainTexture is null —
            // measured — so every particle drew as a hard opaque quad with no falloff.
            Rig(Vector2.right, 5.5f, 60f, out var host);

            var renderers = host.GetComponentsInChildren<ParticleSystemRenderer>(true);
            Assert.Greater(renderers.Length, 0, "the breath emits nothing");

            foreach (var psr in renderers)
            {
                Assert.IsNotNull(psr.sharedMaterial, psr.name + " has no material");
                Assert.IsNotNull(psr.sharedMaterial.mainTexture,
                    psr.name + " draws untextured quads — the SharedUnlitMaterial regression.");
            }
        }

        [Test]
        public void TheGroundScorchIsTheOneNonAdditiveLayer()
        {
            // One opaque layer is what separates "affecting the world" from "lit". Folding it
            // into the shared additive material as a tidy-up makes a dark chip add almost
            // nothing, so the layer vanishes with nothing failing — the note KiAuraFX and
            // VortexFunnelFX both carry about their own ground debris.
            Rig(Vector2.right, 5.5f, 60f, out var host);

            var scorch = host.transform.Find("GroundPlane/ScorchAim/Scorch");
            Assert.IsNotNull(scorch, "no ground scorch");

            var sr = scorch.GetComponent<SpriteRenderer>();
            Assert.AreNotSame(ElementalSprites.SharedAdditiveMaterial, sr.sharedMaterial,
                "the scorch is additive, so it adds darkness to nothing");
            Assert.Less(sr.color.r + sr.color.g + sr.color.b, 0.6f,
                "the scorch is not dark, so it reads as another glow rather than as burnt ground");
        }

        [Test]
        public void TheGroundLayerIsSquashedByOneParentWithTheRotationBelowIt()
        {
            // Squashing each item individually foreshortens its LENGTH without turning its
            // direction, so it slides across the floor instead of lying on it. Rotation on the
            // child, squash on the parent — the split the vortex's ground layer already makes.
            Rig(Vector2.right, 5.5f, 60f, out var host);

            var plane = host.transform.Find("GroundPlane");
            var aim = host.transform.Find("GroundPlane/ScorchAim");
            Assert.IsNotNull(plane, "no ground plane");
            Assert.Less(plane.localScale.y, plane.localScale.x,
                "the ground plane is not flattened, so the scorch stands up like a wall");
            Assert.AreEqual(Vector3.one, aim.localScale,
                "the rotating child carries a scale of its own, which shears the ellipse");
        }

        // ── The intensity ────────────────────────────────────────────────────────────

        /// <summary>
        /// Sum of every body slice's premultiplied contribution — what an additive surface
        /// under the whole wedge actually receives.
        /// </summary>
        private static Vector3 BodyContribution(GameObject host, int slices)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < slices; i++)
            {
                var c = host.transform.Find("Wedge/Body" + i.ToString("00")).GetComponent<SpriteRenderer>().color;
                sum += new Vector3(c.r * c.a, c.g * c.a, c.b * c.a);
            }
            return sum;
        }

        [Test]
        public void TheFireBurnsRedRatherThanCream()
        {
            // THE PALE-RAMP REGRESSION. The body used to run from KiPalette.Core to
            // KiPalette.Edge, and for the shipped orange those measure saturation 0.25 (nearly
            // white) and value 0.62 (nearly dark). So the cone was washed out exactly where it
            // was brightest and dim exactly where it had colour, and the whole wedge summed to
            // (2.265, 1.367, 0.745) — green at 60 per cent of red, which is cream, not fire.
            var fx = Rig(Vector2.right, 5.5f, 60f, out var host);
            Vector3 sum = BodyContribution(host, fx.SliceCount);

            Assert.Greater(sum.y / sum.x, 0.10f, "the flame has gone monochrome red; it needs a hot centre");
            Assert.Less(sum.y / sum.x, 0.45f,
                "green is " + (sum.y / sum.x).ToString("F2") + " of red — that is cream, not flame");
            Assert.Less(sum.z / sum.x, 0.18f,
                "blue is " + (sum.z / sum.x).ToString("F2") + " of red — the fire is washing out");
        }

        [Test]
        public void TheWedgeIsOverdrivenIntoHdrRatherThanBrightenedWithAlpha()
        {
            // On an additive surface alpha is COVERAGE and colour is BRIGHTNESS, so the way to
            // make fire fiercer is to overdrive the colour — widening the alpha instead turns a
            // flame into fog. Measured, SpriteRenderer.color reads back an authored 2.400
            // unchanged and both the camera and the URP asset have HDR on, so this survives.
            var fx = Rig(Vector2.right, 5.5f, 60f, out var host);

            float peak = 0f;
            for (int i = 0; i < fx.SliceCount; i++)
            {
                var c = host.transform.Find("Wedge/Body" + i.ToString("00")).GetComponent<SpriteRenderer>().color;
                peak = Mathf.Max(peak, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
            }

            Assert.Greater(peak, 1.5f,
                "the brightest body component is " + peak.ToString("F2")
                + " — the HDR overdrive is gone, and the intensity is back on the alpha budget");
        }

        [Test]
        public void NoBodySliceIsEverDrawnPale()
        {
            // The near-colourless aura spine belongs to the THROAT layer. The moment the body
            // borrows it, the brightest half of the cone stops looking like fire.
            var fx = Rig(Vector2.right, 5.5f, 60f, out var host);

            for (int i = 0; i < fx.SliceCount; i++)
            {
                var c = host.transform.Find("Wedge/Body" + i.ToString("00")).GetComponent<SpriteRenderer>().color;
                float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                float saturation = max > 0f ? 1f - min / max : 0f;

                Assert.Greater(saturation, 0.70f,
                    "body slice " + i + " has saturation " + saturation.ToString("F2") + " — it is drawing cream");
            }
        }

        [Test]
        public void TheColourNeverDimsTowardTheTipBecauseTheAlphaAlreadyDoes()
        {
            // Double-dimming: the old ramp faded the VALUE toward Edge while the alpha taper was
            // independently fading the same slices, so the far end added almost nothing. Fading
            // is the alpha's job alone; the colour only says what the fire IS.
            var fx = Rig(Vector2.right, 5.5f, 60f, out var host);

            var near = host.transform.Find("Wedge/Body00").GetComponent<SpriteRenderer>().color;
            var far = host.transform.Find("Wedge/Body" + (fx.SliceCount - 1).ToString("00"))
                          .GetComponent<SpriteRenderer>().color;

            float nearPeak = Mathf.Max(near.r, Mathf.Max(near.g, near.b));
            float farPeak = Mathf.Max(far.r, Mathf.Max(far.g, far.b));

            Assert.Greater(farPeak, nearPeak * 0.6f,
                "the tip's colour is " + farPeak.ToString("F2") + " against " + nearPeak.ToString("F2")
                + " at the mouth — it is being darkened as well as faded");
            Assert.Less(far.g, near.g,
                "the tip should be REDDER than the mouth; a flame cools toward red");
        }

        [Test]
        public void ACoolSwatchKeepsItsHueInsteadOfBeingCookedTowardRed()
        {
            // The orange-to-red cooling is a statement about black bodies, so it applies to a
            // WARM swatch only. Applied to a blue breath the same shift swings it through cyan.
            var fx = Rig(Vector2.right, 5.5f, 60f, out _);
            var coolHost = new GameObject("CoolProbe");
            _spawned.Add(coolHost);
            var blue = FlameConeFX.Attach(coolHost.transform, Vector2.right, 5.5f, 60f,
                                          new Color(0.16f, 0.55f, 1f, 1f));

            Color mouth = blue.FireHue(0f);
            Color tip = blue.FireHue(1f);
            Color.RGBToHSV(mouth, out float hMouth, out _, out _);
            Color.RGBToHSV(tip, out float hTip, out _, out _);

            Assert.AreEqual(hMouth, hTip, 0.001f,
                "a cool swatch was hue-shifted from " + hMouth.ToString("F3") + " to " + hTip.ToString("F3"));
            Assert.Greater(fx.FireHue(0f).r, fx.FireHue(1f).g, "sanity: the warm ramp is still warm");
        }

        // ── The envelope ─────────────────────────────────────────────────────────────

        [Test]
        public void TheBreathIgnitesRatherThanPopping()
        {
            // A rig built in Attach renders one frame before Update first runs, so an envelope
            // that is not seated at zero pops at full brightness for a frame.
            var host = new GameObject("FlameConeIgnition");
            _spawned.Add(host);
            var fx = FlameConeFX.Attach(host.transform, Vector2.right, 5.5f, 60f, Color.red);

            Assert.AreEqual(0f, fx.Envelope, 0.001f, "the cone is at full brightness on its build frame");

            for (int i = 0; i < 30; i++) fx.Tick(1f / 60f, 99f);
            Assert.Greater(fx.Envelope, 0.95f, "the cone never reaches full brightness");
        }

        [Test]
        public void TheBreathFadesOutOnItsRemainingTimeNotOnItsAge()
        {
            // The extinction ramp is driven by what is LEFT, so a cast cut short by eviction
            // fades on the same curve as one that ran its course. Every persistent spell effect
            // has five exit paths and only the registry's is not its own timer.
            var host = new GameObject("FlameConeFade");
            _spawned.Add(host);
            var fx = FlameConeFX.Attach(host.transform, Vector2.right, 5.5f, 60f, Color.red);
            for (int i = 0; i < 30; i++) fx.Tick(1f / 60f, 99f);

            fx.Tick(1f / 60f, FlameConeFX.EXTINGUISH_SECONDS * 0.5f);
            Assert.AreEqual(0.5f, fx.Envelope, 0.05f, "half a fade in should read as half brightness");

            fx.Tick(1f / 60f, 0f);
            Assert.AreEqual(0f, fx.Envelope, 0.001f, "the cone is still lit with no time left");
        }

        [Test]
        public void StoppingTheBreathLetsTheAirBurnOutInsteadOfCuttingIt()
        {
            var fx = Rig(Vector2.right, 5.5f, 60f, out var host);
            Assert.Greater(fx.Fire.emission.rateOverTime.constant, 0f, "the jet never emitted");

            fx.StopEmitting();
            foreach (var ps in host.GetComponentsInChildren<ParticleSystem>(true))
                Assert.AreEqual(0f, ps.emission.rateOverTime.constant, 0.001f,
                    ps.name + " is still emitting after the breath ended");
        }

        // ── The wiring ───────────────────────────────────────────────────────────────

        [Test]
        public void TheControllerOwnsItsOwnDissipation()
        {
            // Without ISpellEffectDissipates the registry calls Object.Destroy, so a cone
            // evicted by its own recast is a hard cut. With maxInstances 1 that is the NORMAL
            // case, not the edge one — the same arithmetic that made arcane_flame pop.
            Assert.IsTrue(typeof(ISpellEffectDissipates).IsAssignableFrom(typeof(ConeBreathController)),
                "ConeBreathController cannot fade when the registry evicts it");
        }

        [Test]
        public void TheExecutorTracksTheConeSoMaxInstancesIsNotDeadData()
        {
            string source = Source("Gameplay/Spells/Executors/ConeBreathExecutor.cs");
            StringAssert.Contains("SpellEffectRegistry.Track", source,
                "the cone is a loose GameObject nothing owns, so maxInstances and the zone-change "
                + "teardown both do nothing for it");
        }

        [Test]
        public void TheBreathNeverPlaysASoundItHasNotAuthored()
        {
            // AudioManager warns once per unresolved id, and neither breath id has ever existed
            // in the catalog — so the first cast of every session pushed a warning into a
            // console this project requires to be clean. HasSfx is the gate the interface
            // documents for a speculative "play a sound named after this spell, if one exists".
            string source = Source("Gameplay/Spells/Controllers/ConeBreathController.cs");
            StringAssert.Contains("HasSfx", source,
                "the controller calls PlaySfxById blind, which warns on every unauthored id");
        }

        [Test]
        public void NothingReadsRendererDotMaterialOnTheWayOut()
        {
            // `renderer.material` INSTANTIATES a clone — measured, the material count rises by
            // one on the read. The old teardown did it twice per cast, inside the very method
            // whose comment claimed the per-cast material had been removed.
            foreach (var path in new[]
            {
                "Gameplay/Spells/Controllers/ConeBreathController.cs",
                "Gameplay/Spells/Visuals/FlameConeFX.cs",
                "Gameplay/Spells/Visuals/FlameConeFX.Emitters.cs",
                "Gameplay/Spells/Visuals/FlameConeFX.Update.cs",
            })
            {
                string source = Source(path);
                StringAssert.DoesNotContain(".material =", source, path + " assigns an instanced material");
                StringAssert.DoesNotContain("_lr.material", source, path + " reads an instanced material");
            }
        }
    }
}
