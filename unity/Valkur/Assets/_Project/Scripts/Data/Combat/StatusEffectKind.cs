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

        /// <summary>
        /// Takes MORE damage from every source while it lasts. The magnitude is the extra
        /// fraction (0.30 = +30% incoming). APPENDED for the reason <see cref="Root"/>
        /// records: both readers of this enum serialise it as an integer.
        ///
        /// It is a status effect rather than a stat layer on purpose. The layered store
        /// (<c>PlayerStats</c>, seven layers) hangs off the PLAYER; an NPC has
        /// <c>EntityStats</c> and no equivalent, and building a second composition rule on
        /// the NPC side is how a project ends up with two that disagree. As a status effect
        /// it inherits duration, refresh, immunity and the tint layer for free.
        /// </summary>
        Vulnerable,

        /// <summary>
        /// Marked for raising: if the bearer dies while this holds, something answers. Carries
        /// no magnitude and no combat effect of its own — it is a claim on the DEATH, which is
        /// why it is a status and not a component. See <c>ThrallMarkEffect</c>.
        ///
        /// Being a status is what makes the mechanic cheap: the mark is carried by a LIVING
        /// target, so at the moment of death the definition, level, position and facing are
        /// all still on a live GameObject. A corpse-raising spell would instead need a death
        /// registry, and would have to survive <c>deathDisappearTime</c> despawning the body.
        /// </summary>
        Marked,
    }
}
