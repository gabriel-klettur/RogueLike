using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Anything a caster places in the world resolves its origin through one helper:
    /// the spell's own <c>castAnchor</c> on the caster's body, plus its own forward
    /// clearance.
    ///
    /// <c>ProjectileExecutor.ResolveCasterCenter</c> returns the geometric middle of the
    /// caster's sprite, which on a humanoid with a feet pivot is the waist.
    /// <c>ResolveCastOrigin</c> lifts that to where the hands are. Both are legitimate —
    /// melee arcs, knockback directions and AOE origins genuinely want the body centre —
    /// but a spell that *emits* something from the caster and uses the centre visibly comes
    /// out of the character's stomach. <c>ResolveCastStart</c> adds Fireball's collider
    /// clearance and is the canonical origin for every such spell.
    ///
    /// This source contract keeps every executor and controller connected to that single
    /// origin, so none of them can silently reintroduce its own offset — and, since the
    /// anchor became per-spell, that each of them actually passes the spell through
    /// instead of quietly taking the system default.
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
            // Defines the body, hand and shared Fireball-start helpers.
            "Gameplay/Spells/Executors/ProjectileExecutor.cs",
        };

        /// <summary>
        /// Every implementation that places something relative to its caster. Ground-targeted
        /// zones, auras and summons are on the list too: their anchor is authored per spell
        /// now, so they resolve through the same helper and their own distance term rides on
        /// top of it.
        ///
        /// Deliberately absent: DashExecutor and TeleportExecutor. Those move the caster's
        /// body rather than spawning an effect, so anchoring their maths to hand height would
        /// teleport the character upward.
        ///
        /// Also absent, for the same shape of reason: ShieldExecutor. The magic shield is a
        /// sphere ENCLOSING the caster, centred on their sprite bounds — it is not emitted from
        /// anywhere. Resolving it to hand height plus forward clearance would push the sphere
        /// up and forward and leave the character standing off-centre inside their own shield.
        /// It was on this list only because the version that drew a flat disc also spawned a
        /// 0.4 s telegraph indicator at the cast start, and that telegraph is gone.
        /// </summary>
        private static readonly string[] CasterEmissionCallsites =
        {
            "Gameplay/Spells/Controllers/ConeBreathController.cs",
            "Gameplay/Spells/Controllers/LaserBeamController.cs",
            "Gameplay/Spells/Executors/ArcaneFlameExecutor.cs",
            "Gameplay/Spells/Executors/AreaExecutor.cs",
            "Gameplay/Spells/Executors/AuraExecutor.cs",
            "Gameplay/Spells/Executors/BoomerangExecutor.cs",
            "Gameplay/Spells/Executors/ConeBreathExecutor.cs",
            "Gameplay/Spells/Executors/FireworkLaunchExecutor.cs",
            "Gameplay/Spells/Executors/LightningExecutor.cs",
            "Gameplay/Spells/Executors/MeteorExecutor.cs",
            "Gameplay/Spells/Executors/MineExecutor.cs",
            "Gameplay/Spells/Executors/ProjectileExecutor.cs",
            "Gameplay/Spells/Executors/PuddleExecutor.cs",
            "Gameplay/Spells/Executors/SlashExecutor.cs",
            "Gameplay/Spells/Executors/SmokeEmitterExecutor.cs",
            "Gameplay/Spells/Executors/SmokeExecutor.cs",
            "Gameplay/Spells/Executors/SummonExecutor.cs",
            "Gameplay/Spells/Executors/TotemExecutor.cs",
            "Gameplay/Spells/Executors/VortexFieldExecutor.cs",
            "Gameplay/Spells/Executors/WallExecutor.cs",
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
        public void EveryCasterEmissionUsesTheExactFireballStart()
        {
            var violations = new List<string>();

            foreach (var rel in CasterEmissionCallsites)
            {
                string path = Path.Combine(ScriptsRoot,
                    rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    violations.Add($"{rel}: file is missing");
                    continue;
                }

                int executableCalls = File.ReadAllLines(path).Count(line =>
                {
                    string trimmed = line.TrimStart();
                    return !trimmed.StartsWith("//")
                        && !trimmed.StartsWith("///")
                        && !trimmed.StartsWith("*")
                        && line.Contains("ResolveCastStart(");
                });

                // ProjectileExecutor contains the helper declaration plus Fireball's call.
                int requiredCalls = rel.EndsWith("ProjectileExecutor.cs") ? 2 : 1;
                if (executableCalls < requiredCalls)
                    violations.Add($"{rel}: expected {requiredCalls} ResolveCastStart call(s), found {executableCalls}");
            }

            Assert.IsEmpty(violations,
                "Every spell that visibly leaves the caster must use Fireball's exact " +
                "ResolveCastStart point (hand height plus forward clearance). Ground-targeted " +
                "spells do not belong on this list.\n\nViolations:\n  " +
                string.Join("\n  ", violations));
        }

        /// <summary>
        /// The anchor is authored per spell, so a callsite that resolves the origin without
        /// handing the spell over silently ignores whatever the designer set and falls back
        /// to Hands + the default clearance. That is invisible in play and invisible in a
        /// diff, so it is pinned here.
        /// </summary>
        [Test]
        public void EveryCallsitePassesTheSpellsOwnCastOrigin()
        {
            var violations = new List<string>();

            foreach (var rel in CasterEmissionCallsites)
            {
                // ProjectileExecutor declares the overloads, including the deliberate
                // system-default one, so its own text is exempt from this rule.
                if (rel.EndsWith("ProjectileExecutor.cs")) continue;

                string path = Path.Combine(ScriptsRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) { violations.Add($"{rel}: file is missing"); continue; }

                string src = File.ReadAllText(path);
                int from = 0;
                while (true)
                {
                    int call = src.IndexOf("ResolveCastStart(", from, System.StringComparison.Ordinal);
                    if (call < 0) break;
                    from = call + 1;

                    int end = src.IndexOf(';', call);
                    string args = end < 0 ? src.Substring(call) : src.Substring(call, end - call);

                    // Either the spell itself, or the anchor + clearance a controller
                    // adopted from it up front.
                    if (args.Contains("Spell") || args.Contains("_castAnchor")) continue;

                    int lineNumber = src.Take(call).Count(c => c == '\n') + 1;
                    violations.Add($"{rel}:{lineNumber}  ->  {args.Trim()}");
                }
            }

            Assert.IsEmpty(violations,
                "These resolve the cast origin without passing the spell, so castAnchor and " +
                "castForwardOffset are ignored and the effect always comes out of the hands.\n\n" +
                "Pass ctx.Spell (executors) or adopt it once via SetCastOrigin (controllers).\n\n" +
                "Callsites:\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void EveryAuthorisedCallsiteStillExists()
        {
            foreach (var rel in BodyCentreCallsites.Concat(CasterEmissionCallsites).Distinct())
            {
                string path = Path.Combine(ScriptsRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(File.Exists(path),
                    $"'{rel}' is on the allow-list but no longer exists. A stale entry silently " +
                    "widens the contract — remove it.");
            }
        }
    }
}
