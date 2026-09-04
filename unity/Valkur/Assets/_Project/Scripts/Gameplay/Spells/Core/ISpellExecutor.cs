using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Context passed to spell executors containing all data needed to execute a spell.
    /// </summary>
    public struct SpellContext
    {
        public SpellDefinition Spell;
        public Transform Caster;
        public Vector2 Direction;
        public LayerMask TargetLayers;
        public GameObject ProjectilePrefab;

        /// <summary>
        /// How long the cast key was held, as a fraction of the spell's own
        /// <c>chargeMaxSeconds</c> (0 = released instantly, 1 = fully charged).
        ///
        /// <para>Meaningless — and left at 0 — for every spell whose
        /// <c>SpellDefinition.IsChargeable</c> is false, which is all but one of them. An
        /// executor must therefore never read this raw: it asks
        /// <c>ChargeMath.Resolve(spell, ctx.ChargeFraction)</c>, which answers a neutral 1
        /// for a spell that does not charge. Reading it directly is how a non-chargeable
        /// spell would silently start dealing its minimum damage.</para>
        /// </summary>
        public float ChargeFraction;
    }

    /// <summary>
    /// Strategy interface for spell execution.
    /// Each spell type (Projectile, Slash, Area, Dash) has its own executor.
    /// </summary>
    public interface ISpellExecutor
    {
        void Execute(SpellContext ctx);
    }
}
