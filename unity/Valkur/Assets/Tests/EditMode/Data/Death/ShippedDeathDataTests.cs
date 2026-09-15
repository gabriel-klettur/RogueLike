using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data.Death
{
    /// <summary>
    /// The test that would have caught the shipped bug, and the only one here that reads BOTH sides
    /// of it.
    ///
    /// <para><b>What actually happened.</b> The resurrection altar — a stone arch — was placed in
    /// the world, in <c>lobby</c> at (693, 815), under building template <b>197</b>. The code looked
    /// for template <b>249</b>. Both ids point at the same sprite,
    /// <c>Buildings/portals/portal_stone_arch</c>, and differ only in <c>splitRatio</c>; the placed
    /// instance even overrides its own ratio to 249's value, so somebody placed it deliberately AS
    /// the altar. Nothing failed. The binder bound nothing, the trail drew nothing, and
    /// <c>Revive()</c> was never called by anybody, while an altar stood in the world looking
    /// exactly right.</para>
    ///
    /// <para><b>Why no unit test could have caught it.</b> Each half was internally consistent: the
    /// binder correctly bound template 249, and the world correctly contained template 197. Only
    /// the COMPOSITION was wrong. That is the same shape as
    /// <c>SPAWNER_COORDINATE_SPACE_DRIFT</c> and needs the same answer — assert the composition, and
    /// assert it against the SHIPPED BYTES rather than against a fixture.</para>
    /// </summary>
    public class ShippedDeathDataTests
    {
        private static string BuildingsInstancesPath =>
            Path.Combine(Application.streamingAssetsPath, "Buildings", "buildings_instances.json");

        /// <summary>
        /// The shipped tuning — the one the GAME loads, not a fresh instance. A test that
        /// constructed its own would be comparing the world against the class defaults and would
        /// stay green with a shipped asset that had had its altar list emptied.
        /// </summary>
        private static DeathTuning ShippedTuning()
        {
            var tuning = Resources.Load<DeathTuning>(DeathTuning.ResourcePath);
            Assert.That(tuning, Is.Not.Null,
                $"No DeathTuning at Resources/{DeathTuning.ResourcePath}. Every reader falls back to " +
                "the class defaults, which works — but it means nothing an author tunes in ESC > " +
                "Muerte survives a restart. Press GUARDAR in that editor to create it.");
            return tuning;
        }

        private static List<int> PlacedTemplateIds()
        {
            Assert.That(File.Exists(BuildingsInstancesPath), Is.True,
                "buildings_instances.json is missing: " + BuildingsInstancesPath);

            string json = File.ReadAllText(BuildingsInstancesPath);

            // Regex rather than the MiniJson parser: this fixture must not depend on the assembly
            // that owns the parser, and the field is a flat integer with a fixed name. A malformed
            // file surfaces as "zero placements", which the assertions below already report.
            return Regex.Matches(json, "\"template_id\"\\s*:\\s*(-?\\d+)")
                        .Cast<Match>()
                        .Select(m => int.Parse(m.Groups[1].Value))
                        .ToList();
        }

        /// <summary>
        /// THE composition test. At least one building in the shipped world must carry a template
        /// the shipped tuning calls an altar.
        ///
        /// <para>Without this, the whole death subsystem can be perfectly implemented and perfectly
        /// unusable, which is exactly the state it shipped in.</para>
        /// </summary>
        [Test]
        public void TheShippedWorld_ContainsAtLeastOneAltar()
        {
            var tuning = ShippedTuning();
            var placed = PlacedTemplateIds();

            Assert.That(placed, Is.Not.Empty, "no buildings are placed at all");
            Assert.That(tuning.altarTemplateIds, Is.Not.Null.And.Not.Empty,
                "the tuning lists no altar templates, so nothing in any world can ever be an altar");

            var altarsPlaced = placed.Where(tuning.IsAltarTemplate).ToList();

            Assert.That(altarsPlaced, Is.Not.Empty,
                "No placed building carries an altar template. The player can die and never revive " +
                "except through the rescue. Tuning lists [" +
                string.Join(", ", tuning.altarTemplateIds) + "]; the world places [" +
                string.Join(", ", placed.Distinct().OrderBy(i => i)) + "].");
        }

        /// <summary>
        /// Both ids of the stone arch stay listed.
        ///
        /// <para>They are the same sprite under two templates, and which one an author reaches for
        /// in the Buildings editor is a coin flip — the world already contains the one the code did
        /// not know about. Dropping either re-opens the original bug for whoever places the other,
        /// and it would look exactly right on screen.</para>
        /// </summary>
        [Test]
        public void BothStoneArchTemplates_CountAsAltars()
        {
            var tuning = ShippedTuning();

            Assert.That(tuning.IsAltarTemplate(197), Is.True,
                "197 is the stone arch the shipped world actually places");
            Assert.That(tuning.IsAltarTemplate(249), Is.True,
                "249 is the same arch under its other template — the id the code used to look for");
        }

        /// <summary>
        /// Every listed altar template must exist in the building catalogue.
        ///
        /// <para>A template id nobody can place is an altar that can never exist, and it fails the
        /// same silent way the original did — the list looks populated and binds nothing.</para>
        /// </summary>
        [Test]
        public void EveryAltarTemplate_ExistsInTheCatalogue()
        {
            var tuning = ShippedTuning();

            string dir = Path.Combine(Application.dataPath, "_Project", "Data", "Catalogs", "Buildings");
            Assert.That(Directory.Exists(dir), Is.True, "building catalogue folder missing: " + dir);

            var missing = tuning.altarTemplateIds
                .Where(id => !File.Exists(Path.Combine(dir, $"BuildingTemplate_{id}.asset")))
                .ToList();

            Assert.That(missing, Is.Empty,
                "these altar template ids have no BuildingTemplateData asset, so no building can " +
                "ever carry them: " + string.Join(", ", missing));
        }

        /// <summary>
        /// The shipped altars must be altars because their TEMPLATE says so, not only because a
        /// tuning list happens to name their id.
        ///
        /// <para>The flag on <c>BuildingTemplateData</c> is the model; <c>altarTemplateIds</c> is a
        /// legacy bridge that <c>ResurrectionAltarRegistry.IsAltar</c> still ORs in so no
        /// already-shipped world loses its altar. This is the test that says the MIGRATION actually
        /// happened — without it the bridge would quietly stay the real model forever, and the
        /// flag would be a feature nobody's data uses.</para>
        /// </summary>
        [Test]
        public void TheShippedAltars_CarryTheFlagOnTheirOwnTemplate()
        {
            var tuning = ShippedTuning();
            var placed = PlacedTemplateIds().Distinct().ToList();

            string dir = Path.Combine(Application.dataPath, "_Project", "Data", "Catalogs", "Buildings");
            var viaListOnly = new List<int>();
            int viaFlag = 0;

            foreach (int id in placed)
            {
                if (!tuning.IsAltarTemplate(id)) continue;

                string file = Path.Combine(dir, $"BuildingTemplate_{id}.asset");
                if (!File.Exists(file)) continue;

                // Read the FILE, not the loaded asset: AssetDatabase hands back Unity's in-memory
                // copy, so a test that goes through it agrees with itself whatever is on disk —
                // which is how a data migration passes without having been written.
                bool flagged = Regex.IsMatch(File.ReadAllText(file), @"isResurrectionAltar:\s*1");
                if (flagged) viaFlag++;
                else viaListOnly.Add(id);
            }

            Assert.That(viaListOnly, Is.Empty,
                "these placed altar templates are altars ONLY through the legacy id list, so the " +
                "property never made it onto the building: " + string.Join(", ", viaListOnly));
            Assert.That(viaFlag, Is.GreaterThan(0),
                "no placed altar template carries isResurrectionAltar, so nothing exercises the " +
                "flag the whole layer is built on");
        }

        /// <summary>
        /// Every altar template declares a reach the Proximity anchor can actually use.
        ///
        /// <para>A radius of 0 on a Proximity altar shrinks the zone to the building's own padded
        /// rect — which still works, but silently means something different from what the field
        /// says, and is the shape of every "authored and inert" defect this project keeps finding.</para>
        /// </summary>
        [Test]
        public void EveryAltarTemplate_DeclaresAUsableReach()
        {
            string dir = Path.Combine(Application.dataPath, "_Project", "Data", "Catalogs", "Buildings");
            var bad = new List<string>();
            int inspected = 0;

            foreach (string file in Directory.GetFiles(dir, "BuildingTemplate_*.asset"))
            {
                string text = File.ReadAllText(file);
                if (!Regex.IsMatch(text, @"isResurrectionAltar:\s*1")) continue;

                inspected++;
                var m = Regex.Match(text, @"resurrectionRadius:\s*([0-9.]+)");
                if (!m.Success || float.Parse(m.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture) < 0.25f)
                    bad.Add(Path.GetFileNameWithoutExtension(file));
            }

            Assert.That(inspected, Is.GreaterThan(0),
                "no template in the catalogue is flagged as an altar, so this guard is vacuous");
            Assert.That(bad, Is.Empty,
                "these altar templates declare no usable resurrectionRadius: " + string.Join(", ", bad));
        }

        /// <summary>
        /// The corpse compass and the corpse's lifetime are ONE decision in two fields.
        ///
        /// <para>The trail hangs off <c>DeathSequenceController.ActiveCorpse</c>, so a linger of 0
        /// destroys the marker on the frame the player stands up — the start of the one walk the
        /// compass exists for. Switching the compass on while leaving the linger at 0 is a setting
        /// that reports itself as enabled and draws nothing.</para>
        ///
        /// <para>This is not hypothetical: the shipped asset was CREATED before the linger default
        /// moved from 0 to 120, so it carried a compass that could never point anywhere while the
        /// class it came from said otherwise. Data lagging code is the same drift as a stale
        /// <c>.meta</c> after a re-bake, and only a composition test sees it.</para>
        /// </summary>
        [Test]
        public void TheCorpseCompass_OutlivesTheRevive_ItIsPointingPast()
        {
            var t = ShippedTuning();
            if (!t.showCorpseCompass) Assert.Pass("compass disabled; nothing to outlive");

            Assert.That(t.corpseLingerSeconds, Is.GreaterThan(0f),
                "showCorpseCompass is on but the corpse is destroyed the instant the player " +
                "revives, so the trail it hangs off has nothing to point at for the whole walk back.");
        }

        /// <summary>
        /// The tuning's own values must stay inside the ranges its sliders enforce.
        ///
        /// <para>The asset can be edited in the Inspector, where a <c>[Range]</c> clamps — and by a
        /// merge, a script, or a hand edit, where nothing does. A rescue fraction above 1 or a
        /// negative delay is not something any reader validates.</para>
        /// </summary>
        [Test]
        public void TheShippedTuning_IsInsideItsOwnRanges()
        {
            var t = ShippedTuning();

            Assert.That(t.dyingFlashDuration, Is.InRange(0f, 3f));
            Assert.That(t.grayscaleFadeIn, Is.InRange(0f, 5f));
            Assert.That(t.grayscaleFadeOut, Is.InRange(0f, 5f));
            Assert.That(t.spiritSpeedMultiplier, Is.InRange(0.25f, 3f));
            Assert.That(t.spiritTimeLimitSeconds, Is.InRange(0f, 600f));
            Assert.That(t.altarActivationPadding, Is.InRange(0f, 4f));
            Assert.That(t.rescueDelayWithoutAltar, Is.InRange(1f, 120f));
            Assert.That(t.rescueHpFraction, Is.InRange(0.05f, 1f));
            Assert.That(t.pathUpdateInterval, Is.InRange(0.05f, 2f));
            Assert.That(t.pathMaxMarkers, Is.InRange(8, 600));
            Assert.That(t.coinLossFraction, Is.InRange(0f, 1f));
            Assert.That(t.xpLossFraction, Is.InRange(0f, 1f));
            Assert.That(t.corpseLingerSeconds, Is.InRange(0f, 600f));
        }
    }
}
