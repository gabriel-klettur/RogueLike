using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The single reader of the stance key, and the only thing in the project that calls
    /// <see cref="PlayerStance.Toggle"/> from a gesture.
    ///
    /// <para>ONE READER, its own component, its own suppression — the shape
    /// <see cref="Valkur.Gameplay.Interaction.PlayerInteractionController"/> already
    /// established for the interact key. Folding it into
    /// <c>PlayerController.Update</c> would bury it among four early returns whose reasons
    /// (stun, editor suspension, spirit form, combat suspension) are all about ACTIONS, and
    /// a stance is not an action: it is what the next action would mean.</para>
    ///
    /// <para>THE KEY IS THE SAFETY VALVE, not a convenience. Nothing auto-switches — being
    /// jumped in Peace means the player cannot answer until they flip it — so a trip to the
    /// HUD chip with the mouse is a death. The chip reports the stance; this changes it.</para>
    ///
    /// <para>The bound action is asked first because the binding is the source of truth and a
    /// player may have rebound it, then the literal Tab as the legacy OR-gate every input read
    /// in this project carries for the 2022.3 event-drop bug. The explicit
    /// <see cref="IsSuppressed"/> is needed on top: <see cref="KeyboardInputManager"/> already
    /// refuses while input is blocked, but the InputAction path does not go through it and
    /// would happily fire while a conversation is up.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStanceToggle : MonoBehaviour
    {
        private void Update()
        {
            if (IsSuppressed()) return;
            if (!WasTogglePressed()) return;
            PlayerStance.Toggle();
        }

        private static bool WasTogglePressed()
        {
            var action = InputService.Instance?.Gameplay?.ToggleStance;
            if (action != null && action.WasPerformedThisFrame()) return true;
            return KeyboardInputManager.WasTabPressedThisFrame();
        }

        /// <summary>
        /// A runtime editor owns the world while it is open and several of them have text
        /// fields, so a stance flip underneath one is never what the author meant. Chat and
        /// the console are covered by the same flag that gates every other gameplay read.
        /// </summary>
        private static bool IsSuppressed()
        {
            if (InputBlocker.IsGameplayBlocked) return true;
            return GameEditorManager.HasInstance && GameEditorManager.Instance.AnyEditorActive;
        }
    }
}
