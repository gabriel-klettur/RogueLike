namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Implemented by a persistent spell effect that owns its own death animation.
    ///
    /// <para>WHY THIS EXISTS. A free-standing effect has five exit paths — its own timer,
    /// eviction by <c>maxInstances</c>, a zone change, its caster dying, and scene unload —
    /// and only the FIRST of them runs any of the effect's code before the GameObject is
    /// gone. The other four go through
    /// <see cref="SpellEffectRegistry"/>'s <c>Object.Destroy</c>, so any fade the effect
    /// implements on its own timeline is simply skipped.</para>
    ///
    /// <para>That is not an edge case. The arcane flame runs 5 s on a 2 s cooldown, so in
    /// normal play EVERY instance but the last is evicted by the next cast — a hard cut is
    /// what the player actually sees, roughly every two seconds.</para>
    ///
    /// <para>The registry drops the handle BEFORE calling through here, so an effect that
    /// takes ownership stops counting against <c>maxInstances</c> immediately and the
    /// recast that evicted it is never refused while it fades.</para>
    /// </summary>
    public interface ISpellEffectDissipates
    {
        /// <summary>
        /// Begin a compressed close over <paramref name="seconds"/> and take responsibility
        /// for destroying the GameObject.
        /// </summary>
        /// <returns>
        /// <c>true</c> if ownership was taken — the caller must NOT destroy the object.
        /// <c>false</c> to decline, in which case the caller destroys it immediately as
        /// before. Declining is the correct answer when the component is already inactive
        /// or is itself mid-teardown.
        /// </returns>
        bool BeginDissipate(float seconds);
    }
}
