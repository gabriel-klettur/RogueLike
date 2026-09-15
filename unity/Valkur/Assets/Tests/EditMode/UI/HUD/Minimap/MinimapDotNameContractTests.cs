using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.UI.HUD.Minimap
{
    /// <summary>
    /// <c>Valkur.Gameplay</c> may not reference <c>Valkur.UI</c>, so a minimap dot type
    /// crosses that boundary AS A STRING through
    /// <c>EntitySetup.ConfigureMinimapDot(go, "Monster", …)</c> and is resolved by
    /// reflection. Nothing the compiler can see relates that literal to
    /// <see cref="MinimapDotType"/>.
    ///
    /// <para>It shipped wrong: <c>AlliedSummonService.Adopt</c> passed <c>"Ally"</c> against
    /// an enum that held only Player / Monster / NPC, and <c>Enum.Parse</c> threw out of the
    /// middle of the adoption — so every summoned ally was left with no spirit tint, no green
    /// health bar and no dismissal hook, and the exception surfaced as an
    /// <c>ArgumentException</c> from a VFX file that had nothing to do with the minimap.</para>
    ///
    /// <para>Two halves, and they are different questions. The first reads the production
    /// SOURCE, so a new call site with a name nobody added to the enum is a red test rather
    /// than a runtime throw. The second pins that an unresolvable name costs the DOT and never
    /// the spawn — a guarantee a <c>controls.json</c>-style hand edit or a future rename must
    /// not be able to take away.</para>
    /// </summary>
    public class MinimapDotNameContractTests
    {
        private const string ScriptsRoot = "Assets/_Project/Scripts";

        /// <summary>
        /// The bad-name warning is emitted once per name for the life of the domain, so a
        /// second fixture touching the same name would leave LogAssert.Expect waiting for a
        /// line that was already spent. Clearing the set is what makes the expectation real.
        /// </summary>
        [SetUp]
        public void ClearWarnedNames()
        {
            var field = typeof(EntitySetup).GetField("_warnedDotTypeNames",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(field, "EntitySetup._warnedDotTypeNames was renamed.");
            ((HashSet<string>)field.GetValue(null)).Clear();
        }

        /// <summary>Every string literal handed to ConfigureMinimapDot in shipped code.</summary>
        private static IEnumerable<(string file, string name)> CallSiteNames()
        {
            var rx = new Regex(@"ConfigureMinimapDot\s*\(\s*[^,]+,\s*""([^""]*)""");
            foreach (var path in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(path);
                foreach (Match m in rx.Matches(text))
                    yield return (path.Replace('\\', '/'), m.Groups[1].Value);
            }
        }

        [Test]
        public void EveryCallSiteName_IsADefinedMinimapDotType()
        {
            var offenders = new List<string>();
            int seen = 0;

            foreach (var (file, name) in CallSiteNames())
            {
                seen++;
                if (!System.Enum.IsDefined(typeof(MinimapDotType), name))
                    offenders.Add($"{file}: \"{name}\"");
            }

            Assert.Greater(seen, 0,
                "No ConfigureMinimapDot call sites found — the scan regex has gone stale, " +
                "which makes this fixture pass by measuring nothing.");
            CollectionAssert.IsEmpty(offenders,
                "These names do not exist in MinimapDotType, so the dot is silently skipped:\n" +
                string.Join("\n", offenders));
        }

        [Test]
        public void TheAllySummonName_Exists()
        {
            // The specific value that shipped broken. Named on its own so a regression is one
            // line of failure text rather than an entry in a list.
            Assert.IsTrue(System.Enum.IsDefined(typeof(MinimapDotType), "Ally"),
                "AlliedSummonService colours its summon's dot by the name \"Ally\".");
        }

        [Test]
        public void DotTypeValues_AreNotRenumbered()
        {
            // The enum is serialized on every MinimapDot component, so an insertion in the
            // middle silently repaints existing dots. New values append.
            Assert.AreEqual(0, (int)MinimapDotType.Player);
            Assert.AreEqual(1, (int)MinimapDotType.Monster);
            Assert.AreEqual(2, (int)MinimapDotType.NPC);
            Assert.AreEqual(3, (int)MinimapDotType.Ally);
        }

        [Test]
        public void UnknownName_WarnsAndSkipsTheDot_WithoutThrowing()
        {
            var go = new GameObject("minimap_dot_probe");
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("not a MinimapDotType value"));
                Assert.DoesNotThrow(() =>
                    EntitySetup.ConfigureMinimapDot(go, "NotADotType", Color.white));

                // The component is still added — the caller asked for a dot — but it keeps
                // whatever type it had rather than the call aborting the spawn.
                var dot = go.GetComponent<MinimapDot>();
                Assert.IsNotNull(dot);
                Assert.AreEqual(MinimapDotType.NPC, dot.DotType, "the serialized default");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void KnownName_IsApplied()
        {
            var go = new GameObject("minimap_dot_probe");
            try
            {
                var green = new Color(0.4f, 0.95f, 0.5f, 1f);
                EntitySetup.ConfigureMinimapDot(go, "Ally", green);

                var dot = go.GetComponent<MinimapDot>();
                Assert.IsNotNull(dot);
                Assert.AreEqual(MinimapDotType.Ally, dot.DotType);
                Assert.AreEqual(green, dot.DotColor);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
