namespace Valkur.Core
{
    /// <summary>
    /// Marker interface for runtime editors that should NOT suspend player
    /// movement / combat while open.
    ///
    /// The default behaviour of <see cref="GameEditorManager.IGameEditor"/> is
    /// for the active editor to gate gameplay input — see
    /// <c>PlayerController.Movement::IsGameplayInputSuspended</c>. Editors that
    /// need the player to keep walking around while editing (e.g. for testing
    /// colliders / spawner placements / tilemap collisions) implement this
    /// marker to opt out of that gate.
    ///
    /// Adding the marker to a new editor is the single, scalable extension
    /// point: no changes to the input gate itself. Removing it restores the
    /// default "open editor → frozen player" behaviour.
    /// </summary>
    public interface IAllowsPlayerMovement
    {
    }
}
