using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Keeps runtime pointer/UI input available in every scene, including menus
    /// that build their Canvas dynamically and gameplay scenes without an
    /// authored EventSystem.
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            EnsureRuntimeInput();

            if (_subscribed)
                return;

            SceneManager.sceneLoaded += OnSceneLoaded;
            _subscribed = true;
        }

        public static EventSystem EnsureRuntimeInput()
        {
            EnsureInputDevices();
            return InputDiagnostics.EnsureEventSystem();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureRuntimeInput();
        }

        private static void EnsureInputDevices()
        {
            if (Mouse.current == null)
                InputSystem.AddDevice<Mouse>();

            if (Keyboard.current == null)
                InputSystem.AddDevice<Keyboard>();
        }
    }
}
