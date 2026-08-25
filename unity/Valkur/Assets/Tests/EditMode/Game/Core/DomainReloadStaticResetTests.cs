using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
    ///
    /// <para>
    /// <b>Exemption is per FIELD, not per TYPE.</b> A type that carries a
    /// SubsystemRegistration hook is still scanned field-by-field; only the fields that
    /// hook demonstrably resets are exempt. Before this, a single reset field (or even
    /// an unrelated event null-out) exempted the type's entire static surface — e.g.
    /// <c>TileEditorTheme</c> nulled one event and, as a side effect, gave a free pass
    /// to the 8 mutable color/size fields the F8 UX panel edits live, none of which
    /// were actually being reset. <see cref="IsResetByAnyHook"/> answers "does this
    /// specific field's value provably come from this hook's call graph" by reading
    /// the hook's raw IL for the field-reset shapes the compiler actually emits:
    /// direct <c>stsfld</c>; <c>ldsflda + initobj</c> (what <c>X = null</c> compiles to
    /// when X's type is a bare unconstrained-by-<c>class</c> generic parameter, e.g.
    /// <c>SingletonMonoBehaviour&lt;T&gt;._instance</c>); and <c>Clear()</c>/<c>Reset()</c>
    /// on the field's own value (the only legal way to "reset" a <c>readonly</c>
    /// collection, since <c>stsfld</c> on those is illegal outside the static
    /// constructor). It also follows calls into other methods on the same type up to
    /// <see cref="MAX_CALL_DEPTH"/> levels, so a hook that delegates to a private
    /// helper (<c>TileEditorTheme.ResetToDefaults()</c> is exactly this shape) still
    /// counts.
    /// </para>
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
            var offenders = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (Array.IndexOf(ScannedAssemblies, asm.GetName().Name) < 0) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (var t in types)
                {
                    if (!IsScannable(t)) continue;

                    var hooks = GetSubsystemRegistrationHooks(t);

                    foreach (var f in t.GetFields(STATIC_DECLARED))
                    {
                        if (!IsMutableStatic(f)) continue;
                        if (IsExempt(t, f)) continue;
                        if (IsResetByAnyHook(t, f, hooks)) continue;
                        offenders.Add(Classify(f) + "\t" + t.FullName + "." + f.Name);
                    }
                }
            }
            return offenders;
        }

        /// <summary>Every method on <paramref name="t"/> tagged
        /// <c>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</c>, or null if none.</summary>
        private static List<MethodInfo> GetSubsystemRegistrationHooks(Type t)
        {
            List<MethodInfo> hooks = null;
            foreach (var m in t.GetMethods(STATIC_DECLARED | BindingFlags.Instance))
                foreach (RuntimeInitializeOnLoadMethodAttribute a in
                         m.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false))
                    if (a.loadType == RuntimeInitializeLoadType.SubsystemRegistration)
                        (hooks ??= new List<MethodInfo>()).Add(m);
            return hooks;
        }

        // ── Field-level hook detector (raw-IL) ──────────────────────────────────

        /// <summary>
        /// How many same-type method calls to follow from a hook before giving up.
        /// A hook that delegates through more indirection than this is treated as
        /// "not proven" (fails toward re-exposing the field, never toward silently
        /// exempting it) rather than risk an unbounded walk.
        /// </summary>
        private const int MAX_CALL_DEPTH = 4;

        private static readonly Dictionary<short, OpCode> OpCodeTable = BuildOpCodeTable();

        private static Dictionary<short, OpCode> BuildOpCodeTable()
        {
            var dict = new Dictionary<short, OpCode>();
            foreach (var fi in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (fi.FieldType != typeof(OpCode)) continue;
                var opcode = (OpCode)fi.GetValue(null);
                dict[opcode.Value] = opcode;
            }
            return dict;
        }

        /// <summary>True if any of <paramref name="hooks"/> demonstrably resets <paramref name="field"/>.</summary>
        private static bool IsResetByAnyHook(Type owner, FieldInfo field, List<MethodInfo> hooks)
        {
            if (hooks == null) return false;
            foreach (var h in hooks)
                if (MethodAssignsField(h, field, owner, new HashSet<MethodBase>(), 0))
                    return true;
            return false;
        }

        private static bool MethodAssignsField(MethodBase method, FieldInfo field, Type owner,
            HashSet<MethodBase> visited, int depth)
        {
            if (depth > MAX_CALL_DEPTH || !visited.Add(method)) return false;

            var instrs = DecodeInstructions(method, owner);
            if (instrs == null) return false; // unparsable IL fails closed: not proven reset

            bool fieldTouched = false;
            bool sawMatchingClearOrReset = false;

            for (int idx = 0; idx < instrs.Count; idx++)
            {
                var (op, resolved) = instrs[idx];

                if (op == OpCodes.Stsfld && resolved is FieldInfo fS && fS == field)
                    return true; // direct reassignment is unambiguous on its own

                if (op == OpCodes.Ldsflda && resolved is FieldInfo fA && fA == field &&
                    idx + 1 < instrs.Count && instrs[idx + 1].op == OpCodes.Initobj)
                {
                    // `X = null` / `X = default` on a field whose type is a bare generic
                    // parameter (no `class` constraint spelled out, even if implied by a
                    // base-type constraint) compiles to `ldsflda X; initobj T`, not
                    // `stsfld` -- SingletonMonoBehaviour<T>._instance is exactly this shape.
                    return true;
                }

                if ((op == OpCodes.Ldsfld || op == OpCodes.Ldsflda) && resolved is FieldInfo fL && fL == field)
                    fieldTouched = true;

                if ((op == OpCodes.Call || op == OpCodes.Callvirt) &&
                    resolved is MethodBase mb && mb.GetParameters().Length == 0 &&
                    (mb.Name == "Clear" || mb.Name == "Reset") &&
                    mb.DeclaringType == field.FieldType)
                    sawMatchingClearOrReset = true;

                if ((op == OpCodes.Call || op == OpCodes.Callvirt) &&
                    resolved is MethodBase called && called.DeclaringType == owner &&
                    MethodAssignsField(called, field, owner, visited, depth + 1))
                    return true;
            }

            // A readonly collection can never be reassigned (stsfld is illegal outside the
            // declaring type's static ctor), so the ONLY legal way to "reset" one is to
            // mutate it in place -- `_cache.Clear()` / `_cache?.Clear()`. The null-conditional
            // form in particular compiles to a branchy, non-adjacent "diamond" (dup/brtrue/
            // pop/br .. out-of-line call .. two separate `ret`s) that makes position-based
            // adjacency unreliable, so this checks two whole-method facts instead: the
            // field's value was loaded somewhere, AND a zero-arg Clear()/Reset() on that
            // EXACT field type was called somewhere. Exact-type equality is what keeps this
            // from conflating two different collections reset in the same hook.
            return fieldTouched && sawMatchingClearOrReset;
        }

        /// <summary>
        /// Decodes <paramref name="method"/>'s IL into (opcode, resolved-token) pairs.
        /// Resolves only the operand kinds this detector cares about (field/method tokens
        /// on <c>stsfld</c>/<c>ldsfld</c>/<c>ldsflda</c>/<c>call</c>/<c>callvirt</c>) — every
        /// other opcode is still walked (to keep the byte offset aligned) but its operand is
        /// discarded. Returns null on anything unparsable so callers fail closed.
        /// </summary>
        private static List<(OpCode op, object resolved)> DecodeInstructions(MethodBase method, Type owner)
        {
            MethodBody body;
            try { body = method.GetMethodBody(); } catch { return null; }
            if (body == null) return null;

            byte[] il;
            try { il = body.GetILAsByteArray(); } catch { return null; }
            if (il == null) return null;

            var module = method.Module;
            // Needed to resolve tokens inside an OPEN generic type's own methods (e.g.
            // SingletonMonoBehaviour<T> itself, not a closed instantiation of it).
            Type[] typeArgs = owner.IsGenericType ? owner.GetGenericArguments() : null;
            Type[] methodArgs = method is MethodInfo mi && mi.IsGenericMethod ? mi.GetGenericArguments() : null;

            var result = new List<(OpCode, object)>();
            int i = 0;
            while (i < il.Length)
            {
                ushort code16 = il[i];
                if (code16 == 0xFE)
                {
                    if (i + 1 >= il.Length) return null;
                    code16 = (ushort)(0xFE00 | il[i + 1]);
                    i += 2;
                }
                else i += 1;

                if (!OpCodeTable.TryGetValue((short)code16, out var op)) return null;

                int operandStart = i;
                int operandSize;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                    case OperandType.InlinePhi: operandSize = 0; break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: operandSize = 1; break;
                    case OperandType.InlineVar: operandSize = 2; break;
                    case OperandType.InlineI:
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineSig:
                    case OperandType.InlineString:
                    case OperandType.InlineTok:
                    case OperandType.InlineType:
                    case OperandType.ShortInlineR: operandSize = 4; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: operandSize = 8; break;
                    case OperandType.InlineSwitch:
                    {
                        if (operandStart + 4 > il.Length) return null;
                        int count = BitConverter.ToInt32(il, operandStart);
                        operandSize = 4 + count * 4;
                        break;
                    }
                    default: return null; // unreachable for the opcode set that exists today
                }
                if (operandStart + operandSize > il.Length) return null;

                object resolved = null;
                if (operandSize == 4 &&
                    (op == OpCodes.Stsfld || op == OpCodes.Ldsfld || op == OpCodes.Ldsflda ||
                     op == OpCodes.Call || op == OpCodes.Callvirt))
                {
                    int token = BitConverter.ToInt32(il, operandStart);
                    try
                    {
                        resolved = (op == OpCodes.Stsfld || op == OpCodes.Ldsfld || op == OpCodes.Ldsflda)
                            ? (object)module.ResolveField(token, typeArgs, methodArgs)
                            : (object)module.ResolveMethod(token, typeArgs, methodArgs);
                    }
                    catch { resolved = null; } // unresolved token; irrelevant to this pass either way
                }

                result.Add((op, resolved));
                i = operandStart + operandSize;
            }
            return result;
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

        // ── Guards on the field-level hook detector, so "the hook resets it" means it really does ──

        [Test]
        public void IsResetByAnyHook_FieldDirectlyAssignedInHookBody_IsDetected()
        {
            var t = typeof(HookProbe);
            var hooks = GetSubsystemRegistrationHooks(t);
            var f = t.GetField(nameof(HookProbe.DirectlyReset), STATIC_DECLARED);

            Assert.IsTrue(IsResetByAnyHook(t, f, hooks));
        }

        [Test]
        public void IsResetByAnyHook_FieldNeverTouchedByHook_IsNotDetected()
        {
            // The exact regression this whole change exists to catch: a hook that resets
            // SOME of a type's statics must not give a free pass to the rest.
            var t = typeof(HookProbe);
            var hooks = GetSubsystemRegistrationHooks(t);
            var f = t.GetField(nameof(HookProbe.NeverTouched), STATIC_DECLARED);

            Assert.IsFalse(IsResetByAnyHook(t, f, hooks));
        }

        [Test]
        public void IsResetByAnyHook_ReadonlyCollectionClearedThroughPrivateHelper_IsDetected()
        {
            // Mirrors TileEditorTheme.ResetStaticStateOnPlayModeEnter -> ResetToDefaults():
            // the hook itself doesn't touch the field, a method it calls does.
            var t = typeof(HookProbe);
            var hooks = GetSubsystemRegistrationHooks(t);
            var f = t.GetField(nameof(HookProbe.ClearedViaHelper), STATIC_DECLARED);

            Assert.IsTrue(IsResetByAnyHook(t, f, hooks));
        }

        [Test]
        public void IsResetByAnyHook_ReadonlyCollectionNeverCleared_IsNotDetected()
        {
            var t = typeof(HookProbe);
            var hooks = GetSubsystemRegistrationHooks(t);
            var f = t.GetField(nameof(HookProbe.NeverCleared), STATIC_DECLARED);

            Assert.IsFalse(IsResetByAnyHook(t, f, hooks));
        }

        [Test]
        public void IsResetByAnyHook_NullConditionalClearCall_IsDetected()
        {
            // `_field?.Clear();` compiles to a branchy dup/brtrue/pop/br "diamond" with the
            // actual Clear() call placed out of line -- not adjacent to the ldsfld the way a
            // plain `_field.Clear();` is. This is the exact shape that hid
            // MinimapManager._dots / ._markers from a naive proximity check.
            var t = typeof(HookProbe);
            var hooks = GetSubsystemRegistrationHooks(t);
            var f = t.GetField(nameof(HookProbe.ClearedViaNullConditional), STATIC_DECLARED);

            Assert.IsTrue(IsResetByAnyHook(t, f, hooks));
        }

        [Test]
        public void IsResetByAnyHook_GenericFieldAssignedNull_UsesInitobjPatternAndIsDetected()
        {
            // `Instance = null;` where Instance's type is a bare generic parameter (T
            // constrained only by a self-referential base type, no explicit `class`
            // keyword -- the exact shape of SingletonMonoBehaviour<T>) compiles to
            // `ldsflda Instance; initobj T`, never `stsfld`.
            var t = typeof(GenericHookProbe<>);
            var hooks = GetSubsystemRegistrationHooks(t);
            var f = t.GetField("Instance", STATIC_DECLARED);

            Assert.IsTrue(IsResetByAnyHook(t, f, hooks));
        }

        [Test]
        public void IsResetByAnyHook_HookOnDifferentUnrelatedType_DoesNotExemptThisType()
        {
            // A type with NO hook of its own must never borrow one from elsewhere --
            // guards the "type-level exemption crept back in" regression directly.
            var probeHooks = GetSubsystemRegistrationHooks(typeof(HookProbe));
            var unrelatedField = typeof(UnhookedProbe).GetField(
                nameof(UnhookedProbe.NeverResetAnywhere), STATIC_DECLARED);

            Assert.IsFalse(IsResetByAnyHook(typeof(UnhookedProbe), unrelatedField, probeHooks));
        }

        /// <summary>Fixture with a hook that resets some of its statics and not others,
        /// through both direct assignment and a call-graph indirection, so the detector's
        /// true/false outcomes can be pinned down independently of production code.</summary>
        private static class HookProbe
        {
            public static int DirectlyReset;
            public static int NeverTouched;
            public static readonly List<int> ClearedViaHelper = new List<int>();
            public static readonly List<int> NeverCleared = new List<int>();
            public static readonly List<string> ClearedViaNullConditional = new List<string>();

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetOnPlayModeEnter()
            {
                DirectlyReset = 0;
                ClearedViaNullConditional?.Clear();
                ResetCollections();
            }

            private static void ResetCollections()
            {
                ClearedViaHelper.Clear();
            }
        }

        private abstract class GenericHookProbe<T> where T : GenericHookProbe<T>
        {
            protected static T Instance;

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetOnPlayModeEnter()
            {
                Instance = null;
            }
        }

        private sealed class GenericHookProbeImpl : GenericHookProbe<GenericHookProbeImpl> { }

        private static class UnhookedProbe
        {
            public static int NeverResetAnywhere;
        }
    }
}
