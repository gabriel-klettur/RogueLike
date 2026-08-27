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
    }
}
