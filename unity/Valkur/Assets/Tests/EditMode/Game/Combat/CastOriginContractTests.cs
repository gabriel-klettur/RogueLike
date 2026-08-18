using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Anything a caster fires must leave from the same place: hand height.
    ///
    /// <c>ProjectileExecutor.ResolveCasterCenter</c> returns the geometric middle of the
    /// caster's sprite, which on a humanoid with a feet pivot is the waist.
    /// <c>ResolveCastOrigin</c> lifts that to where the hands are. Both are legitimate —
    /// melee arcs, knockback directions and AOE origins genuinely want the body centre —
    /// but a spell that *emits* something from the caster and uses the centre visibly comes
    /// out of the character's stomach.
    ///
    /// The fireball had that bug and it was reported from a screenshot. The laser had the
    /// identical bug in a different file, and nothing connected the two. This is that
    /// connection: a source scan in the same shape as ZoomContractTests, so the next spell
    /// to reach for the wrong helper fails here instead of shipping.
    /// </summary>
    [TestFixture]
    public class CastOriginContractTests
    {
        /// <summary>
        /// Files allowed to resolve the BODY CENTRE. Every entry must be something that
        /// genuinely wants the middle of the character rather than its hands.
        /// </summary>
        private static readonly string[] BodyCentreCallsites =
        {
            // Defines both helpers; ResolveCastOrigin is built on top of ResolveCasterCenter.
            "Gameplay/Spells/Executors/ProjectileExecutor.cs",

            // A melee arc sweeps around the body, and knockback is measured from its middle.
            // Firing this from the hands would bias every swing upward.
            "Gameplay/Spells/Executors/SlashExecutor.cs",
        };

        private static readonly Regex BodyCentreCall = new Regex(
            @"\bResolveCasterCenter\s*\(", RegexOptions.Compiled);

        private static string ScriptsRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "_Project", "Scripts"));

        private static bool IsAuthorised(string fullPath)
        {
            string normalised = fullPath.Replace('\\', '/');
            return BodyCentreCallsites.Any(a => normalised.EndsWith(a.Replace('\\', '/'),
                                                                    System.StringComparison.OrdinalIgnoreCase));
        }

        [Test]
        public void ScriptsRootExists()
        {
            Assert.IsTrue(Directory.Exists(ScriptsRoot),
                $"{ScriptsRoot} not found — this fixture would silently scan nothing.");
        }

        [Test]
        public void OnlyBodyCentricSystemsResolveTheBodyCentre()
        {
            var violations = new List<string>();

            foreach (var file in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (IsAuthorised(file)) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();
                    // Comments and doc references name the helper legitimately.
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*")) continue;
                    if (!BodyCentreCall.IsMatch(line)) continue;

                    violations.Add($"{file.Replace('\\', '/')}:{i + 1}  →  {trimmed}");
                }
            }

            Assert.IsEmpty(violations,
                "These call ResolveCasterCenter, which is the caster's waist. A spell that " +
                "emits something from the caster should call ResolveCastOrigin instead, or it " +
                "visibly fires out of the character's stomach.\n\n" +
                "If the callsite genuinely wants the body centre — a melee arc, a knockback " +
                "direction, an AOE origin — add its path to BodyCentreCallsites with the " +
                "reason.\n\nCallsites:\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void TheLaserBeamFiresFromHandHeight()
        {
            // Named explicitly because it is the one that was wrong, and because a source
            // scan alone would go quiet if the beam simply stopped resolving an origin.
            string path = Path.Combine(ScriptsRoot, "Gameplay", "Spells", "Controllers",
                                       "LaserBeamController.cs");
            Assert.IsTrue(File.Exists(path), "LaserBeamController.cs moved — update this test.");

            string source = File.ReadAllText(path);
            Assert.IsTrue(source.Contains("ResolveCastOrigin"),
                "The beam must resolve its origin at hand height, like the fireball. It is " +
                "used for the raycast as well as the visuals so that what is drawn and what " +
                "is hit stay the same line.");
        }

        [Test]
        public void EveryAuthorisedCallsiteStillExists()
        {
            foreach (var rel in BodyCentreCallsites)
            {
                string path = Path.Combine(ScriptsRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(File.Exists(path),
                    $"'{rel}' is on the allow-list but no longer exists. A stale entry silently " +
                    "widens the contract — remove it.");
            }
        }
    }
}
