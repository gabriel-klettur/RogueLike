using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// The SHIPPED <c>firework_launch</c> definition, and the executor that reads it.
    ///
    /// <para>Everything pinned here was wrong at once, and every piece of it was internally
    /// consistent while being wrong — which is why it survived. The spell rode
    /// <c>ProjectileExecutor</c> with <c>damage: 0</c>, no <c>impactPreset</c> and no
    /// <c>lifetime</c>, so it expired on the default range of 20 after 0.44 s and never burst
    /// at all; it asked the catalog for an audio id that has never existed; it carried the
    /// opaque-white "nobody authored this" swatch; it left <c>element</c> blank and leaned on
    /// the legacy key table; it exposed <c>vfxPreset</c> and <c>impactPreset</c> in F4 while
    /// hiding the three numbers that actually aim it; and its asset predated four schema
    /// fields, which therefore deserialized to defaults.</para>
    ///
    /// <para><c>FireworkVisualContractTests</c> covers the rig that gets drawn.</para>
    /// </summary>
    public class FireworkSpellDataTests
    {
        private const string Key = "firework_launch";
        private const string AssetPath = "Assets/_Project/Data/Catalogs/Spells/" + Key + ".asset";
        private const string ExecutorPath =
            "_Project/Scripts/Gameplay/Spells/Executors/FireworkLaunchExecutor.cs";
        private const string AudioCatalogPath = "Assets/_Project/Resources/AudioCatalog.asset";

        private static SpellDefinition Load()
        {
            var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(AssetPath);
            Assert.IsNotNull(spell, AssetPath + " did not load — the fixture would assert on nothing.");
            return spell;
        }

        private static string ExecutorSource()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath,
                ExecutorPath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.IsTrue(File.Exists(path), path + " not found — the source scan would pass vacuously.");
            return File.ReadAllText(path);
        }

        /// <summary>Executable lines only: doc comments name these constantly.</summary>
        private static bool HasExecutableText(string source, string needle)
            => source.Split('\n').Any(line =>
            {
                string t = line.TrimStart();
                if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*")) return false;
                return line.Contains(needle);
            });

        // ── The three numbers that aim the spell ───────────────────────────────

        /// <summary>
        /// <c>range</c> is the flight distance, <c>speed</c> the flight speed and <c>radius</c>
        /// the burst radius, all in WORLD UNITS. Five other spells in this project shipped
        /// authored in the Python pixel scale with a silent divide by 16 somewhere — the tell is
        /// always that the authored number is an order of magnitude away from anything the
        /// camera can show, so the bounds here are the camera's.
        /// </summary>
        [Test]
        public void FlightDistanceSpeedAndRadiusAreAuthoredInWorldUnits()
        {
            var spell = Load();

            // The camera is 33.33 x 16.67 world units, so its half-height is 8.33. Past ~10 an
            // upward shot bursts off the top of the screen; below ~3 it goes off in the caster's
            // face whichever way it is aimed.
            Assert.That(spell.range, Is.InRange(3f, 10f),
                "range is the FLIGHT DISTANCE in world units — how far along the cursor bearing " +
                "the shell travels before it opens.");

            // distance / speed is the flight time. Long enough to watch the shell travel, short
            // enough that the spell still answers the button.
            float flightSeconds = spell.range / spell.speed;
            Assert.That(flightSeconds, Is.InRange(0.35f, 1.4f),
                $"range/speed is the flight time and came out at {flightSeconds:F2}s. Under a " +
                "third of a second nobody sees the shell travel; over about 1.4 s the spell " +
                "stops answering the button.");

            Assert.That(spell.radius, Is.InRange(1.5f, 6f),
                "radius is the BURST RADIUS in world units, and the ring, the stars and the " +
                "light all size off it. A radius in the Python pixel scale would be ~50 here.");
        }

        [Test]
        public void TheExecutorAppliesNoDivisorToTheAuthoredNumbers()
        {
            string source = ExecutorSource();
            Assert.IsFalse(HasExecutableText(source, "/ 16f") || HasExecutableText(source, "/ 16)"),
                "A divide by 16 is the Python pixel scale creeping back in. wallWidth, the " +
                "totem, the vortex, the puddle and the arcane flame all shipped that way.");
        }

        // ── The seam every caster-emitted spell shares ─────────────────────────

        [Test]
        public void TheShellLeavesFromTheSharedCastStart()
        {
            Assert.IsTrue(HasExecutableText(ExecutorSource(), "ResolveCastStart("),
                "Every spell that visibly leaves the caster uses Fireball's exact launch point " +
                "— hand height plus the spell's own forward clearance. CastOriginContractTests " +
                "pins the same thing from the other direction.");
        }

        // ── Audio ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The id the executor used to ask for. <c>AudioCatalog.asset</c> contains no
        /// <c>spell_</c> id at all, so the call produced one warning per session and no sound —
        /// and <c>BossDefinitionDataIntegrityTests</c> already forbids that same id.
        /// </summary>
        [Test]
        public void TheExecutorDoesNotAskTheCatalogForASoundThatDoesNotExist()
        {
            Assert.IsFalse(HasExecutableText(ExecutorSource(), "spell_firework_launch"),
                "'spell_firework_launch' has never existed in AudioCatalog.asset. FireworkAudio " +
                "synthesises the one-shots instead; the catalog path becomes the fallback the " +
                "day a recorded set is authored.");
        }

        [Test]
        public void TheAudioCatalogStillHasNoSpellIds()
        {
            // If this ever fails, someone authored real spell audio — which is good news, and
            // the reason FireworkAudio's doc comment says the catalog is the better answer.
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", AudioCatalogPath));
            if (!File.Exists(path)) Assert.Ignore("AudioCatalog.asset not found at " + path);

            Assert.IsFalse(File.ReadAllText(path).Contains("spell_firework_launch"),
                "A recorded firework one-shot now exists. Point the controller at the catalog " +
                "and demote FireworkAudio to the fallback its doc comment already describes.");
        }

        // ── Element and swatch ─────────────────────────────────────────────────

        /// <summary>
        /// <c>element</c> used to be blank, so the spell's element came from
        /// <c>MapSpellKeyToElement</c> — the legacy switch whose own comment tells new spells
        /// not to grow it. Authoring it is free here because the spell deals no damage, so the
        /// usual coupling to <c>Health.MitigateDamage</c> cannot bite.
        /// </summary>
        [Test]
        public void TheElementIsAuthoredOnTheAssetRatherThanInferredFromTheKey()
        {
            var spell = Load();
            Assert.AreEqual("Fire", spell.element,
                "element must be authored on the SO. The legacy key table is not the place to " +
                "say what a spell is made of.");
        }

        /// <summary>
        /// The one spell in the catalog where the opaque-white sentinel is the DESIRED value.
        /// Everywhere else white means "nobody chose a colour" and the palette falls back to a
        /// single hue; here it means "be every colour", which is what a firework is. Authoring
        /// an actual colour is still supported and gives a single-hue shell — this test pins
        /// that the shipped one is the festival, not that white is the only legal value.
        /// </summary>
        [Test]
        public void TheShippedSwatchResolvesToTheFestivalSpread()
        {
            var spell = Load();
            var palette = FireworkPalette.From(spell.particleColor);

            Assert.IsTrue(palette.IsFestival,
                "The shipped firework is meant to be multicoloured. A single-hue shell is a flare.");
            Assert.GreaterOrEqual(palette.Stars.Length, 3,
                "Fewer than three star colours and the shell reads as monochrome.");
        }

        [Test]
        public void AnAuthoredSwatchGivesASingleHueShellCentredOnIt()
        {
            var palette = FireworkPalette.From(new Color(0.2f, 0.45f, 1f, 1f));

            Assert.IsFalse(palette.IsFestival);
            foreach (var star in palette.Stars)
            {
                Color.RGBToHSV(star, out float h, out _, out float v);
                Assert.That(v, Is.GreaterThan(0.7f),
                    "A shell burns at one temperature and differs in HUE. A dark star adds " +
                    "almost nothing on an additive surface.");
                // Blue is ~0.6 in hue. The spread is deliberately narrow enough that the
                // authored colour survives as the shell's identity.
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(h * 360f, 0.6f * 360f)), Is.LessThan(45f),
                    "The stars wandered far enough from the authored hue that the shell is no " +
                    "longer the colour the designer picked.");
            }
        }

        /// <summary>
        /// An achromatic swatch has no hue and <c>RGBToHSV</c> reports 0 for it, which is RED —
        /// the trap <c>ElementPalette.Retint</c> records. A grey shell is a real request and
        /// what it asks for is the absence of colour, not a pink one.
        /// </summary>
        [Test]
        public void AGreySwatchStaysGrey()
        {
            var palette = FireworkPalette.From(new Color(0.59f, 0.59f, 0.59f, 1f));

            foreach (var star in palette.Stars)
            {
                Color.RGBToHSV(star, out _, out float s, out _);
                Assert.That(s, Is.LessThan(0.05f),
                    $"A grey shell produced a saturated star {star}. RGBToHSV reports hue 0 for " +
                    "an achromatic colour, and hue 0 is red.");
            }
        }

        // ── Schema ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The asset predated <c>castPreset</c>, <c>vfxPresetLayers</c>,
        /// <c>impactPresetLayers</c> and <c>castPresetLayers</c>, so those deserialized to
        /// defaults and the file was a silent record of how long nobody had opened it.
        /// </summary>
        [Test]
        public void TheAssetCarriesTheCurrentSchema()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", AssetPath));
            Assert.IsTrue(File.Exists(path), path + " not found.");
            string yaml = File.ReadAllText(path);

            foreach (var field in new[]
            {
                "usesAttackAnimation:", "infinite:", "statusApplications:", "castAnchor:",
                "castForwardOffset:", "animState:", "loadoutAnimKey:", "loadoutKey:",
                "vfxPresetLayers:", "impactPresetLayers:", "castPreset:", "castPresetLayers:",
                "gatherOverride:",
            })
            {
                Assert.IsTrue(yaml.Contains(field),
                    $"'{field}' is missing from {AssetPath}. The asset is on an older schema " +
                    "than SpellDefinition, so that field is taking a default nobody chose.");
            }
        }

        /// <summary>
        /// The shell is drawn by <c>FireworkShellController</c> and <c>FireworkBurstFX</c> off
        /// the spell's own swatch, so a preset spawned on top of it is an uncoordinated extra
        /// layer — the same reason VortexField carries none.
        /// </summary>
        [Test]
        public void TheSpellCarriesNoTrailPreset()
        {
            var spell = Load();
            Assert.IsEmpty(spell.CollectVfxPresets(),
                "vfxPreset is not the firework's look any more. Setting one puts a second, " +
                "differently-tuned emitter on top of the rig.");
        }

        // ── What F4 shows ──────────────────────────────────────────────────────

        [Test]
        public void ThePanelShowsTheFieldsTheExecutorActuallyReads()
        {
            var shown = SpellFieldRelevance.FieldsForType(SpellType.FireworkLaunch);

            foreach (var field in new[] { "range", "speed", "radius", "particleColor" })
                Assert.Contains(field, shown.ToArray(),
                    $"'{field}' aims the firework and the F4 panel must expose it. The row used " +
                    "to show vfxPreset and impactPreset and none of these.");

            Assert.IsFalse(shown.Contains("vfxPreset"),
                "The rig is procedural — a vfxPreset control would be a dial that does nothing.");
        }
    }
}
