using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Every particle preset a spell names must exist in the catalog.
    ///
    /// Spells reference presets by string id. Nothing in the engine validates that
    /// reference: a typo, a renamed preset, or a preset asset that never got added to
    /// ParticlePresetCatalog all fail the same silent way — VFXManager returns null,
    /// the spell fires, and the effect is simply invisible. There is no error, no
    /// warning, and no way to tell from playing it whether the spell is supposed to
    /// look like that.
    ///
    /// This is the guard for the whole catalog, not one spell, because the failure is a
    /// property of the reference mechanism rather than of any particular effect.
    /// </summary>
    [TestFixture]
    public class SpellVfxPresetIntegrityTests
    {
        private const string SPELL_CATALOG    = "Assets/_Project/Data/Catalogs/SpellCatalog.asset";
        private const string PARTICLE_CATALOG = "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        private SpellCatalog _spells;
        private ParticlePresetCatalog _particles;

        [SetUp]
        public void SetUp()
        {
            _spells = AssetDatabase.LoadAssetAtPath<SpellCatalog>(SPELL_CATALOG);
            _particles = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(PARTICLE_CATALOG);
        }

        /// <summary>Every spell in the catalog, skipping null slots.</summary>
        private IEnumerable<SpellDefinition> Spells()
        {
            if (_spells == null) yield break;
            foreach (var s in _spells.AllSpells)
                if (s != null) yield return s;
        }

        /// <summary>
        /// Spell→preset references that are already broken, recorded so the guard can be
        /// switched on without inventing art direction for four unrelated spells.
        ///
        /// Each of these names a preset that has never existed in the catalog, so the spell
        /// fires today with an invisible trail and nothing logged — which is exactly the
        /// failure this fixture exists to make impossible in future.
        ///
        /// THIS LIST ONLY SHRINKS. Authoring the missing preset (or clearing the dead
        /// reference) and deleting the line here is the fix;
        /// <see cref="BaselineHasNoStaleEntries"/> fails if a line outlives its defect.
        /// </summary>
        private static readonly string[] KnownMissingPresets =
        {
            "vortex_pull → vfx 'vortex_dark'",
            "vortex_push → vfx 'vortex_dark'",
            "flame_breath → vfx 'breath_fire'",
            "root_whip → vfx 'root_whip'",
        };

        /// <summary>Every unresolved spell→preset reference in the project right now.</summary>
        private List<string> FindUnresolvedReferences()
        {
            var missing = new List<string>();
            foreach (var spell in Spells())
            {
                foreach (var id in spell.CollectVfxPresets())
                    if (_particles.GetById(id) == null)
                        missing.Add($"{spell.spellKey} → vfx '{id}'");

                foreach (var id in spell.CollectImpactPresets())
                    if (_particles.GetById(id) == null)
                        missing.Add($"{spell.spellKey} → impact '{id}'");
            }
            return missing;
        }

        [Test]
        public void BothCatalogsResolve()
        {
            Assert.IsNotNull(_spells, $"SpellCatalog missing at {SPELL_CATALOG} — this fixture " +
                                      "silently checks nothing without it.");
            Assert.IsNotNull(_particles, $"ParticlePresetCatalog missing at {PARTICLE_CATALOG}.");
            Assert.IsNotEmpty(Spells().ToList(), "SpellCatalog is empty.");
        }

        [Test]
        public void NoNewSpellPresetReferenceIsBroken()
        {
            var unexpected = FindUnresolvedReferences().Except(KnownMissingPresets).ToList();

            Assert.IsEmpty(unexpected,
                "These spells name a particle preset that does not resolve. The spell still " +
                "fires; it just has no visible effect, and nothing is logged — so the cost lands " +
                "on whoever plays it, not on whoever broke it. Fix by authoring the preset and " +
                "registering it in ParticlePresetCatalog, or by clearing the dead reference on " +
                "the spell.\n\n  " +
                string.Join("\n  ", unexpected));
        }

        [Test]
        public void BaselineHasNoStaleEntries()
        {
            var stale = KnownMissingPresets.Except(FindUnresolvedReferences()).ToList();

            Assert.IsEmpty(stale,
                "These baseline entries no longer describe reality — the preset now exists, or " +
                "the reference was cleared. Delete the lines from KnownMissingPresets so the " +
                "backlog stays honest and only ever shrinks.\n\n  " +
                string.Join("\n  ", stale));
        }

        [Test]
        public void NoSpellListsTheSamePresetTwice()
        {
            var dupes = new List<string>();

            foreach (var spell in Spells())
            {
                // CollectPresets dedupes, so compare against the raw declaration.
                var raw = new List<string>();
                if (!string.IsNullOrWhiteSpace(spell.vfxPreset)) raw.Add(spell.vfxPreset);
                if (spell.vfxPresetLayers != null) raw.AddRange(spell.vfxPresetLayers.Where(x => !string.IsNullOrWhiteSpace(x)));

                if (raw.Count != raw.Distinct().Count())
                    dupes.Add($"{spell.spellKey} trail: {string.Join(", ", raw)}");
            }

            Assert.IsEmpty(dupes,
                "A preset listed twice spawns two identical emitters, doubling its particle cost " +
                "for no visual gain. Collect* hides it at runtime, so only this catches it.\n\n  " +
                string.Join("\n  ", dupes));
        }

        [Test]
        public void Fireball_IsLayered_NotASingleFlatEmitter()
        {
            var fireball = Spells().FirstOrDefault(s => s.spellKey == "fireball");
            Assert.IsNotNull(fireball, "fireball is missing from the SpellCatalog.");

            var trail = fireball.CollectVfxPresets();
            var impact = fireball.CollectImpactPresets();

            Assert.GreaterOrEqual(trail.Count, 3,
                "The fireball's look depends on stacking: an additive core, a wake, sparks and " +
                "an alpha-blended smoke mass. One emitter is one material and one behaviour, so " +
                "collapsing this back to a single preset is a visual regression, not a cleanup.");
            Assert.GreaterOrEqual(impact.Count, 3,
                "An impact reads as one event but is built from several — flash, shockwave, " +
                "debris, smoke.");
        }

        [Test]
        public void FireballLayers_MixAdditiveAndAlphaBlending()
        {
            var fireball = Spells().FirstOrDefault(s => s.spellKey == "fireball");
            Assert.IsNotNull(fireball);

            var defs = fireball.CollectVfxPresets()
                .Select(id => _particles.GetById(id))
                .Where(d => d != null && d.vfx != null)
                .ToList();

            Assert.IsTrue(defs.Any(d => d.vfx.additive),
                "Fire emits light. Without at least one additive layer the effect can never " +
                "read as incandescent — it only ever occludes what is behind it.");
            Assert.IsTrue(defs.Any(d => !d.vfx.additive),
                "Smoke blocks light. An all-additive stack has no mass and washes out to white " +
                "wherever layers overlap.");
        }
    }
}
