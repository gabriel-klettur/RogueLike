using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Keeps <see cref="SpellFieldRelevance"/> honest against the code it describes.
    ///
    /// The map decides which rows the Spells Editor shows for a spell. Hiding a field an
    /// executor genuinely reads is the dangerous direction: the designer never sees the
    /// number that governs the behaviour they are trying to change, and the spell looks
    /// broken rather than unconfigured. So the source of every executor is scanned and
    /// every <c>Spell.someField</c> it reads must be marked relevant for the types that
    /// executor serves.
    ///
    /// The reverse direction — a field marked relevant that nobody reads — is left
    /// deliberately loose. A spare row costs a designer one glance; a missing one costs
    /// an afternoon.
    /// </summary>
    [TestFixture]
    public class SpellFieldRelevanceTests
    {
        private static string ScriptsRoot =>
            Path.Combine(Application.dataPath, "_Project", "Scripts");

        private static string SpellsRoot => Path.Combine(ScriptsRoot, "Gameplay", "Spells");

        /// <summary>
        /// Sources that belong to a type but are not its executor: executors that hand a
        /// spell's values to a controller, or that branch into a bespoke implementation.
        /// Without these, Beam would look as though it reads nothing at all.
        /// </summary>
        private static readonly Dictionary<SpellType, string[]> ExtraSources =
            new Dictionary<SpellType, string[]>
        {
            { SpellType.Beam,       new[] { "Controllers/LaserBeamController.cs",
                                            "Controllers/LaserBeamController.Visual.cs" } },
            { SpellType.Slash,      new[] { "Executors/RegularSlashAttack.cs" } },
            { SpellType.ConeBreath, new[] { "Controllers/ConeBreathController.cs" } },
        };

        private static readonly Regex SpellFieldRead =
            new Regex(@"Spell\.([a-zA-Z][a-zA-Z0-9]*)", RegexOptions.Compiled);

        private static bool IsRealField(string name) =>
            typeof(SpellDefinition).GetField(name,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance) != null;

        private static IEnumerable<string> FieldsReadIn(string absolutePath)
        {
            if (!File.Exists(absolutePath)) yield break;

            foreach (var line in File.ReadAllLines(absolutePath))
            {
                string trimmed = line.TrimStart();
                // Doc comments name fields constantly; they are not reads.
                if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
                    continue;

                foreach (Match m in SpellFieldRead.Matches(line))
                {
                    string field = m.Groups[1].Value;
                    if (IsRealField(field)) yield return field;
                }
            }
        }

        private static SpellDefinition MakeSpell(SpellType type, string key = "relevance_probe")
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.type = type;
            spell.spellKey = key;
            return spell;
        }

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        // ── The map must not hide what the code reads ───────────────────────

        [Test]
        public void EveryFieldAnExecutorReadsIsShownForItsType()
        {
            var violations = new List<string>();

            foreach (SpellType type in System.Enum.GetValues(typeof(SpellType)))
            {
                var executor = SpellCaster.GetExecutor(type);
                // Types with no executor fall back to Projectile; they are covered by it.
                if (executor == null) continue;

                var sources = new List<string>
                {
                    Path.Combine(SpellsRoot, "Executors", executor.GetType().Name + ".cs"),
                };
                if (ExtraSources.TryGetValue(type, out var extra))
                    sources.AddRange(extra.Select(rel =>
                        Path.Combine(SpellsRoot, rel.Replace('/', Path.DirectorySeparatorChar))));

                var spell = MakeSpell(type);
                try
                {
                    foreach (var source in sources)
                    foreach (var field in FieldsReadIn(source).Distinct())
                    {
                        if (SpellFieldRelevance.Applies(spell, field)) continue;
                        violations.Add($"{type}: reads '{field}' in {Path.GetFileName(source)} " +
                                       "but the Spells Editor hides it");
                    }
                }
                finally { Object.DestroyImmediate(spell); }
            }

            Assert.IsEmpty(violations,
                "These fields drive behaviour but are filtered out of the properties panel, " +
                "so a designer cannot reach the number that governs what they are tuning.\n\n" +
                "Add the field to that type's set in SpellFieldRelevance.\n\n" +
                string.Join("\n  ", violations));
        }

        [Test]
        public void TheFieldsCalledDeadAreReadByNothing()
        {
            var dead = new[]
            {
                "offset", "hitArcDegrees", "length",
                "particleCount", "particleDispersion", "particleLifespan",
                "particleSpeed", "particleColors", "sizeRange", "emitRate",
            };

            var violations = new List<string>();
            foreach (var file in Directory.GetFiles(SpellsRoot, "*.cs", SearchOption.AllDirectories))
            foreach (var field in FieldsReadIn(file).Distinct())
            {
                if (!dead.Contains(field)) continue;
                violations.Add($"{Path.GetFileName(file)} reads '{field}', which the editor " +
                               "hides from every spell");
            }

            Assert.IsEmpty(violations,
                "A field listed as dead everywhere has come back to life. Move it out of " +
                "DeadEverywhere and into the sets of the types that now read it, or the " +
                "designer will never be able to author it.\n\n  " +
                string.Join("\n  ", violations));
        }

        // ── The case that prompted all this ─────────────────────────────────

        [Test]
        public void SlashRegularShowsTheShapeItReadsAndHidesTheOneItIgnores()
        {
            var spell = MakeSpell(SpellType.Slash, RegularSlashAttack.SpellKey);
            try
            {
                Assert.IsTrue(SpellFieldRelevance.Applies(spell, "hitRadius"),
                    "hitRadius is the radius slash_regular actually uses, for damage and visuals alike.");
                Assert.IsTrue(SpellFieldRelevance.Applies(spell, "arcRangeDegrees"));

                Assert.IsFalse(SpellFieldRelevance.Applies(spell, "radius"),
                    "radius is never read for slash_regular. Showing it is what led to tuning " +
                    "a number the spell ignores.");
                Assert.IsFalse(SpellFieldRelevance.Applies(spell, "hitArcDegrees"));
                Assert.IsFalse(SpellFieldRelevance.Applies(spell, "vfxPreset"),
                    "The crescent is code-native; the catalog presets never reach it.");
            }
            finally { Object.DestroyImmediate(spell); }
        }

        [Test]
        public void CastingAndCastOriginShowOnEverySpell()
        {
            foreach (SpellType type in System.Enum.GetValues(typeof(SpellType)))
            {
                var spell = MakeSpell(type);
                try
                {
                    foreach (var always in new[] { "spellKey", "displayName", "type", "manaCost",
                                                   "cooldownDuration", "castAnchor", "castForwardOffset" })
                        Assert.IsTrue(SpellFieldRelevance.Applies(spell, always),
                            $"{type} must still show '{always}' — it applies to every spell.");
                }
                finally { Object.DestroyImmediate(spell); }
            }
        }

        [Test]
        public void ASpellSpecificSetIsNarrowerThanItsTypeSet()
        {
            var generic = MakeSpell(SpellType.Slash, "some_other_slash");
            var bespoke = MakeSpell(SpellType.Slash, RegularSlashAttack.SpellKey);
            try
            {
                Assert.IsTrue(SpellFieldRelevance.Applies(generic, "vfxPreset"),
                    "An ordinary Slash still drives its look from the catalog preset.");
                Assert.IsFalse(SpellFieldRelevance.Applies(bespoke, "vfxPreset"),
                    "A per-key override replaces the type set rather than adding to it.");
            }
            finally
            {
                Object.DestroyImmediate(generic);
                Object.DestroyImmediate(bespoke);
            }
        }

        [Test]
        public void ANullSpellStillShowsEverythingLive()
        {
            // The form has no selection yet; filtering by a type we do not know would
            // blank the panel.
            Assert.IsTrue(SpellFieldRelevance.Applies(null, "wallHeight"));
            Assert.IsFalse(SpellFieldRelevance.Applies(null, "emitRate"),
                "Fields nothing reads stay hidden even with no selection.");
        }
    }
}
