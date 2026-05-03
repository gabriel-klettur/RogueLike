using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Save;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Permadeath enforcer. When <see cref="GameSettings.permadeath"/> is on
    /// and the player dies, deletes the active autosave so the run cannot
    /// be reloaded. Settings flag stays at false by default — this is an
    /// opt-in hardcore mode, not the standard play.
    ///
    /// The actual scene transition (back to main menu, "you died" splash)
    /// is the responsibility of the existing OnPlayerDied listeners
    /// (DeathDropSystem, audio, UI). This system's job is just save cleanup
    /// so a dead run can never be revived from disk after the fact.
    /// </summary>
    public class PermadeathSaveCleanupSystem : MonoBehaviour
    {
        private void OnEnable()
        {
            GameEvents.OnPlayerDied += OnPlayerDied;
        }

        private void OnDisable()
        {
            GameEvents.OnPlayerDied -= OnPlayerDied;
        }

        private void OnPlayerDied()
        {
            if (!GameSettings.Instance.permadeath) return;

            string runId = SaveService.Instance != null ? SaveService.Instance.RunId : null;
            if (string.IsNullOrEmpty(runId))
            {
                Debug.LogWarning("[Permadeath] OnPlayerDied fired but no active runId; nothing to delete.");
                return;
            }

            string path = SaveFileManager.GetAutosavePath(runId);
            bool deleted = SaveFileManager.DeleteSave(path);
            if (deleted)
                Debug.Log($"[Permadeath] Deleted autosave for run '{runId}' — this run is over.");
            else
                Debug.LogWarning($"[Permadeath] DeleteSave returned false for '{path}' " +
                                 "(file already gone or IO error).");
        }
    }
}
