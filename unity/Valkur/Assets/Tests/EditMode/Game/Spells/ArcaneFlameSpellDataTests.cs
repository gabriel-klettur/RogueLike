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
    /// The SHIPPED <c>arcane_flame</c> definition, and the executor that reads it.
    ///
    /// <para><c>ArcaneFlameControllerTests</c> covers the rig that gets drawn. This fixture
    /// covers the half that was wrong for longer and more quietly: authored data that was
    /// internally consistent and disagreed only with what happened on screen. The spell
    /// divided its radius by 16 — the Python pixel scale, the fifth sighting after
    /// <c>wallWidth</c>, the totem, the vortex and the puddle — left its <c>element</c> blank
    /// and leaned on a hard-coded key table for it, shipped the "nobody authored this"
    /// opaque-white swatch, and could not be aimed at all while carrying the two fields that
    /// say where a spell lands.</para>
    /// </summary>
    public class ArcaneFlameSpellDataTests
    {
        private const string Key = "arcane_flame";
        private const string AssetPath = "Assets/_Project/Data/Catalogs/Spells/" + Key + ".asset";
        private const string ExecutorPath =
            "_Project/Scripts/Gameplay/Spells/Executors/ArcaneFlameExecutor.cs";

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

        /// <summary>Executable lines only: the file's own doc comment names both of these.</summary>
        private static bool HasExecutableText(string source, string needle)
            => source.Split('\n').Any(line =>
            {
                string t = line.TrimStart();
                if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*")) return false;
                return line.Contains(needle);
            });

        // ── The pixel scale ────────────────────────────────────────────────────

        [Test]
        public void RadiusIsAuthoredInWorldUnits_NotThePythonPixelScale()
        {
            var spell = Load();

            // 40 was the shipped value, and 40/16 = 2.5 was the zone that actually appeared.
            // A radius in the tens is the tell that the pixel scale came back.
            Assert.Less(spell.radius, 12f,
                "radius " + spell.radius + " is far too large to be world units — the camera is "
                + "33 units wide, so this would be most of the screen. It is the Python pixel "
                + "scale coming back.");
            Assert.Greater(spell.radius, 0.5f,
                "radius " + spell.radius + " leaves the zone smaller than a character.");
        }

        [Test]
        public void TheExecutorNoLongerDividesTheAuthoredRadius()
        {
            Assert.IsFalse(HasExecutableText(ExecutorSource(), "/ 16f"),
                "ArcaneFlameExecutor divides radius by 16 again. The shipped definition authors "
                + "world units now, so the divide would render a 2.5 u zone at 0.16 u — twelve "
                + "screen pixels, the same failure wall_ice shipped with for months.");
        }

        // ── Aiming ─────────────────────────────────────────────────────────────

        [Test]
        public void TheZoneIsPlacedWhereThePlayerPoints()
        {
            var spell = Load();
            Assert.IsTrue(spell.spawnAtMouse,
                "arcane_flame is a ground zone with a five-second life; landing it on a private "
                + "constant two units ahead makes it the only placed spell the player cannot aim.");
            Assert.Greater(spell.range, 0f,
                "range 0 hands the cast distance back to a constant inside the executor — the "
                + "exact thing SpellTargeting exists to stop.");
        }

        [Test]
        public void TheExecutorResolvesItsLandingPointThroughTheSingleOwner()
        {
            string src = ExecutorSource();
            Assert.IsTrue(HasExecutableText(src, "SpellTargeting.ResolveGroundTarget("),
                "Ground-placed spells resolve where they land in ONE place. Re-inlining the "
                + "projection here is how two executors end up clamping to different ranges.");
            Assert.IsFalse(HasExecutableText(src, "ProjectileExecutor.ResolveCastStart("),
                "The cast start is SpellTargeting's business now; resolving it here as well "
                + "means two answers to one question.");
        }

        // ── Element and colour ─────────────────────────────────────────────────

        [Test]
        public void ElementIsAuthoredOnTheAsset_NotLeftToTheLegacyKeyTable()
        {
            var spell = Load();
            Assert.IsFalse(string.IsNullOrWhiteSpace(spell.element),
                "element is blank, so the spell's element comes from MapSpellKeyToElement — a "
                + "hard-coded switch that new spells are explicitly told not to grow.");

            SpellElement parsed;
            Assert.IsTrue(System.Enum.TryParse(spell.element, true, out parsed),
                "element '" + spell.element + "' does not parse to a SpellElement, so it falls "
                + "silently back to the key table and the field looks authored while doing nothing.");
            Assert.AreEqual(SpellElement.Arcane, parsed);

            // Both halves must agree, or damage mitigation and the visuals split.
            Assert.AreEqual(SpellElement.Arcane, ProjectileExecutor.ResolveElement(spell));
        }

        [Test]
        public void ParticleColourIsAuthored_NotTheOpaqueWhiteSentinel()
        {
            var spell = Load();
            var c = spell.particleColor;

            // Opaque white is the project's "nobody touched this" sentinel (KiPalette.IsUnauthored),
            // and a spell that means white is indistinguishable from one nobody authored.
            bool sentinel = c.a >= 0.99f && c.r >= 0.99f && c.g >= 0.99f && c.b >= 0.99f;
            Assert.IsFalse(sentinel,
                "particleColor is the unauthored sentinel, so the cast flourish keeps the element "
                + "palette by luck rather than because anyone chose the colour.");

            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            Assert.Greater(s, 0.2f, "an achromatic swatch desaturates the gather — this spell is violet.");
            Assert.Greater(v, 0.5f,
                "a near-black swatch adds nothing on an additive material: the flourish would not "
                + "dim, it would disappear (hostile_slash_dark's 0.04 grey is the recorded case).");
        }

        // ── Balance ────────────────────────────────────────────────────────────

        [Test]
        public void TheCooldownOutlastsTheField()
        {
            var spell = Load();

            // Same consequence VortexFieldTests pins for the two vortices. With maxInstances 1
            // a cooldown shorter than the duration means the player always has one out AND can
            // evict their own to reposition it, so a persistent ground hazard lands as a
            // permanent one. It shipped at cooldown 2 against a 5 s field.
            Assert.Greater(spell.cooldownDuration, spell.duration,
                "cooldown " + spell.cooldownDuration + " is shorter than its own "
                + spell.duration + "s field, so the zone is permanently up");
            Assert.AreEqual(1, spell.maxInstances,
                "the cooldown rule above is only the whole story while one flame can be out at a time.");
        }

        [Test]
        public void TheZoneDamagesForMostOfTheLifeItAdvertises()
        {
            var spell = Load();

            // Damage stops when the dissipation ramp starts — a flame that is leaving is not
            // burning. That is right, and it means the advertised duration overstates the
            // damaging window by exactly one ramp; the beat has to stay small against it.
            Assert.Greater(spell.duration, spell.tickPeriod * 4f,
                "fewer than four beats is a burst wearing a duration.");
            Assert.Greater(spell.damagePerTick, 0f, "a damage zone that deals none is a decal.");
        }
    }
}
