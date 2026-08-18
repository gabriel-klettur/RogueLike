using System;

namespace Valkur.Core
{
    /// <summary>
    /// Marks a static field (or the type holding it) as safe to survive a Play-mode
    /// boundary without a <c>SubsystemRegistration</c> reset.
    ///
    /// Domain Reload is OFF in this project, so statics outlive a Play session. The
    /// project rule is that every mutable static gets a reset hook — see
    /// <c>DomainReloadStaticResetTests</c>, which fails the suite when a new one
    /// appears without one.
    ///
    /// Some statics genuinely do not need the hook, and this attribute is how you say
    /// so at the declaration instead of in a distant allow-list. Legitimate uses:
    ///
    ///   • Immutable lookup data built once from constants (name tables, colour tables,
    ///     tutorial step text) — nothing writes to it after the static initialiser.
    ///   • A cache that re-resolves itself on use, where every read already handles the
    ///     value being null or destroyed (the Unity-null check must be in the accessor,
    ///     not merely "usually true").
    ///   • Scratch buffers reused per call whose contents are always overwritten before
    ///     being read (physics overlap buffers, StringBuilders).
    ///
    /// It is NOT an escape hatch for "this one is annoying to reset". If the field can
    /// hold a reference to a scene object, an event subscriber, or a decision made
    /// during a session, it needs a real hook.
    ///
    /// Always state the reason:
    /// <code>
    /// [SelfHealingStatic("Rebuilt from constants; never mutated after init.")]
    /// private static readonly string[] EquipSlotLabels = { … };
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class,
                    AllowMultiple = false, Inherited = false)]
    public sealed class SelfHealingStaticAttribute : Attribute
    {
        /// <summary>Why this static is safe without a reset hook.</summary>
        public string Reason { get; }

        public SelfHealingStaticAttribute(string reason)
        {
            Reason = reason;
        }
    }
}
