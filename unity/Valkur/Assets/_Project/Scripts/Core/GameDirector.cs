using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Central orchestrator for the gameplay scene.
    /// Equivalent to Python's Game class — coordinates update phases.
    /// Lives in MainGameplay scene.
    /// 
    /// Note: SaveService lives in Valkur.Gameplay (separate asmdef)
    /// and manages its own lifecycle (autosave on pause, shutdown save on quit).
    /// GameDirector does NOT reference Gameplay to avoid circular asmdef deps.
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

            EnsurePerformanceMonitor();
            Debug.Log("[GameDirector] Initialized.");
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            Debug.Log($"[GameDirector] Paused: {paused}");
        }

        private void EnsurePerformanceMonitor()
        {
            if (PerformanceMonitor.Instance != null) return;

            var perfGo = new GameObject("PerformanceMonitor");
            perfGo.AddComponent<PerformanceMonitor>();
            Debug.Log("[GameDirector] PerformanceMonitor created.");
        }
    }
}
