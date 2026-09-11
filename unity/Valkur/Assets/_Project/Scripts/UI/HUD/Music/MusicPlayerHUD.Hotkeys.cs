using UnityEngine.InputSystem;
using Valkur.Core.Input;
using Valkur.Core;

namespace Valkur.UI.HUD
{
    public sealed partial class MusicPlayerHUD
    {
        /// <summary>
        /// The music transport from the keyboard: <c>Gameplay/MusicPlayPause</c>,
        /// <c>MusicNext</c> and <c>MusicPrevious</c>. They ship UNBOUND — the player assigns them
        /// in the Controls editor — and they work with the panel closed, because controlling the
        /// music is exactly what a player who closed the panel still wants to do.
        ///
        /// <para>Same gates as the world map's key: not while typing, not while a runtime editor
        /// owns the keyboard, not when the posture mask the Controls editor edits says no. Read
        /// through <see cref="InputBindingResolver"/>, so a rebind moves both the new-system and
        /// the legacy half of the OR-gate.</para>
        /// </summary>
        private void HandleHotkeys()
        {
            var gameplay = InputService.Instance != null ? InputService.Instance.Gameplay : null;
            if (gameplay == null || _audio == null) return;
            if (InputBlocker.IsGameplayBlocked) return;
            if (GameEditorManager.HasInstance && GameEditorManager.Instance.AnyEditorActive) return;

            if (Pressed(gameplay.MusicPlayPause, "MusicPlayPause")) OnPlayPause();
            if (Pressed(gameplay.MusicNext, "MusicNext")) OnNext();
            if (Pressed(gameplay.MusicPrevious, "MusicPrevious")) OnPrevious();
        }

        private static bool Pressed(InputAction action, string name)
        {
            if (action == null) return false;
            var descriptor = InputActionCatalog.Find(InputActionCatalog.MapGameplay, name);
            if (descriptor != null && !InputContextPolicy.IsLive(descriptor)) return false;
            return InputBindingResolver.WasPerformedThisFrame(action);
        }
    }
}
