namespace Valkur.Data
{
    /// <summary>
    /// The closed set of status effects the game implements
    /// (<c>Gameplay/Combat/StatusEffects/</c>: BurnEffect, PoisonEffect, StunEffect,
    /// FreezeEffect, SlowEffect). Lives in the Data assembly so entity/spell data — which
    /// cannot reference Gameplay — can name a status effect without inventing a second,
    /// string-keyed vocabulary. <c>StatusEffect.Kind</c> (Gameplay) maps each concrete
    /// class back to one of these values; <see cref="EntityStats.statusImmunities"/>
    /// and <c>SpellDefinition.statusApplications</c> both key off it.
    /// </summary>
    public enum StatusEffectKind
    {
        Burn,
        Poison,
        Stun,
        Freeze,
        Slow,

        /// <summary>
        /// Held in place: movement is refused, everything else is not. APPENDED, never
        /// inserted -- <see cref="EntityStats.statusImmunities"/> and
        /// <c>SpellDefinition.statusApplications</c> both serialise this enum as its
        /// integer, so renumbering the values above would repoint every authored immunity
        /// and every authored application at the wrong effect without touching a file.
        ///
        /// Deliberately NOT a flavour of <see cref="Stun"/>. A stun refuses movement AND
        /// attacks (<c>PlayerController</c>, <c>NPCAutoCast</c> and <c>AttackState</c> all
        /// read <c>IsStunned</c>); a root refuses only the feet, which is what makes it a
        /// zoning tool rather than a shorter stun.
        /// </summary>
        Root,
    }
}
