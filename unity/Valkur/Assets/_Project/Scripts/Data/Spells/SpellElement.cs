namespace Valkur.Data
{
    /// <summary>
    /// Element preset — drives palette, trail behaviour, ember type and impact style for
    /// <c>ElementalProjectileVisual</c> (Gameplay assembly), AND is the damage-typing key
    /// consulted by <c>Health</c>'s elemental resistance table
    /// (<see cref="EntityStats.resistances"/>) and by <c>SpellDefinition.element</c> (parsed
    /// via <c>Enum.TryParse</c> — see <c>ProjectileExecutor.ResolveElement</c>).
    ///
    /// Lives in the Data assembly — not Gameplay, where it was originally authored — because
    /// <see cref="EntityStats"/> needs it for the resistance table and Data cannot reference
    /// Gameplay (see the assembly dependency rule in CLAUDE.md). Every Gameplay/Spells file
    /// that used to resolve this name for free (same namespace) now needs
    /// <c>using Valkur.Data;</c>, matching how those files already pull in
    /// <see cref="SpellDefinition"/> and <see cref="SpellType"/>.
    /// </summary>
    public enum SpellElement
    {
        Dark,
        Ice,
        Light,
        Lightning,
        Boomerang,
        Arcane,
        Fire,

        /// <summary>
        /// Verdant Rites. Appended last, and safe to append because <c>SpellDefinition.element</c>
        /// is a STRING parsed by name — no shipped asset stores this enum as an integer, so
        /// adding a member repoints nothing.
        ///
        /// <para>It exists because the five Verdant spells (<c>thorn_burst</c>, <c>entangle</c>,
        /// <c>barkskin</c>, <c>spore_cloud</c>, <c>summon_wolf</c>) have all authored
        /// <c>element: Nature</c> since they shipped, against an enum that had no such member —
        /// so <c>Enum.TryParse</c> failed on every one of them, they fell through to the legacy
        /// key switch, and it returned null. The whole school then drew from whatever palette
        /// each caller used for "no element", which is why a wolf summoned by a green spell
        /// arrived violet.</para>
        /// </summary>
        Nature,
    }
}
