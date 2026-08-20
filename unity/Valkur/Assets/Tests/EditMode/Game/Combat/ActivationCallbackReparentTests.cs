using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Unity refuses to reparent a GameObject while its parent is in the middle of being
    /// activated or deactivated. It does not throw — it logs
    ///
    ///     Cannot set the parent of the GameObject 'X' while activating or
    ///     deactivating the parent GameObject 'Y'
    ///
    /// and the reparent simply does not happen. That combination is what makes it worth a
    /// test: the object keeps working, the hierarchy is quietly wrong, and the only symptom
    /// is console noise that is easy to read as harmless.
    ///
    /// It shipped in <c>ParticleProjectileVisual</c>, whose <c>OnEnable</c> attached the
    /// spell's four trail emitters to the projectile. Every pooled projectile comes out of
    /// the pool via <c>SetActive(true)</c>, so every cast produced four errors and four
    /// emitters left behind at the pool's origin instead of following the shot.
    ///
    /// This is a source scan rather than a runtime test because reproducing it needs a
    /// pooled object mid-activation, and the fix is structural: do the attach a frame later,
    /// and detach from the impact callback, which runs before the pool deactivates anything.
    /// </summary>
    [TestFixture]
    public class ActivationCallbackReparentTests
    {
        private static string ScriptsRoot =>
            Path.Combine(Application.dataPath, "_Project", "Scripts");

        /// <summary>
        /// Comments are stripped before scanning. Without that, a file that DOCUMENTS the
        /// trap fails the test that guards it — which is exactly how this fixture first
        /// went red: the fix's own explanatory comment named the call it had removed.
        /// </summary>
        private static string StripComments(string src)
        {
            src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return string.Join("\n", src.Split('\n').Select(StripLineComment));
        }

        private static string StripLineComment(string line)
        {
            int i = line.IndexOf("//", System.StringComparison.Ordinal);
            return i < 0 ? line : line.Substring(0, i);
        }

        private static string Source(string path)
            => StripComments(File.ReadAllText(path));

        /// <summary>
        /// The body of a method, found by matching braces from its signature. Good enough
        /// for a lint: C# lets braces appear inside strings and chars, but no Unity
        /// lifecycle callback in this project contains either.
        /// </summary>
        private static string MethodBody(string source, int signatureEnd)
        {
            int open = source.IndexOf('{', signatureEnd);
            if (open < 0) return string.Empty;

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0)
                    return source.Substring(open, i - open);
            }
            return source.Substring(open);
        }

        [Test]
        public void NoReparentInsideAnActivationCallback()
        {
            var offenders = new List<string>();
            var callback = new Regex(@"void\s+(OnEnable|OnDisable)\s*\(\s*\)");

            string[] files = Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories);
            Assert.IsNotEmpty(files, "Found no sources to scan — the scan is broken, not the code.");

            foreach (string file in files)
            {
                string source = Source(file);
                foreach (Match m in callback.Matches(source))
                {
                    string body = MethodBody(source, m.Index + m.Length);
                    if (body.Contains("SetParent("))
                        offenders.Add($"{Path.GetFileName(file)}: SetParent inside {m.Groups[1].Value}");
                }
            }

            Assert.IsEmpty(offenders,
                "Unity silently refuses the reparent and logs one error per call. Defer the " +
                "attach to the next LateUpdate; detach from a callback that runs before the " +
                "object is deactivated.\n\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void TheProjectileTrailAttachesOutsideOnEnable()
        {
            string path = Path.Combine(ScriptsRoot, "Gameplay", "Spells", "Visuals",
                                       "ParticleProjectileVisual.cs");
            Assert.IsTrue(File.Exists(path), "ParticleProjectileVisual.cs moved or was renamed.");
            string source = Source(path);

            var enable = Regex.Match(source, @"void\s+OnEnable\s*\(\s*\)");
            Assert.IsTrue(enable.Success, "OnEnable is gone.");
            string body = MethodBody(source, enable.Index + enable.Length);

            Assert.IsFalse(body.Contains("StartTrail()"),
                "StartTrail parents the emitters, so calling it from OnEnable is exactly the " +
                "bug. It must be deferred a frame.");
            Assert.IsTrue(body.Contains("_trailStartPending"),
                "OnEnable must arm the deferred start instead.");

            Assert.IsTrue(Regex.IsMatch(source, @"void\s+LateUpdate\s*\(\s*\)"),
                "Something has to consume _trailStartPending or the trail never starts at all.");
        }

        [Test]
        public void ADeferredTrailIsAbandonedIfTheShotAlreadyLanded()
        {
            // A projectile can be spawned and expire in the same frame — a point-blank cast
            // into a wall. Without this guard the deferred start runs after OnImpact has
            // already stopped the trail, leaving emitters running on a projectile that no
            // longer exists.
            string path = Path.Combine(ScriptsRoot, "Gameplay", "Spells", "Visuals",
                                       "ParticleProjectileVisual.cs");
            string source = Source(path);

            var late = Regex.Match(source, @"void\s+LateUpdate\s*\(\s*\)");
            Assert.IsTrue(late.Success);
            string body = MethodBody(source, late.Index + late.Length);

            Assert.IsTrue(body.Contains("_impacted"),
                "The deferred start must bail out when the projectile already impacted.");
        }
    }
}
