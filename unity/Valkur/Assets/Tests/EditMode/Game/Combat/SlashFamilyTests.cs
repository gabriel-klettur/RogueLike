using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// The slash family has one rule above all others: what is drawn is what damages.
    ///
    /// Before <see cref="SlashAttack"/> the legacy path damaged inside a circle centred half
    /// a radius forward, so its true reach was one and a half times the authored
    /// <c>hitRadius</c> — and the visual, a fixed blade sprite that ignored the arc
    /// entirely, was scaled to a fraction of that. A boss slash reached roughly 39 world
    /// units behind an effect about five units wide. These tests pin both halves of the
    /// contract: the geometry the code uses, and the authored numbers that geometry is fed.
    /// </summary>
    [TestFixture]
    public class SlashFamilyTests
    {
        private const string SpellCatalogPath = "Assets/_Project/Data/Catalogs/SpellCatalog.asset";

        /// <summary>
        /// Longest reach a slash may author, in world units. A slash is drawn as a swept
        /// sector around its caster; past this it stops reading as a swing and becomes an
        /// invisible area attack, which is what the old boss values had become.
        /// </summary>
        private const float MAX_AUTHORED_REACH = 8f;

        private SpellDefinition[] _slashes;

        [SetUp]
        public void SetUp()
        {
            if (_slashes != null) return;
            var catalog = AssetDatabase.LoadAssetAtPath<SpellCatalog>(SpellCatalogPath);
            Assert.IsNotNull(catalog, "SpellCatalog is missing.");
            _slashes = catalog.AllSpells
                .Where(s => s != null && s.type == SpellType.Slash)
                .ToArray();
            Assert.IsNotEmpty(_slashes, "No Slash spells in the catalog — this fixture would prove nothing.");
        }

        // ── Geometry ────────────────────────────────────────────────────────

        [Test]
        public void ArcBoundariesResolveToTheFourFamilies()
        {
            Assert.AreEqual(SlashStyle.Thrust, SlashAttack.StyleFor(40f));
            Assert.AreEqual(SlashStyle.Thrust, SlashAttack.StyleFor(55f));
            Assert.AreEqual(SlashStyle.Crescent, SlashAttack.StyleFor(56f));
            Assert.AreEqual(SlashStyle.Crescent, SlashAttack.StyleFor(108f));
            Assert.AreEqual(SlashStyle.Cleave, SlashAttack.StyleFor(109f));
            Assert.AreEqual(SlashStyle.Cleave, SlashAttack.StyleFor(175f));
            Assert.AreEqual(SlashStyle.Whirl, SlashAttack.StyleFor(176f));
        }

        [Test]
        public void TheDamageSectorStopsAtTheAuthoredRadiusAndArc()
        {
            Vector2 origin = new Vector2(-4f, 7f);
            Vector2 forward = Vector2.up;
            const float radius = 3f;
            const float arc = 120f;

            Assert.IsTrue(SlashAttack.IsInsideSector(origin, forward,
                origin + Vector2.up * radius, radius, arc),
                "A target at exactly the authored reach, dead ahead, is inside the swing.");

            Assert.IsFalse(SlashAttack.IsInsideSector(origin, forward,
                origin + Vector2.up * (radius + 0.05f), radius, arc),
                "Reach is the authored radius, not a multiple of it. This is the bug the " +
                "legacy overlap circle shipped for months.");

            Vector2 boundary = Quaternion.Euler(0f, 0f, arc * 0.5f) * Vector2.up;
            Assert.IsTrue(SlashAttack.IsInsideSector(origin, forward,
                origin + boundary * (radius * 0.9f), radius, arc));

            Vector2 outside = Quaternion.Euler(0f, 0f, arc * 0.5f + 3f) * Vector2.up;
            Assert.IsFalse(SlashAttack.IsInsideSector(origin, forward,
                origin + outside * (radius * 0.9f), radius, arc));

            Assert.IsFalse(SlashAttack.IsInsideSector(origin, forward,
                origin + Vector2.down * radius, radius, arc),
                "Nothing behind the caster is ever inside a forward swing.");
        }

        // ── Authored data ───────────────────────────────────────────────────

        [Test]
        public void EverySlashDrawsTheReachItDamagesWith()
        {
            var violations = new List<string>();

            foreach (var spell in _slashes)
            {
                float reach = spell.hitRadius > 0f ? spell.hitRadius : spell.range;
                if (reach <= 0f)
                    violations.Add($"{spell.spellKey}: no hitRadius and no range — the reach " +
                                   "falls back to a hardcoded default the designer cannot see");
                else if (reach > MAX_AUTHORED_REACH)
                    violations.Add($"{spell.spellKey}: reach {reach:F2} exceeds {MAX_AUTHORED_REACH} " +
                                   "world units — past that a swept sector stops reading as a swing");

                if (!Mathf.Approximately(spell.radius, spell.hitRadius))
                    violations.Add($"{spell.spellKey}: radius {spell.radius:F2} and hitRadius " +
                                   $"{spell.hitRadius:F2} disagree — only one of them is read");

                if (!Mathf.Approximately(spell.arcRangeDegrees, spell.hitArcDegrees))
                    violations.Add($"{spell.spellKey}: arcRangeDegrees {spell.arcRangeDegrees:F0} " +
                                   $"and hitArcDegrees {spell.hitArcDegrees:F0} disagree");
            }

            Assert.IsEmpty(violations,
                "A slash whose drawn shape and damaged shape are authored differently will " +
                "hit outside its visual somewhere.\n\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void EverySlashLeavesARecoveryBeatAfterItsVisual()
        {
            var violations = new List<string>();

            foreach (var spell in _slashes)
            {
                if (spell.lifetime <= 0f)
                    violations.Add($"{spell.spellKey}: lifetime {spell.lifetime:F2} — the swing " +
                                   "has no authored duration");

                float busy = spell.prepareDuration + spell.lifetime;
                if (spell.cooldownDuration <= busy)
                    violations.Add($"{spell.spellKey}: cooldown {spell.cooldownDuration:F2} does not " +
                                   $"outlast wind-up plus swing ({busy:F2}), so the next cast starts " +
                                   "before the last one has finished being seen");
            }

            Assert.IsEmpty(violations,
                "Melee reads through its recovery. Without one the attacks blur together.\n\n  " +
                string.Join("\n  ", violations));
        }

        [Test]
        public void AllFourSilhouettesAreActuallyUsed()
        {
            var used = new HashSet<SlashStyle>();
            foreach (var spell in _slashes)
            {
                float arc = spell.arcRangeDegrees > 0f ? spell.arcRangeDegrees : 90f;
                used.Add(SlashAttack.StyleFor(arc));
            }

            foreach (SlashStyle style in System.Enum.GetValues(typeof(SlashStyle)))
                Assert.IsTrue(used.Contains(style),
                    $"No slash in the catalog resolves to {style}. The family exists to give " +
                    "melee four readable shapes; an unused one is a shape the player never learns.");
        }

        [Test]
        public void NoSlashCarriesAParticleBudgetNothingReads()
        {
            var violations = new List<string>();

            foreach (var spell in _slashes)
            {
                if (!Mathf.Approximately(spell.offset, 0f))
                    violations.Add($"{spell.spellKey}: offset {spell.offset:F2}");
                if (spell.particleCount != 0)
                    violations.Add($"{spell.spellKey}: particleCount {spell.particleCount}");
                if (!Mathf.Approximately(spell.particleSpeed, 0f))
                    violations.Add($"{spell.spellKey}: particleSpeed {spell.particleSpeed:F2}");
                if (spell.sizeRange != null && spell.sizeRange.Count > 0)
                    violations.Add($"{spell.spellKey}: sizeRange[{spell.sizeRange.Count}]");
            }

            Assert.IsEmpty(violations,
                "These fields are in SpellFieldRelevance.DeadEverywhere: no pipeline reads them, " +
                "and the Spells Editor hides them. Leaving values behind makes a slash look " +
                "configured in ways it is not.\n\n  " + string.Join("\n  ", violations));
        }
    }
}
