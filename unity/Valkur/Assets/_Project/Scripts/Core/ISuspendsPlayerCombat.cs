namespace Valkur.Core
{
    /// <summary>
    /// Marker interface for runtime editors that should restrict the player to
    /// <i>movement only</i> while open — no attacks, no dashes, no spell casts.
    ///
    /// Layered on top of <see cref="IAllowsPlayerMovement"/>:
    /// <list type="bullet">
    ///   <item>Editor implements neither marker → player is fully frozen.</item>
    ///   <item>Editor implements <see cref="IAllowsPlayerMovement"/> only → player can move AND fight.</item>
    ///   <item>Editor implements both → player can move but combat is suspended.</item>
    /// </list>
    ///
    /// Use this on placement editors where left-click is the painting gesture
    /// (Tile Editor F8). Without this marker every painted tile would also
    /// fire the player's fireball spell on the same click — see
    /// <c>PlayerController.PollCombatActions</c>.
    /// </summary>
    public interface ISuspendsPlayerCombat
    {
    }
}
