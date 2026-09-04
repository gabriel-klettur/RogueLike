using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The reusable query buffers every area sweep in this project needs, with ONE reset hook.
    ///
    /// <para>WHY THIS EXISTS RATHER THAN A NOTE IN CLAUDE.md. The note is already there, in as
    /// many words: <i>"a `static readonly` array cache cannot be reset in a way the scanner
    /// recognises: drop the `readonly` and assign a fresh array"</i>. It did not help. Three
    /// separate buffers were written as <c>private static readonly Collider2D[]</c> in a single
    /// batch — the homing acquisition, the damaging aura and the healing totem — and every one
    /// of them failed <c>DomainReloadStaticResetTests</c>, because that declaration is what a
    /// NonAlloc query obviously wants and nobody reads a warning about a trap they do not know
    /// they are walking into.</para>
    ///
    /// <para>So the fix is not more documentation, it is making the wrong shape unavailable.
    /// A new area sweep borrows a buffer from here instead of declaring one, and the reset
    /// that Domain-Reload-OFF demands is already written.</para>
    ///
    /// <para>THE BUFFERS ARE NAMED PER PURPOSE, NOT SHARED GENERICALLY, and that is
    /// deliberate. One buffer handed to every caller would be correct for sequential queries
    /// and silently wrong for nested ones — a sweep whose loop body triggers a second sweep
    /// would have its own results overwritten mid-iteration, with no exception and no log.
    /// Each field below has exactly one owner.</para>
    ///
    /// <para>NOT <c>readonly</c>: <c>DomainReloadStaticResetTests</c> reads this hook's raw IL
    /// and accepts only <c>stsfld</c> or <c>field.Clear()</c>. An array has no instance
    /// <c>Clear()</c>, and <c>System.Array.Clear(buffer, 0, n)</c> passes the field as an
    /// ARGUMENT and counts as no reset at all, so assigning a fresh array is the only form the
    /// scanner recognises — which a readonly field forbids.</para>
    /// </summary>
    public static class PhysicsScratch
    {
        /// <summary>Owner: <c>Projectile.AcquireTarget</c> — homing acquisition.</summary>
        public static Collider2D[] HomingAcquire = new Collider2D[16];

        /// <summary>Owner: <c>AuraController.DamageTick</c> — the damaging aura's sweep.</summary>
        public static Collider2D[] AuraTargets = new Collider2D[24];

        /// <summary>Owner: <c>TotemController.HealTick</c> — the healing totem's sweep.</summary>
        public static Collider2D[] TotemHeal = new Collider2D[16];

        /// <summary>
        /// Domain Reload is OFF, so every one of these would survive a Play-mode restart still
        /// holding <c>Collider2D</c> references to objects destroyed with the previous scene.
        /// Nothing reads a stale slot — <c>OverlapCircleNonAlloc</c> overwrites what it fills
        /// and returns how many are valid — but a destroyed-object reference kept alive across
        /// sessions is precisely what the rule exists to catch, and a few dozen references is
        /// not worth arguing over.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            HomingAcquire = new Collider2D[16];
            AuraTargets = new Collider2D[24];
            TotemHeal = new Collider2D[16];
        }
    }
}
