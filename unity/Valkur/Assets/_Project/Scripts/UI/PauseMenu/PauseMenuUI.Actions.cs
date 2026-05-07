using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;

namespace Valkur.UI.PauseMenu
{
    public partial class PauseMenuUI
    {
        // ── Pause menu execution ─────────────────────────────────────────────

        private void ExecutePause(int idx)
        {
            if (_pauseOptions == null || idx >= _pauseOptions.Length) return;
            switch (_pauseOptions[idx])
            {
                case "Continue":
                    ClosePause();
                    break;
                case "New Game":
                    ClosePause();
                    SceneTransitionManager.LoadScene("MainGameplay");
                    break;
                case "Save Game":
                    if (SaveService.Instance != null) SaveService.Instance.QuickSave();
                    ClosePause();
                    break;
                case "Load Game":
                    ShowScreen(PauseScreen.LoadGame);
                    break;
                case "Options":
                    ShowScreen(PauseScreen.Options);
                    break;
                case "Exit":
                    // Quicksave before returning to the main menu so the player can
                    // pick "Continue" on next launch (otherwise their progress is
                    // silently lost when they exit through the pause menu).
                    //
                    // Gated on IsSessionDirty: if the player did literally nothing
                    // worth saving (no damage, XP, item, level-up, zone change or
                    // manual save) we skip the QuickSave so we don't pollute the
                    // Load Game panel with a fresh Lv.0/Lobby phantom run folder.
                    // The autosave timer enforces the same guard for the same reason.
                    if (SaveService.Instance != null && SaveService.Instance.IsSessionDirty)
                    {
                        try { SaveService.Instance.QuickSave(); }
                        catch (System.Exception ex)
                        { Debug.LogError($"[PauseMenu] Quicksave on exit failed: {ex.Message}"); }
                    }
                    ClosePause();
                    SceneTransitionManager.LoadScene("MainMenu");
                    break;
            }
        }

        private void ExecuteOption(int idx)
        {
            switch (_optOptions[idx])
            {
                case "Inputs": ShowScreen(PauseScreen.Inputs); break;
                case "Sound":  ShowScreen(PauseScreen.Sounds); break;
                case "Back":   ShowScreen(PauseScreen.Pause);  break;
            }
        }
    }
}
