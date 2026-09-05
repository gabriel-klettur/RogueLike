using UnityEngine.InputSystem;
using Valkur.Core.Input;

namespace Valkur.UI.PauseMenu
{
    public partial class PauseMenuUI
    {
        // ── Input ────────────────────────────────────────────────────────────
        //
        // THIS USED TO BUILD SEVEN InputActions IN CODE, with literal paths for `p`, the
        // arrows, enter and escape. Every one was a binding no audit over ValkurInputActions
        // could see, the Controls editor could not list or move, and the conflict scanner
        // could not check — and `p` was a straight DUPLICATE of Gameplay/Pause, which the
        // asset already had on the same key. Same defect as InventoryUI's tab and
        // TileEditorInputHandler's eight, in a fourth file.
        //
        // Pause comes from the asset and is rebindable. Navigation, confirm and cancel go
        // through InputCompat — the project's semantic menu helper — which is also why the
        // UI map's Navigate / Submit / Cancel are declared Rebindable: false in
        // InputActionCatalog: a player who moves Submit off Enter can no longer confirm the
        // dialog asking them to confirm it.

        private InputAction PauseAction => InputService.Instance?.Gameplay?.Pause;

        partial void SetupInputActions()
        {
            // Nothing to build. Kept as the seam the builder calls, so the lifecycle reads
            // the same as every other panel's.
            InputService.Initialize();
        }

        private static bool NavUpPressed()    => InputCompat.NavUpPressed();
        private static bool NavDownPressed()  => InputCompat.NavDownPressed();
        private static bool NavLeftPressed()  => InputCompat.NavLeftPressed();
        private static bool NavRightPressed() => InputCompat.NavRightPressed();
        private static bool ConfirmPressed()  => InputCompat.ConfirmPressed();
        private static bool CancelPressed()   => InputCompat.CancelPressed();

        private bool PausePressed() => InputBindingResolver.WasPerformedThisFrame(PauseAction);
    }
}
