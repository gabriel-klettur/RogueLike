using UnityEngine;
using Valkur.Infrastructure;

namespace Valkur.Core
{
    /// <summary>
    /// Central orchestrator for the gameplay scene.
    /// Equivalent to Python's Game class — coordinates update phases.
    /// Lives in MainGameplay scene.
    /// </summary>
    public class GameDirector : MonoBehaviour
    {
        public static GameDirector Instance { get; private set; }

        [Header("State")]
        [SerializeField] private bool _isPaused;

        public bool IsPaused => _isPaused;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsureSaveService();
            Debug.Log("[GameDirector] Initialized.");
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            Debug.Log($"[GameDirector] Paused: {paused}");
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                Debug.Log("[GameDirector] Application paused — triggering autosave.");
                SaveService.Instance?.Autosave();
            }
        }

        private void OnApplicationQuit()
        {
            Debug.Log("[GameDirector] Application quitting — triggering shutdown save.");
            SaveService.Instance?.Save("shutdown_save");
        }

        private void EnsureSaveService()
        {
            if (SaveService.Instance != null) return;

            var saveGo = new GameObject("SaveService");
            saveGo.AddComponent<SaveService>();
            Debug.Log("[GameDirector] SaveService created.");
        }
    }
}
