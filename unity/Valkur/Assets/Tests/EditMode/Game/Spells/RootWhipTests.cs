using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// The root field — the spell called Root Whip that used to root nothing and whip
    /// nothing.
    ///
    /// <para>Every case here guards something that looked like a working spell while being
    /// the wrong one, and all of it was measured before it was written: a radius carried
    /// over from the Python build in its own units (24, divided by 16, drawing a field 9%
    /// of the screen wide); a green nature spell rendering four ORANGE lava sprites, an
    /// orange light and 25 lava particles a second under 16 green ones; a stretched
    /// billboard at zero velocity, which makes the stretch axis undefined and makes Unity
    /// ignore particle rotation, so three authored parameters were inert and nothing rose
    /// out of the ground; a visible emission circle 27.5% wider than the circle that hurts;
    /// and a persistent hazard on a 0.6 s cooldown against its own 4 s duration with no
    /// instance cap and no mana cost.</para>
    /// </summary>
    public class RootWhipTests
    {
        private const string Folder = "Assets/_Project/Data/Catalogs/Spells/";
        private const string Key = "root_whip";

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

        private GameObject NewHost()
        {
            var go = new GameObject("root_field_probe");
            _spawned.Add(go);
            return go;
        }

        // ── shipped data ─────────────────────────────────────────────────────────────

        [Test]
        public void Radius_IsAuthoredInWorldUnits_NotThePythonPixelScale()
        {
            var spell = Load(Key);
            Assert.IsNotNull(spell, "root_whip.asset is missing");

            // PuddleExecutor no longer divides by 16, so an authored 24 would now be a
            // 24-unit field on a camera 33.33 units wide. Anything at or above 16 is the
            // old pixel scale surviving.
            Assert.Less(spell.radius, 16f,
                "radius looks like the Python pixel scale; PuddleExecutor reads world units");
            Assert.Greater(spell.radius, 0.5f, "radius must be authored, not left to the fallback");
        }

        [Test]
        public void PuddleLava_WasMigratedWithIt()
        {
            // The executor divides for nobody now, so the sibling had to move in the same
            // change or it would be a 48-unit lava pool.
            var lava = Load("puddle_lava");
            Assert.IsNotNull(lava);
            Assert.Less(lava.radius, 16f, "puddle_lava is still authored in the pixel scale");
        }

        [Test]
        public void Cooldown_ExceedsItsOwnDuration()
        {
            var spell = Load(Key);

            // The rule every other persistent ground hazard follows (arcane_flame,
            // vortex_pull, vortex_push): a cooldown shorter than the duration means the
            // caster always has one out AND can evict their own to reposition it, which is
            // permanent area denial. It shipped at 0.6 against 4.
            Assert.Greater(spell.cooldownDuration, spell.duration,
                "a field that outlives its own cooldown is permanent area denial");
        }

        [Test]
        public void Field_IsCappedAndCostsMana()
        {
            var spell = Load(Key);
            Assert.AreEqual(1, spell.maxInstances,
                "maxInstances 0 means unlimited, which stacks the DoT with itself");
            Assert.Greater(spell.manaCost, 0f, "a free spammable DoT field has no cost to trade");
        }

        [Test]
        public void RootWhip_ActuallyRoots()
        {
            var spell = Load(Key);

            Assert.IsNotNull(spell.statusApplications,
                "the spell is called Root Whip and authored no status at all");
            Assert.IsNotEmpty(spell.statusApplications);

            bool rooted = false;
            foreach (var app in spell.statusApplications)
            {
                if (app.type != StatusEffectKind.Root) continue;
                rooted = true;
                Assert.Greater(app.duration, 0f, "duration <= 0 is a no-op");
                Assert.Greater(app.chance, 0f, "chance 0 is the default and means never");
            }
            Assert.IsTrue(rooted, "no Root application on root_whip");
        }

        [Test]
        public void AimedField_AuthorsItsOwnReach()
        {
            var spell = Load(Key);
            Assert.IsTrue(spell.spawnAtMouse, "a placed field the player cannot aim is not a placed field");
            // range 0 hands the reach to a constant inside SpellTargeting, which is not
            // something a player can learn or a designer can tune.
            Assert.Greater(spell.range, 0f, "spawnAtMouse with range 0 defers the reach to a constant");
        }

        [Test]
        public void Swatch_IsAuthoredAndGreen()
        {
            var spell = Load(Key);
            Assert.IsFalse(RootPalette.IsUnauthored(spell.particleColor),
                "opaque white is the project sentinel for an untouched swatch");
            Assert.Greater(spell.particleColor.g, spell.particleColor.r,
                "a root field authored redder than it is green");
        }

        // ── palette ──────────────────────────────────────────────────────────────────

        [Test]
        public void Palette_RunsDarkSoilToBrightSap()
        {
            var p = RootPalette.From(new Color(0.30f, 0.55f, 0.20f, 1f));

            Assert.Less(Value(p.Soil), Value(p.Bark), "soil must be darker than bark");
            Assert.Less(Value(p.Bark), Value(p.Leaf), "bark must be darker than the living tip");
            Assert.LessOrEqual(Value(p.Leaf), Value(p.Sap) + 1e-3f, "sap is the brightest field");
        }

        [Test]
        public void Palette_KeepsAnAuthoredHue()
        {
            // A crimson root must not come out green. Every field but the deliberately
            // warmed soil keeps the authored hue.
            var authored = new Color(0.72f, 0.12f, 0.18f, 1f);
            var p = RootPalette.From(authored);

            Color.RGBToHSV(authored, out float h0, out _, out _);
            foreach (var c in new[] { p.Bark, p.Leaf, p.Sap })
            {
                Color.RGBToHSV(c, out float h, out _, out _);
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(h0 * 360f, h * 360f)), 12f,
                    "the authored hue did not survive into the palette");
            }
        }

        [Test]
        public void Palette_DesaturatesAGreySwatchInsteadOfLightingItPink()
        {
            // RGBToHSV reports hue 0 for anything achromatic, and hue 0 is RED. A grey
            // swatch is a real request for the absence of colour.
            var p = RootPalette.From(new Color(0.59f, 0.59f, 0.59f, 1f));
            foreach (var c in new[] { p.Soil, p.Bark, p.Leaf, p.Sap })
            {
                Color.RGBToHSV(c, out _, out float s, out _);
                Assert.Less(s, 0.10f, "a grey root was lit with a hue");
            }
        }

        // ── rig composition ──────────────────────────────────────────────────────────

        [Test]
        public void Rig_NeverScalesItsRoot()
        {
            var host = NewHost();
            host.transform.localScale = Vector3.one * 7f;   // the old PuddleController wrote this
            RootWhipFX.Attach(host.transform, 2.8f, Color.green);

            Assert.AreEqual(1f, host.transform.localScale.x, 1e-4f,
                "a scaled root multiplies the Light2D radius and every child size with it");
        }

        [Test]
        public void GroundRing_SitsExactlyOnTheDamageRadius()
        {
            const float radius = 2.8f;
            var host = NewHost();
            RootWhipFX.Attach(host.transform, radius, Color.green);

            var ring = host.transform.Find("GroundRing");
            Assert.IsNotNull(ring, "the field draws no ground ring");

            // ElementalSprites.Ring is one world unit across and its bright band peaks at a
            // known normalized radius, so the scale that puts the drawn circle on a wanted
            // world radius is a composition, not a literal. Asserting the scale alone would
            // pass while the ring sat somewhere else.
            Assert.AreEqual(RootWhipFX.RingSpanFor(radius), ring.localScale.x, 1e-3f,
                "the drawn boundary is not the circle the spell queries");
        }

        [Test]
        public void EveryStem_IsSeededInsideTheRing()
        {
            const float radius = 2.8f;
            var host = NewHost();
            RootWhipFX.Attach(host.transform, radius, Color.green);

            int stems = 0;
            foreach (Transform child in host.transform)
            {
                if (!child.name.StartsWith("Stem")) continue;
                stems++;
                // The ground plane is drawn foreshortened, so un-squash Y before measuring
                // or every stem looks closer to the centre than it is.
                float x = child.localPosition.x;
                float y = child.localPosition.y / RootWhipFX.GroundSquash;
                Assert.LessOrEqual(Mathf.Sqrt(x * x + y * y), radius + 1e-3f,
                    "a stem was drawn outside the circle the spell promises");
            }
            Assert.Greater(stems, 0, "the field built no stems");
        }

        [Test]
        public void NothingInTheFieldIsLavaOrange()
        {
            // The regression that made this whole rig necessary: PuddleController attached
            // AreaFXRig with the LavaPuddle palette unconditionally, so a green spell drew
            // an orange rune, an orange halo, an orange glow, a yellow core and an orange
            // Light2D on top of its own tendrils.
            var host = NewHost();
            RootWhipFX.Attach(host.transform, 2.8f, new Color(0.30f, 0.55f, 0.20f, 1f));

            foreach (var sr in host.GetComponentsInChildren<SpriteRenderer>(true))
            {
                Color c = sr.color;
                if (c.r < 0.02f && c.g < 0.02f && c.b < 0.02f) continue;  // black is not a hue
                Assert.LessOrEqual(c.r, c.g + 0.08f,
                    sr.gameObject.name + " is redder than it is green");
            }
        }

        [Test]
        public void OneLayerIsOpaque_AndTheRingIsNot()
        {
            // A field made entirely of additive light reads as light shining on the floor
            // rather than as the floor being torn open. The clods and stems are matter.
            var host = NewHost();
            RootWhipFX.Attach(host.transform, 2.8f, Color.green);

            var stem = host.transform.Find("Stem0");
            var ring = host.transform.Find("GroundRing");
            Assert.IsNotNull(stem);
            Assert.IsNotNull(ring);

            var stemMat = stem.GetComponent<SpriteRenderer>().sharedMaterial;
            var ringMat = ring.GetComponent<SpriteRenderer>().sharedMaterial;
            Assert.AreNotSame(stemMat, ringMat,
                "the matter and the light are on the same material, so one of them is wrong");
        }

        // ── motion ───────────────────────────────────────────────────────────────────

        [Test]
        public void Stems_ActuallyRise()
        {
            // The whole reason the old rig failed: measured, maxVelocity was 0 across every
            // live particle and sizeOverLifetime was the only module that did anything, so
            // "roots rising from the ground" never rose.
            var host = NewHost();
            var fx = RootWhipFX.Attach(host.transform, 2.8f, Color.green);

            var stem = host.transform.Find("Stem0");
            Assert.IsNotNull(stem);

            float tallest = 0f;
            for (int i = 0; i < 60; i++)
            {
                fx.Tick(1f / 60f, 1f);
                tallest = Mathf.Max(tallest, stem.localScale.y);
            }

            Assert.Greater(tallest, 0.3f, "no stem ever reached a readable height");
        }

        [Test]
        public void Lash_LeansStemsTowardsTheVictim()
        {
            var host = NewHost();
            var fx = RootWhipFX.Attach(host.transform, 2.8f, Color.green);

            // Past the sprout stagger, so every stem is standing and can strike.
            for (int i = 0; i < 60; i++) fx.Tick(1f / 60f, 1f);

            var baseHeight = new Dictionary<string, float>();
            foreach (Transform child in host.transform)
                if (child.name.StartsWith("Stem"))
                    baseHeight[child.name] = child.localScale.y;

            fx.Lash(host.transform.position + new Vector3(2f, 0f, 0f));

            // Sampled over the whole crack, not on one frame. The lash ramps in and out —
            // one frame after the call it is barely a quarter of the way up, so a
            // single-frame reading measures the ramp rather than the event. The same
            // mistake the vortex debris test made, and it passed on the defect.
            var peakStretch = new Dictionary<string, float>();
            foreach (var k in baseHeight.Keys) peakStretch[k] = 1f;
            for (int i = 0; i < 20; i++)
            {
                fx.Tick(1f / 60f, 1f);
                foreach (Transform child in host.transform)
                {
                    if (!child.name.StartsWith("Stem")) continue;
                    float ratio = child.localScale.y / Mathf.Max(1e-3f, baseHeight[child.name]);
                    if (ratio > peakStretch[child.name]) peakStretch[child.name] = ratio;
                }
            }

            int struck = 0;
            foreach (var kv in peakStretch)
                if (kv.Value > 1.2f) struck++;

            // Some stems strike and the rest go on swaying: a field where EVERY stem
            // answers is one object breathing, not roots reaching for somebody.
            Assert.Greater(struck, 0, "a damage tick moved nothing; the field has no event");
            Assert.Less(struck, host.transform.childCount,
                "every stem answered at once, which reads as the whole field pulsing");
        }

        // ── the controller seam ──────────────────────────────────────────────────────

        [Test]
        public void OwnedVisual_SuppressesTheDiscRigAndTheRootScale()
        {
            var host = NewHost();
            var fx = RootWhipFX.Attach(host.transform, 2.8f, Color.green);

            var controller = host.AddComponent<PuddleController>();
            controller.Initialize(5f, 2.8f, 8, 0.35f, 0, string.Empty, null, null, null, fx);

            Assert.AreEqual(1f, host.transform.localScale.x, 1e-4f,
                "the controller scaled a root whose children carry absolute sizes");
            Assert.IsNull(host.transform.Find("Rune"), "AreaFXRig was built over an owned visual");
            Assert.IsNull(host.transform.Find("Halo"), "AreaFXRig was built over an owned visual");
        }

        private static float Value(Color c)
        {
            Color.RGBToHSV(c, out _, out _, out float v);
            return v;
        }
    }
}
