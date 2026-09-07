using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Core.Services;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The single reader of the pause key.
    ///
    /// <para>WHY IT EXISTS AT ALL. <c>Gameplay/Pause</c> has been bound to <c>p</c> for the
    /// life of the asset and had ZERO readers in production — grepped, and the Controls editor
    /// is what made it visible: a panel that lists every action and the key it is on cannot
    /// list a key that does nothing. It is the same authored-and-inert shape this project has
    /// found in <c>animation_map.json</c>, the FSM's <c>Actions</c> block and four of the six
    /// casting flags, and the fix is the same one: give the data a reader, or delete it.
    /// Deleting it was the worse option — the pause menu really is reachable only through
    /// Escape then a menu entry, and every game in this genre has a pause key.</para>
    ///
    /// <para>ONE READER, its own component, its own suppression — the shape
    /// <see cref="PlayerStanceToggle"/> and
    /// <c>PlayerInteractionController</c> already established. It is a SCENE component rather
    /// than a player one because pausing is not something the character does: it must work
    /// while the player is dead, mid-transition, or not yet spawned.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PauseHotkeyReader : MonoBehaviour
    {
        private static InputActionDescriptor Descriptor =>
            InputActionCatalog.Find(InputActionCatalog.MapGameplay, "Pause");

        private void Update()
        {
            if (IsSuppressed()) return;

            // The context mask is consulted, so the Controls editor's posture chips on this
            // row mean something. Pause carries no damage, so a player may narrow it to one
            // posture or silence it entirely and get it back from the same row.
            if (!InputContextPolicy.IsLive(Descriptor)) return;

            if (!InputBindingResolver.WasPerformedThisFrame(InputService.Instance?.Gameplay?.Pause))
                return;

            ServiceLocator.Get<IPauseMenuService>()?.OpenPause();
        }

        /// <summary>
        /// A runtime editor owns the world while it is open and several of them have text
        /// fields; chat and the console are covered by the same flag that gates every other
        /// gameplay read. The pause menu itself is checked last because it owns Escape for its
        /// own navigation, and a second key that reopened it from inside would fight that.
        /// </summary>
        private static bool IsSuppressed()
        {
            if (InputBlocker.IsGameplayBlocked) return true;
            if (GameEditorManager.HasInstance && GameEditorManager.Instance.AnyEditorActive) return true;

            var pause = ServiceLocator.Get<IPauseMenuService>();
            return pause == null || pause.IsOpen;
        }
    }
}
