using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// The laser beam ships as seven colour variants of one spell.
    ///
    /// A colour variant is only useful if it is a colour variant: the moment one of them
    /// picks up a different mana cost, cooldown, range or VFX preset, "same spell in another
    /// colour" stops being true and the set becomes seven spells to maintain instead of one
    /// plus a palette. Nothing in Unity enforces that — they are seven independent assets
    /// that happen to have been copied from each other once.
    ///
    /// The dark variant is the interesting one. Additive blending is <c>dst += rgb * a</c>,
    /// so a black beam drawn like every other beam contributes exactly nothing to the frame
    /// and renders as literally invisible, not as dark. It has to composite the other way.
    /// </summary>
    [TestFixture]
    public class LaserBeamVariantTests
    {
        private const string CATALOG = "Assets/_Project/Data/Catalogs/SpellCatalog.asset";
        private const string BASE_KEY = "laser_beam";

        private static readonly string[] VariantKeys =
        {
            "laser_beam_blue",
            "laser_beam_red",
            "laser_beam_white",
            "laser_beam_yellow",
            "laser_beam_green",
            "laser_beam_black",
        };

        private SpellCatalog _catalog;

        [SetUp]
        public void SetUp() => _catalog = AssetDatabase.LoadAssetAtPath<SpellCatalog>(CATALOG);

        private IEnumerable<SpellDefinition> Spells() =>
            _catalog == null ? Enumerable.Empty<SpellDefinition>()
                             : _catalog.AllSpells.Where(s => s != null);

        private SpellDefinition ByKey(string key) =>
            Spells().FirstOrDefault(s => s.spellKey == key);

        // ── Presence ─────────────────────────────────────────────────────────────

        [Test]
        public void TheCatalogLoads()
        {
            Assert.IsNotNull(_catalog, $"{CATALOG} not found — every test below would pass vacuously.");
        }

        [Test]
        public void EveryVariantIsInTheCatalog()
        {
            // An asset that exists on disk but is not in the catalog does not appear in the
            // F4 Spells Editor and cannot be cast. It is invisible in exactly the way that
            // looks like it was never created.
            foreach (var key in VariantKeys)
                Assert.IsNotNull(ByKey(key),
                    $"'{key}' is missing from the catalog. The asset can exist on disk and " +
                    "still be unreachable from the game and from the editor.");
        }

        [Test]
        public void EverySpellKeyIsUnique()
        {
            var dupes = Spells()
                .GroupBy(s => s.spellKey)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} x{g.Count()}")
                .ToList();

            Assert.IsEmpty(dupes,
                "Spells are resolved by key, so a duplicate silently binds to whichever asset " +
                "the catalog lists first and the other becomes uncastable.\n  " +
                string.Join("\n  ", dupes));
        }

        [Test]
        public void EveryVariantHasItsOwnDisplayName()
        {
            var names = VariantKeys.Select(k => ByKey(k)).Where(s => s != null)
                                   .Select(s => s.displayName).ToList();

            Assert.AreEqual(names.Count, names.Distinct().Count(),
                "The Spells Editor lists spells by display name. Duplicates make two rows " +
                "indistinguishable, which is worse than no variant at all.");
        }

        // ── They really are the same spell ───────────────────────────────────────

        [Test]
        public void EveryVariantIsMechanicallyIdenticalToTheOriginal()
        {
            var b = ByKey(BASE_KEY);
            Assert.IsNotNull(b, $"'{BASE_KEY}' is missing — there is nothing to compare against.");

            foreach (var key in VariantKeys)
            {
                var v = ByKey(key);
                if (v == null) continue;   // reported by EveryVariantIsInTheCatalog

                Assert.AreEqual(b.type, v.type, $"{key}: type");
                Assert.AreEqual(b.manaCost, v.manaCost, 1e-4f, $"{key}: manaCost");
                Assert.AreEqual(b.cooldownDuration, v.cooldownDuration, 1e-4f, $"{key}: cooldownDuration");
                Assert.AreEqual(b.channelDuration, v.channelDuration, 1e-4f, $"{key}: channelDuration");
                Assert.AreEqual(b.prepareDuration, v.prepareDuration, 1e-4f, $"{key}: prepareDuration");
                Assert.AreEqual(b.damage, v.damage, 1e-4f, $"{key}: damage");
                Assert.AreEqual(b.range, v.range, 1e-4f, $"{key}: range");
                Assert.AreEqual(b.scale, v.scale, 1e-4f, $"{key}: scale");
                Assert.AreEqual(b.vfxPreset, v.vfxPreset, $"{key}: vfxPreset");
                Assert.AreEqual(b.impactPreset, v.impactPreset, $"{key}: impactPreset");
            }
        }

        [Test]
        public void NoTwoVariantsShareAColour()
        {
            var all = new List<SpellDefinition> { ByKey(BASE_KEY) };
            all.AddRange(VariantKeys.Select(ByKey));

            var colours = all.Where(s => s != null).Select(s => s.particleColor).ToList();
            Assert.AreEqual(colours.Count, colours.Distinct().Count(),
                "Colour is the only thing separating these spells. Two that share one are the " +
                "same spell twice.");
        }

        [Test]
        public void EveryVariantIsActuallyColoured()
        {
            // A variant left at Color.clear or alpha 0 falls back to the hardcoded default
            // cyan in LaserBeamController.Visual.cs, so it would render as some other
            // variant's colour rather than its own.
            foreach (var key in VariantKeys)
            {
                var v = ByKey(key);
                if (v == null) continue;

                Assert.Greater(v.particleColor.a, 0f,
                    $"{key}: a zero alpha makes the controller fall back to its default cyan.");
            }
        }

        // ── The dark one ─────────────────────────────────────────────────────────

        [Test]
        public void TheDarkVariantDoesNotRenderAdditively()
        {
            var v = ByKey("laser_beam_black");
            Assert.IsNotNull(v);

            Assert.IsFalse(BeamMaterialCache.ShouldRenderAdditive(v.particleColor),
                "Additive blending can only brighten. Drawn additively this beam adds " +
                $"{v.particleColor} x alpha to the frame, which rounds to nothing — the spell " +
                "would fire, deal damage, and be completely invisible.");
        }

        [Test]
        public void EveryOtherVariantStillRendersAdditively()
        {
            foreach (var key in VariantKeys.Concat(new[] { BASE_KEY }))
            {
                if (key == "laser_beam_black") continue;
                var v = ByKey(key);
                if (v == null) continue;

                Assert.IsTrue(BeamMaterialCache.ShouldRenderAdditive(v.particleColor),
                    $"{key} {v.particleColor} fell below the darkness threshold. Alpha blending " +
                    "makes a beam occlude the world instead of glowing over it, which is the " +
                    "look the additive rework existed to remove.");
            }
        }

        [Test]
        public void TheThresholdSitsBetweenTheDarkestBrightVariantAndTheDarkOne()
        {
            // Both bounds matter. Too low and the void beam renders additively and vanishes;
            // too high and a legitimately deep colour gets forced into alpha and stops glowing.
            float darkest = VariantKeys.Concat(new[] { BASE_KEY })
                .Where(k => k != "laser_beam_black")
                .Select(ByKey).Where(s => s != null)
                .Min(s => Mathf.Max(s.particleColor.r, Mathf.Max(s.particleColor.g, s.particleColor.b)));

            var voidBeam = ByKey("laser_beam_black");
            float voidMax = voidBeam == null ? 0f
                : Mathf.Max(voidBeam.particleColor.r, Mathf.Max(voidBeam.particleColor.g, voidBeam.particleColor.b));

            Assert.Less(voidMax, BeamMaterialCache.DARK_BEAM_THRESHOLD);
            Assert.Greater(darkest, BeamMaterialCache.DARK_BEAM_THRESHOLD,
                $"The darkest glowing variant peaks at {darkest:0.00} against a threshold of " +
                $"{BeamMaterialCache.DARK_BEAM_THRESHOLD:0.00} — too close to be safe.");
        }

        // ── Black and grey, and readable ─────────────────────────────────────────

        /// <summary>
        /// Straight `over` compositing, the same equation the alpha material requests with
        /// SrcAlpha / OneMinusSrcAlpha: <c>dst = src * a + dst * (1 - a)</c>.
        /// </summary>
        private static float Over(float src, float alpha, float dst) => (src * alpha) + (dst * (1f - alpha));

        /// <summary>
        /// The charge texture's true peak alpha along its length.
        ///
        /// Not sampled at the head's centre: the tail is one-sided and contributes exactly
        /// zero there, so the maximum actually sits a little BEHIND the head, where the
        /// gaussian has barely decayed and the streak has already risen. Sampling the head
        /// centre understates the charge by about a third.
        /// </summary>
        private static float PacketPeakAlpha(float softness)
        {
            float peak = 0f;
            for (int i = 0; i <= 1024; i++)
                peak = Mathf.Max(peak, BeamTextureLibrary.EvaluateAlpha(
                    BeamTextureKind.Packet, 0f, i / 1024f, softness));
            return peak;
        }

        /// <summary>Reads a private tuning constant out of the controller's source.</summary>
        private static float Constant(string name)
        {
            string src = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath,
                "_Project", "Scripts", "Gameplay", "Spells", "Controllers", "LaserBeamController.cs"));
            // Word boundary so a short name cannot match inside a longer identifier.
            var m = System.Text.RegularExpressions.Regex.Match(src, @"\b" + name + @"\s*=\s*([0-9.]+)f");
            Assert.IsTrue(m.Success, $"{name} not found in LaserBeamController.");
            return float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Composites the void beam over a background and returns (body, charge) tone.
        ///
        /// The stone the player fights over sits around mid grey, which is the one tone a dark
        /// beam can hide in. So the test is not "is it dark" — it is whether the beam separates
        /// from mid grey in BOTH directions: a body darker than the ground and a charge lighter
        /// than it.
        /// </summary>
        private static void CompositeVoidBeam(float background, out float body, out float charge)
        {
            var v = AssetDatabase.LoadAssetAtPath<SpellCatalog>(CATALOG)
                                 .AllSpells.First(s => s != null && s.spellKey == "laser_beam_black");
            Color c = v.particleColor;

            LaserBeamController.ResolvePacketStyle(additive: false, out float coreLerp,
                                                   out float packetLerp, out int packetOrder);

            float glowTone = c.grayscale;
            float coreTone = Color.Lerp(c, Color.white, coreLerp).grayscale;
            float packetTone = Color.Lerp(c, Color.white, packetLerp).grayscale;

            float glowA = BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Glow, 0f, 0.5f, 0.80f) * Constant("GLOW_ALPHA");
            float coreA = BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Core, 0f, 0.5f, 0.25f) * Constant("CORE_ALPHA");
            float packetA = PacketPeakAlpha(0.5f) * Constant("PACKET_ALPHA");

            // Glow first, then core. The charge goes last because a dark beam draws it above
            // the core — see ORDER_PACKET_DARK.
            Assert.Greater(packetOrder, LaserBeamController.ORDER_CORE,
                "`over` is not commutative. With the charge under the core, the core repaints " +
                "it every frame and drags it back to the body's tone.");

            body = Over(coreTone, coreA, Over(glowTone, glowA, background));
            charge = Over(packetTone, packetA, body);
        }

        [Test]
        public void TheVoidBeamIsDarkerThanTheGroundItIsFiredOver()
        {
            const float STONE = 0.37f;   // roughly the dungeon floor
            CompositeVoidBeam(STONE, out float body, out float charge);

            Assert.Less(body, STONE - 0.12f,
                $"The beam body composites to {body:0.000} over a {STONE:0.00} floor. A dark beam " +
                "that lands near the background tone is not subtle, it is invisible.");
        }

        [Test]
        public void TheVoidBeamsChargeIsLighterThanTheGround()
        {
            const float STONE = 0.37f;
            CompositeVoidBeam(STONE, out float body, out float charge);

            Assert.Greater(charge, STONE + 0.12f,
                $"The charge composites to {charge:0.000} over a {STONE:0.00} floor. It has to " +
                "separate upward while the body separates downward — that is what makes the " +
                "travel legible on a beam that cannot glow.");

            Assert.Greater(charge - body, 0.30f,
                $"Body {body:0.000} against charge {charge:0.000}. Too little tonal range and " +
                "the whole beam reads as one flat dark bar.");
        }

        [Test]
        public void TheVoidBeamHoldsUpOnDarkAndOnBrightGround()
        {
            // Compositing over a dark cave floor and over snow. The body must never end up
            // LIGHTER than what it is drawn over, whatever that is — a black beam that
            // brightens the scene has picked the wrong blend mode.
            foreach (float bg in new[] { 0.08f, 0.37f, 0.85f })
            {
                CompositeVoidBeam(bg, out float body, out float charge);
                Assert.LessOrEqual(body, bg + 1e-4f, $"over background {bg:0.00}: body {body:0.000}");
                Assert.Greater(charge, body, $"over background {bg:0.00}: the charge must stay the lighter of the two");
            }
        }

        [Test]
        public void TheVoidBeamKeepsItsBodyBlackInsteadOfWashingItToMidGrey()
        {
            LaserBeamController.ResolvePacketStyle(additive: false, out float darkCoreLerp, out float darkPacketLerp, out _);
            LaserBeamController.ResolvePacketStyle(additive: true, out float litCoreLerp, out float litPacketLerp, out _);

            Assert.Less(darkCoreLerp, litCoreLerp,
                "An additive beam lifts its core toward white because adding white light is how " +
                "it reads as hot. A dark beam cannot add anything, so the same lift only lands " +
                "the core on mid grey — the one tone it disappears into.");

            Assert.Greater(darkPacketLerp, litPacketLerp,
                "With the body held black, the charge is the only layer left to carry contrast, " +
                "so it lifts further than it would on a glowing beam.");
        }

        [Test]
        public void TheVoidBeamIsNeutralRatherThanTinted()
        {
            var v = ByKey("laser_beam_black");
            Assert.IsNotNull(v);
            Color c = v.particleColor;

            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));

            Assert.Less(max - min, 0.05f,
                $"{c} has a visible hue. Every layer of this beam is derived from the base " +
                "colour by lerping toward white, so a tint in the base tints the whole beam and " +
                "it stops being the black-and-grey variant.");
        }

        // ── The decision itself ──────────────────────────────────────────────────

        [Test]
        public void BrightnessIsJudgedByChannelNotByLuminance()
        {
            // A saturated pure blue has a perceptual luminance near 0.07 and glows perfectly
            // well additively, because the blue channel has plenty to add. Judging by
            // luminance would force every cool colour into alpha blending.
            Assert.IsTrue(BeamMaterialCache.ShouldRenderAdditive(new Color(0f, 0f, 1f, 1f)),
                "A pure blue beam must still glow.");
            Assert.IsTrue(BeamMaterialCache.ShouldRenderAdditive(new Color(0f, 0f, 0.6f, 1f)));
            Assert.IsFalse(BeamMaterialCache.ShouldRenderAdditive(Color.black));
        }

        [Test]
        public void AlphaDoesNotAffectTheDecision()
        {
            // Opacity cannot rescue an additive black: dst += 0 * a is zero for every a.
            Assert.IsFalse(BeamMaterialCache.ShouldRenderAdditive(new Color(0f, 0f, 0f, 1f)));
            Assert.IsFalse(BeamMaterialCache.ShouldRenderAdditive(new Color(0f, 0f, 0f, 0.1f)));
        }

        [Test]
        public void TheTwoBlendModesGetSeparateMaterials()
        {
            var tex = BeamTextureLibrary.Get(BeamTextureKind.Core, 0.4f);

            Assert.AreNotSame(BeamMaterialCache.Get(tex, additive: true),
                              BeamMaterialCache.Get(tex, additive: false),
                "Materials are shared across every beam in the scene. One key for both blend " +
                "modes would let a void beam repaint every other laser's material.");
        }
    }
}
