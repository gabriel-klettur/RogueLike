using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Valkur.Tests.Support
{
    /// <summary>
    /// The one way a test reaches a private member. It used to be written per file: 127 private
    /// copies of <c>GetField</c> / <c>SetField</c> / <c>Invoke</c> under seven different names,
    /// each with its own idea of which <see cref="BindingFlags"/> to pass. Most of them looked on
    /// the declaring type only, so the same call worked on a field of the class and failed with a
    /// <see cref="NullReferenceException"/> on a field of its base, in a line that says nothing
    /// about reflection.
    ///
    /// <para><b>Behaviour.</b> Lookups walk the type and every base type, instance and static,
    /// public and non-public. A member that does not exist throws
    /// <see cref="MissingMemberException"/> naming the type and the member, so a renamed field is
    /// a readable failure instead of an NRE three lines later.</para>
    ///
    /// <para><b>Exceptions are NOT unwrapped.</b> <see cref="Invoke(object, string, object[])"/>
    /// lets <see cref="TargetInvocationException"/> through, exactly as
    /// <see cref="MethodBase.Invoke(object, object[])"/> does, because that is what every helper
    /// this replaced did and some tests catch it. Use <see cref="InvokeUnwrapped"/> when the
    /// assertion is about the exception the method itself threw.</para>
    ///
    /// <para><b>Prefer not needing this.</b> <c>InternalsVisibleTo</c> is granted to every test
    /// assembly that can see the production assembly, so an <c>internal</c> member is reachable
    /// with a compile-time check. Reflection is for <c>private</c> state, and a test that needs a
    /// lot of it is usually a sign the seam is missing.</para>
    /// </summary>
    public static class TestReflection
    {
        private const BindingFlags Everything =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // ── Fields ─────────────────────────────────────────────────────────────────────────

        /// <summary>The field <paramref name="name"/> on <paramref name="type"/> or any base type.</summary>
        public static FieldInfo FindField(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, Everything);
                if (f != null) return f;
            }
            throw new MissingMemberException($"{type.FullName} has no field '{name}' (searched its base types too).");
        }

        public static object GetField(object target, string name)
            => FindField(TypeOf(target), name).GetValue(target);

        public static T GetField<T>(object target, string name)
            => (T)GetField(target, name);

        public static void SetField(object target, string name, object value)
            => FindField(TypeOf(target), name).SetValue(target, value);

        public static object GetStaticField(Type type, string name)
            => FindField(type, name).GetValue(null);

        public static T GetStaticField<T>(Type type, string name)
            => (T)GetStaticField(type, name);

        public static void SetStaticField(Type type, string name, object value)
            => FindField(type, name).SetValue(null, value);

        // ── Properties ─────────────────────────────────────────────────────────────────────

        public static PropertyInfo FindProperty(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, Everything);
                if (p != null) return p;
            }
            throw new MissingMemberException($"{type.FullName} has no property '{name}' (searched its base types too).");
        }

        public static T GetProperty<T>(object target, string name)
            => (T)FindProperty(TypeOf(target), name).GetValue(target);

        public static void SetProperty(object target, string name, object value)
            => FindProperty(TypeOf(target), name).SetValue(target, value);

        // ── Methods ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The method <paramref name="name"/> that accepts <paramref name="args"/>. Overloads are
        /// told apart by argument count and assignability; a <c>null</c> argument matches any
        /// reference type. Ambiguity after that is an error rather than a silent first pick.
        /// </summary>
        public static MethodInfo FindMethod(Type type, string name, object[] args)
        {
            args ??= Array.Empty<object>();
            var candidates = new List<MethodInfo>();
            for (var t = type; t != null; t = t.BaseType)
                candidates.AddRange(t.GetMethods(Everything).Where(m => m.Name == name));
            if (candidates.Count == 0)
                throw new MissingMemberException($"{type.FullName} has no method '{name}' (searched its base types too).");

            var matching = candidates.Where(m => Accepts(m, args)).ToList();
            if (matching.Count == 1) return matching[0];
            if (matching.Count == 0)
                throw new MissingMethodException($"{type.FullName}.{name} has no overload accepting {args.Length} argument(s) of those types.");
            // the most-derived declaration wins over a hidden base member of the same shape
            var mostDerived = matching.Where(m => m.DeclaringType == matching[0].DeclaringType).ToList();
            if (mostDerived.Count == 1) return mostDerived[0];
            throw new AmbiguousMatchException($"{type.FullName}.{name} has {mostDerived.Count} overloads accepting those arguments; call FindMethod with an exact type.");
        }

        public static object Invoke(object target, string name, params object[] args)
            => FindMethod(TypeOf(target), name, args).Invoke(target, args);

        public static T Invoke<T>(object target, string name, params object[] args)
            => (T)Invoke(target, name, args);

        public static object InvokeStatic(Type type, string name, params object[] args)
            => FindMethod(type, name, args).Invoke(null, args);

        /// <summary>Like <see cref="Invoke(object, string, object[])"/>, but rethrows the method's own exception.</summary>
        public static object InvokeUnwrapped(object target, string name, params object[] args)
        {
            try { return Invoke(target, name, args); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        private static Type TypeOf(object target)
            => target?.GetType() ?? throw new ArgumentNullException(nameof(target), "Reflection target is null.");

        private static bool Accepts(MethodInfo m, object[] args)
        {
            var ps = m.GetParameters();
            var hasParams = ps.Length > 0 && ps[ps.Length - 1].IsDefined(typeof(ParamArrayAttribute), false);
            if (ps.Length != args.Length && !(hasParams && args.Length >= ps.Length - 1))
            {
                // optional trailing parameters
                if (args.Length > ps.Length || ps.Skip(args.Length).Any(p => !p.IsOptional)) return false;
            }
            for (int i = 0; i < Math.Min(ps.Length, args.Length); i++)
            {
                var pt = ps[i].ParameterType;
                if (pt.IsByRef) pt = pt.GetElementType();
                if (args[i] == null)
                {
                    if (pt.IsValueType && Nullable.GetUnderlyingType(pt) == null) return false;
                }
                else if (!pt.IsInstanceOfType(args[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
