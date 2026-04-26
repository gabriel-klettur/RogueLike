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
                case "Continuar":
                    ClosePause();
                    break;
                case "Nueva Partida":
                    ClosePause();
                    SceneTransitionManager.LoadScene("MainGameplay");
                    break;
                case "Guardar partida":
                    if (SaveService.Instance != null) SaveService.Instance.QuickSave();
                    ClosePause();
                    break;
                case "Cargar juego":
                    ShowScreen(PauseScreen.LoadGame);
                    break;
                case "Opciones":
                    ShowScreen(PauseScreen.Options);
                    break;
                case "Salir":
                    // Quicksave before returning to the main menu so the player can
                    // pick "Continuar" on next launch (otherwise their progress is
                    // silently lost when they exit through the pause menu).
                    if (SaveService.Instance != null)
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
                case "Sonido": ShowScreen(PauseScreen.Sounds); break;
                case "Volver": ShowScreen(PauseScreen.Pause);  break;
            }
        }
    }
}
