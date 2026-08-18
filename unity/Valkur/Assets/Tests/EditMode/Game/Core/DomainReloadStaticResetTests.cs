using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Core
{
    /// <summary>
    /// The ratchet that keeps Domain-Reload-OFF honest.
    ///
    /// Domain Reload is disabled in this project so entering Play Mode is fast. The
    /// price is that every static field keeps its value from the previous session:
    /// a cached Camera, a registered service, an event subscriber, a decision the
    /// player made last run. The project rule is that each mutable static carries a
    /// <c>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</c> reset — and
    /// before this fixture existed, 158 of them did not.
    ///
    /// Rather than fail the suite on all 158, the known backlog lives in
    /// <c>Assets/Tests/EditMode/Baselines/unreset-statics.txt</c>. This fixture
    /// enforces two directions at once:
    ///
    ///   • Nothing NEW may appear. Add an unreset static and the suite goes red with
    ///     the exact field name, so the cost is paid by whoever introduces it rather
    ///     than by whoever debugs the second-Play MissingReferenceException weeks later.
    ///   • Nothing may LINGER. Fix a static and forget to delete its baseline line and
    ///     the suite also goes red, so the file cannot rot into a fictional to-do list.
    ///
    /// The intended escape hatch is <see cref="SelfHealingStaticAttribute"/> on the
    /// declaration, not an extra line in the baseline.
    /// </summary>
    [TestFixture]
    public class DomainReloadStaticResetTests
    {
        /// <summary>Assemblies whose statics survive into the next Play session.</summary>
        private static readonly string[] ScannedAssemblies =
        {
            "Valkur.Core", "Valkur.Data", "Valkur.Infrastructure",
            "Valkur.Gameplay", "Valkur.UI", "Valkur.UIKit",
        };

        private const string BASELINE_RELATIVE = "Tests/EditMode/Baselines/unreset-statics.txt";

        private const BindingFlags STATIC_DECLARED =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // ── Scanning ─────────────────────────────────────────────────────────────

        /// <summary>One offending field, in the baseline's own "KIND\tType.Field" form.</summary>
        private static SortedSet<string> ScanOffenders()
        {
            var hooked = new HashSet<string>();
            var offenders = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (Array.IndexOf(ScannedAssemblies, asm.GetName().Name) < 0) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (var t in types)
                    if (HasSubsystemRegistrationHook(t))
                        hooked.Add(t.FullName);

                foreach (var t in types)
                {
                    if (!IsScannable(t) || hooked.Contains(t.FullName)) continue;

                    foreach (var f in t.GetFields(STATIC_DECLARED))
                    {
                        if (!IsMutableStatic(f)) continue;
                        if (IsExempt(t, f)) continue;
                        offenders.Add(Classify(f) + "\t" + t.FullName + "." + f.Name);
                    }
                }
            }
            return offenders;
        }

        private static bool HasSubsystemRegistrationHook(Type t)
        {
            var all = STATIC_DECLARED | BindingFlags.Instance;
            foreach (var m in t.GetMethods(all))
                foreach (RuntimeInitializeOnLoadMethodAttribute a in
                         m.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false))
                    if (a.loadType == RuntimeInitializeLoadType.SubsystemRegistration)
                        return true;
            return false;
        }

        /// <summary>
        /// Compiler-generated types hold delegate caches for static lambdas
        /// (<c>&lt;&gt;c.&lt;&gt;9__12_0</c>) and closure frames. They never reference scene
        /// state and there are roughly 290 of them, so scanning them would bury the
        /// real signal.
        /// </summary>
        private static bool IsScannable(Type t)
        {
            if (t.IsEnum || t.IsInterface) return false;
            if (t.IsDefined(typeof(CompilerGeneratedAttribute), false)) return false;
            return t.FullName != null && !t.FullName.Contains("+<");
        }

        private static bool IsMutableStatic(FieldInfo f)
        {
            if (f.IsLiteral) return false;                 // const: baked into callers
            return !f.IsInitOnly || IsMutableCollection(f.FieldType);
        }

        /// <summary>
        /// <c>readonly</c> only freezes the reference. A readonly List or Dictionary is
        /// still mutable, and is exactly where registries accumulate stale entries.
        /// </summary>
        private static bool IsMutableCollection(Type t)
            => typeof(IEnumerable).IsAssignableFrom(t) && t != typeof(string);

        private static bool IsExempt(Type t, FieldInfo f)
            => f.IsDefined(typeof(SelfHealingStaticAttribute), false)
            || t.IsDefined(typeof(SelfHealingStaticAttribute), false);

        private static string Classify(FieldInfo f)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)) return "UNITYOBJ";
            if (typeof(Delegate).IsAssignableFrom(f.FieldType)) return "DELEGATE";
            if (IsMutableCollection(f.FieldType)) return "COLLECTION";
            return "VALUE";
        }

        // ── Baseline ─────────────────────────────────────────────────────────────

        private static string BaselinePath => Path.Combine(Application.dataPath, BASELINE_RELATIVE);

        private static SortedSet<string> ReadBaseline()
        {
            Assert.IsTrue(File.Exists(BaselinePath),
                $"Baseline file missing at {BaselinePath}. It is the record of the known backlog; " +
                "deleting it would silently disable this gate.");

            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var raw in File.ReadAllLines(BaselinePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                set.Add(line);
            }
            return set;
        }

        // ── The gate ─────────────────────────────────────────────────────────────

        [Test]
        public void NoNewStaticEscapesTheSubsystemRegistrationRule()
        {
            var offenders = ScanOffenders();
            var baseline = ReadBaseline();

            var added = offenders.Except(baseline).ToList();

            Assert.IsEmpty(added,
                "New static mutable state with no SubsystemRegistration reset.\n\n" +
                "Domain Reload is OFF, so these keep their value into the next Play session — " +
                "a destroyed object, a stale registration or a leaked subscriber.\n\n" +
                "Fix one of three ways, best first:\n" +
                "  1. Add [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]\n" +
                "     to a private static reset method on the declaring type.\n" +
                "  2. Mark the field [SelfHealingStatic(\"why it is safe\")] if it genuinely is.\n" +
                "  3. Append it to " + BASELINE_RELATIVE + " — a deliberate, reviewable exception.\n\n" +
                "Offending fields:\n  " + string.Join("\n  ", added));
        }

        [Test]
        public void BaselineHasNoStaleEntries()
        {
            var offenders = ScanOffenders();
            var baseline = ReadBaseline();

            var stale = baseline.Except(offenders).ToList();

            Assert.IsEmpty(stale,
                "These baseline entries no longer describe reality — the field was fixed, renamed or " +
                "deleted. Remove the lines from " + BASELINE_RELATIVE + " so the backlog stays honest " +
                "and the count keeps ratcheting down.\n\nStale entries:\n  " + string.Join("\n  ", stale));
        }

        [Test]
        public void ScanFindsTheAssembliesItClaimsToCover()
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet();
            var missing = ScannedAssemblies.Where(a => !loaded.Contains(a)).ToList();

            Assert.IsEmpty(missing,
                "The gate silently covers nothing if an assembly it names is not loaded. " +
                "Missing: " + string.Join(", ", missing));
        }

        [Test]
        public void ScanIgnoresCompilerGeneratedLambdaCaches()
        {
            // Guards the filter itself: without it the offender list is ~450 entries of
            // <>c.<>9__N_M noise and the gate stops being readable, which is how these
            // things quietly get disabled.
            var offenders = ScanOffenders();
            Assert.IsFalse(offenders.Any(o => o.Contains("<>c")),
                "Compiler-generated delegate caches leaked into the scan: " +
                string.Join(", ", offenders.Where(o => o.Contains("<>c")).Take(5)));
        }

        // ── Guards on the classifier, so the baseline's KIND column means something ──

        [Test]
        public void Classify_UnityObjectField_IsTaggedUnityObj()
        {
            var f = typeof(ClassifierProbe).GetField(nameof(ClassifierProbe.Texture), STATIC_DECLARED);
            Assert.AreEqual("UNITYOBJ", Classify(f));
        }

        [Test]
        public void Classify_DelegateField_IsTaggedDelegate()
        {
            var f = typeof(ClassifierProbe).GetField(nameof(ClassifierProbe.Callback), STATIC_DECLARED);
            Assert.AreEqual("DELEGATE", Classify(f));
        }

        [Test]
        public void Classify_StringField_IsNotTreatedAsACollection()
        {
            var f = typeof(ClassifierProbe).GetField(nameof(ClassifierProbe.Name), STATIC_DECLARED);
            Assert.AreEqual("VALUE", Classify(f),
                "string implements IEnumerable; misclassifying it would flood the COLLECTION bucket.");
        }

        [Test]
        public void IsMutableStatic_ReadonlyCollection_CountsAsMutable()
        {
            var f = typeof(ClassifierProbe).GetField(nameof(ClassifierProbe.Items), STATIC_DECLARED);
            Assert.IsTrue(IsMutableStatic(f),
                "readonly freezes the reference, not the contents — registries accumulate there.");
        }

        [Test]
        public void IsMutableStatic_ReadonlyScalar_IsNotMutable()
        {
            var f = typeof(ClassifierProbe).GetField(nameof(ClassifierProbe.Threshold), STATIC_DECLARED);
            Assert.IsFalse(IsMutableStatic(f));
        }

        [Test]
        public void IsExempt_HonoursSelfHealingStaticOnTheField()
        {
            var plain = typeof(ClassifierProbe).GetField(nameof(ClassifierProbe.Name), STATIC_DECLARED);
            var marked = typeof(ClassifierProbe).GetField(nameof(ClassifierProbe.Exempted), STATIC_DECLARED);

            Assert.IsFalse(IsExempt(typeof(ClassifierProbe), plain));
            Assert.IsTrue(IsExempt(typeof(ClassifierProbe), marked));
        }

        /// <summary>Fixture-local fields with known shapes, so the classifier is tested against
        /// something stable instead of against whatever production happens to declare today.</summary>
        private static class ClassifierProbe
        {
            public static Texture2D Texture;
            public static Action Callback;
            public static string Name;
            public static readonly List<int> Items = new List<int>();
            public static readonly float Threshold = 1f;

            [SelfHealingStatic("Probe field for IsExempt coverage.")]
            public static string Exempted;
        }
    }
}
