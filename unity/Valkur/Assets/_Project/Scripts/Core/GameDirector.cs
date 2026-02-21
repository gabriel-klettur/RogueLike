using UnityEngine;

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
                // SaveService.Instance?.Save(); // TODO: wire up when SaveService exists
            }
        }

        private void OnApplicationQuit()
        {
            Debug.Log("[GameDirector] Application quitting — triggering shutdown save.");
            // SaveService.Instance?.Save(); // TODO: wire up when SaveService exists
        }
    }
}
