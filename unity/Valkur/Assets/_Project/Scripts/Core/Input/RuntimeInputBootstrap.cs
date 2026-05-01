using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Brings the input pipeline up before any scene loads:
    ///   1. Adds Mouse / Keyboard devices if missing.
    ///   2. Boots <see cref="InputService"/> from the canonical asset.
    ///   3. Creates a persistent <see cref="EventSystem"/> wired to InputService.UI.
    ///
    /// Subsequent scene loads reconfigure the persistent EventSystem so any scene that
    /// still ships its own EventSystem (legacy) is collapsed into a single instance.
    /// </summary>
    public static class RuntimeInputBootstrap
    {
        private static bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (_subscribed)
                SceneManager.sceneLoaded -= OnSceneLoaded;
            _subscribed = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            EnsureRuntimeInput();

            if (_subscribed) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            _subscribed = true;
        }

        public static EventSystem EnsureRuntimeInput()
        {
            EnsureInputDevices();
            InputService.Initialize();
            return PersistentEventSystem.Ensure();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Re-ensure on every scene load so legacy scenes that still ship an
            // EventSystem get collapsed into the persistent one.
            EnsureRuntimeInput();
        }

        private static void EnsureInputDevices()
        {
            // Safety net: if the Input System has not yet auto-discovered the
            // hardware Mouse / Keyboard by BeforeSceneLoad (rare race on first
            // launch), add a virtual one so polling APIs and F-key actions are
            // never dispatched into a null device. When the real hardware is
            // discovered moments later, both coexist; <c>Mouse.current</c> /
            // <c>Keyboard.current</c> follow the most recently used device, so
            // real OS events take over the moment the user touches them.
            if (Mouse.current == null)    InputSystem.AddDevice<Mouse>();
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
        }
    }
}
