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
    }
}
