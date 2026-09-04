using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// The SECOND gate on a Properties row, which nothing used to guard.
    ///
    /// <para>A field reaches a designer only if two things are true: <see cref="SpellFieldRelevance"/>
    /// declares it relevant for the spell's type, AND the panel actually emits a row for it.
    /// <c>SpellFieldRelevanceTests</c> pins the first against what the executors read and says
    /// in as many words that the reverse direction is "left deliberately loose" — so a field
    /// the map declared relevant and the panel never rendered was invisible to every test in
    /// the suite.</para>
    ///
    /// <para>That is not hypothetical. <c>particleColor</c> reached 24 of the 28 spell types
    /// through the map — it retints the whole cast-flourish palette — and had no row at all,
    /// with CLAUDE.md recording the fix as done because the map half of it had landed.</para>
    ///
    /// <para>Source-scanning, in the style of <c>CameraFeelContractTests</c>: the panel builds
    /// its rows imperatively at runtime, so the honest way to ask "does a row exist" without a
    /// live canvas is to read the calls.</para>
    /// </summary>
    [TestFixture]
    public class SpellsPanelFieldCoverageTests
    {
        private static string ScriptsRoot =>
            Path.Combine(Application.dataPath, "_Project", "Scripts");

        private static string SpellsEditorDir =>
            Path.Combine(ScriptsRoot, "Gameplay", "Editors", "Spells");

        /// <summary>
        /// Fields that legitimately have no row of their own, each with the reason. Anything
        /// not listed here and not rendered is a hole.
        /// </summary>
        private static readonly Dictionary<string, string> Exempt = new Dictionary<string, string>
        {
            { "sprite",     "assigned from the Assets tab's sprite browser, which needs thumbnails a text row cannot show" },
            { "iconSprite", "assigned from the Assets tab's icon browser, same reason" },
            { "statusApplications", "a variable-length array: rendered by AddStatusApplicationRows with its own keys" },
            { "statModifiers", "a variable-length array: rendered by AddStatModifierRows with its own keys" },
        };

        /// <summary>Every field name the Properties panel emits a row for.</summary>
        private static HashSet<string> RenderedKeys()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            // The source aligns calls as `form.AddBool ("x", …)`, so the whitespace before the
            // paren is not optional in this pattern — without \s* the scan silently misses
            // sixteen rows and reports them all as holes.
            var call = new Regex(@"form\.Add[A-Za-z]+\s*\(\s*""([A-Za-z0-9_]+)""");

            foreach (var file in Directory.GetFiles(SpellsEditorDir, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in call.Matches(File.ReadAllText(file)))
                    keys.Add(m.Groups[1].Value);

            return keys;
        }

        private static IEnumerable<SpellType> AllTypes()
            => Enum.GetValues(typeof(SpellType)).Cast<SpellType>();

        /// <summary>How many spell types declare this field relevant.</summary>
        private static int RelevantTypeCount(SpellDefinition probe, string field)
        {
            int n = 0;
            foreach (var t in AllTypes())
            {
                probe.type = t;
                if (SpellFieldRelevance.Applies(probe, field)) n++;
            }
            return n;
        }

        [Test]
        public void TheScannerFindsTheRowsThatObviouslyExist()
        {
            var keys = RenderedKeys();
            // A guard on the guard: a regex that matched nothing would make every other test
            // in this fixture pass by finding no rows to check against.
            foreach (var known in new[] { "spellKey", "damage", "allowOverlap", "cooldownDuration" })
                Assert.IsTrue(keys.Contains(known),
                    $"The row scan missed '{known}', which is plainly in the panel — the " +
                    "pattern is wrong and every assertion below is measuring nothing.");
        }

        [Test]
        public void EveryFieldTheMapCallsRelevantHasARow()
        {
            var keys = RenderedKeys();
            var probe = ScriptableObject.CreateInstance<SpellDefinition>();
            var holes = new List<string>();
            try
            {
                foreach (var field in typeof(SpellDefinition).GetFields())
                {
                    if (keys.Contains(field.Name)) continue;
                    if (Exempt.ContainsKey(field.Name)) continue;

                    int n = RelevantTypeCount(probe, field.Name);
                    if (n > 0)
                        holes.Add($"{field.Name} ({field.FieldType.Name}) — relevant for {n} type(s)");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }

            Assert.IsEmpty(holes,
                "SpellFieldRelevance says show these and the panel emits no row, so they are " +
                "unreachable from F4. Add a row, or add an entry to Exempt saying where else " +
                "the designer edits it:\n  " + string.Join("\n  ", holes));
        }

        [Test]
        public void NoRowIsRenderedForAFieldNoTypeEverShows()
        {
            var keys = RenderedKeys();
            var probe = ScriptableObject.CreateInstance<SpellDefinition>();
            var dead = new List<string>();
            try
            {
                foreach (var key in keys)
                {
                    // Keys belonging to the Gather tab and the status array address other
                    // objects entirely; they are not SpellDefinition fields by design.
                    if (typeof(SpellDefinition).GetField(key) == null) continue;
                    if (RelevantTypeCount(probe, key) == 0) dead.Add(key);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }

            Assert.IsEmpty(dead,
                "These rows are built and then hidden for every spell type in the game — " +
                "either the field belongs in the relevance map or the row should go:\n  " +
                string.Join("\n  ", dead));
        }

        [Test]
        public void EveryExemptFieldIsRealAndStillNeedsItsExemption()
        {
            var keys = RenderedKeys();
            foreach (var pair in Exempt)
            {
                Assert.IsNotNull(typeof(SpellDefinition).GetField(pair.Key),
                    $"'{pair.Key}' is exempted but is not a SpellDefinition field any more.");
                Assert.IsFalse(keys.Contains(pair.Key),
                    $"'{pair.Key}' now HAS a row, so its exemption is stale and hides the next " +
                    "regression on it. Drop it from Exempt.");
                Assert.IsNotEmpty(pair.Value, "An exemption without a reason is a TODO.");
            }
        }

        /// <summary>
        /// A row can exist and still not be writable: <c>ConvertValue</c> is what turns the
        /// widget's payload into the field's type, and anything it does not know falls through
        /// to "assign as-is", which throws and is swallowed as a warning. That failure mode is
        /// exactly what a colour row hit before <c>Color</c> was handled — a control that
        /// appears, accepts an edit and writes nothing.
        /// </summary>
        [Test]
        public void EveryRenderedFieldHasATypeTheEditorCanWrite()
        {
            var keys = RenderedKeys();
            var unwritable = new List<string>();

            foreach (var key in keys)
            {
                var field = typeof(SpellDefinition).GetField(key);
                if (field == null) continue;

                var t = field.FieldType;
                bool ok = t.IsEnum || t == typeof(float) || t == typeof(int)
                       || t == typeof(bool) || t == typeof(string) || t == typeof(Color)
                       || typeof(UnityEngine.Object).IsAssignableFrom(t);
                if (!ok) unwritable.Add($"{key} : {t.Name}");
            }

            Assert.IsEmpty(unwritable,
                "ConvertValue cannot produce these types, so the row edits nothing and the " +
                "failure is a swallowed warning:\n  " + string.Join("\n  ", unwritable));
        }
    }
}
