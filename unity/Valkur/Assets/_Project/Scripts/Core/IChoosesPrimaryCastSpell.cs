namespace Valkur.Core
{
    /// <summary>
    /// Implemented by a runtime editor that wants the player's LEFT CLICK to cast the spell
    /// it currently has selected, instead of the hardcoded primary.
    ///
    /// An editor that opts in gets exactly ONE gesture back and nothing else. Editors that do
    /// not implement <see cref="IAllowsPlayerMovement"/> have gameplay input fully suspended —
    /// <c>PlayerController.Update</c> returns before it ever polls combat — so while such an
    /// editor is open the player cannot move, dash, slash or cast at all. Opting in here
    /// reopens left click alone, still with the pointer-over-UI guard, so clicking the editor's
    /// own panels never fires into the world behind them.
    ///
    /// Note what this is NOT: implementing <see cref="IAllowsPlayerMovement"/> instead would
    /// also work, and is wrong twice over. <c>ReadInput</c> OR-reads raw WASD with no
    /// focused-field guard, so typing in an editor's search box would walk the player; and
    /// <c>WorldDropInteractor</c> gates its own left-click drag on that same interface, so
    /// world drops would fight the cast for the same click.
    ///
    /// Sits beside <see cref="ISuspendsPlayerCombat"/> deliberately, and the two are mutually
    /// exclusive in practice. An editor whose left click is a placement gesture suspends combat
    /// so painting does not also cast; an editor whose left click IS the cast implements this
    /// instead. Implementing both would mean asking for a cast that is then suppressed.
    ///
    /// Nothing changes while the editor is closed: with no active editor the key is null and
    /// every path falls back to the ordinary primary attack.
    /// </summary>
    public interface IChoosesPrimaryCastSpell
    {
        /// <summary>
        /// Spell key left click should cast right now, or null/empty to leave the default
        /// alone. Returning null is the correct answer whenever the editor has no selection,
        /// so an editor opened on an empty grid behaves exactly as it did before.
        /// </summary>
        string PrimaryCastSpellKey { get; }

        /// <summary>
        /// Whether the redirected primary cast should bypass its mana requirement.
        /// This is evaluated for every cast rather than latched, so closing the editor
        /// restores normal mana validation and consumption immediately.
        /// </summary>
        bool PrimaryCastIgnoresManaCost { get; }
    }
}
