using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Diagnostics system for mouse and input handling.
    /// Detects and reports common input system issues at runtime.
    /// </summary>
    public static class InputDiagnostics
    {
        private static bool _initialized;

        /// <summary>
        /// Run all input diagnostics and log issues to console.
        /// Called automatically on first mouse access if not explicitly run.
        /// </summary>
        public static void RunDiagnostics()
        {
            if (_initialized) return;
            _initialized = true;

            Debug.Log("[InputDiagnostics] ===== INPUT SYSTEM DIAGNOSTICS =====");

            CheckMouseDevice();
            CheckKeyboard();
            CheckEventSystem();
            CheckInputActions();
            CheckScreenSize();

            Debug.Log("[InputDiagnostics] ===== DIAGNOSTICS COMPLETE =====");
        }

        private static void CheckMouseDevice()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                Debug.LogWarning("[InputDiagnostics] Mouse device not found. Attempting to add one.");
                try
                {
                    InputSystem.AddDevice<Mouse>();
                    mouse = Mouse.current;
                    if (mouse != null)
                        Debug.Log("[InputDiagnostics] Mouse device auto-added successfully.");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[InputDiagnostics] Failed to add mouse device: {ex.Message}");
                    return;
                }
            }
            else
            {
                Debug.Log("[InputDiagnostics] Mouse device found: " + mouse.displayName);
            }

            if (mouse != null)
            {
                bool mouseEnabled = mouse.enabled;
                Debug.Log($"[InputDiagnostics] Mouse enabled: {mouseEnabled}");
                if (!mouseEnabled)
                    Debug.LogWarning("[InputDiagnostics] Mouse is disabled.");
            }
        }

        private static void CheckKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                Debug.LogWarning("[InputDiagnostics] Keyboard device not found. Attempting to add one.");
                try
                {
                    InputSystem.AddDevice<Keyboard>();
                    keyboard = Keyboard.current;
                    if (keyboard != null)
                        Debug.Log("[InputDiagnostics] Keyboard device auto-added successfully.");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[InputDiagnostics] Failed to add keyboard device: {ex.Message}");
                }
            }
            else
            {
                Debug.Log("[InputDiagnostics] Keyboard device found: " + keyboard.displayName);
            }
        }

        private static void CheckEventSystem()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                Debug.LogWarning("[InputDiagnostics] EventSystem not found. UI interactions will not work properly.");
                Debug.Log("[InputDiagnostics] Recommended: add an EventSystem or let InputDiagnostics.EnsureEventSystem create one.");
                return;
            }

            Debug.Log("[InputDiagnostics] EventSystem found: " + eventSystem.gameObject.name);
            EnsureInputSystemUIModule(eventSystem);

            var selectedObject = eventSystem.currentSelectedGameObject;
            if (selectedObject != null)
                Debug.Log("[InputDiagnostics] Currently selected object: " + selectedObject.name);
        }

        private static void CheckInputActions()
        {
            try
            {
                var actions = Resources.Load<InputActionAsset>("Input/ValkurInputActions");
                if (actions == null)
                {
                    Debug.Log("[InputDiagnostics] ValkurInputActions is not in Resources/Input; runtime code uses standalone actions.");
                    return;
                }

                Debug.Log("[InputDiagnostics] ValkurInputActions found at Resources/Input/ValkurInputActions");
                Debug.Log($"[InputDiagnostics] Input action maps: {actions.actionMaps.Count}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[InputDiagnostics] Could not verify InputActionAsset: {ex.Message}");
            }
        }

        private static void CheckScreenSize()
        {
            Debug.Log($"[InputDiagnostics] Screen size: {Screen.width}x{Screen.height}");
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var pos = mouse.position.ReadValue();
                Debug.Log($"[InputDiagnostics] Current mouse position: ({pos.x}, {pos.y})");
            }
        }

        /// <summary>
        /// Validate that a mouse click will be detected.
        /// Returns tuple: (canRead, mouseExists, buttonDown).
        /// </summary>
        public static (bool canRead, bool mouseExists, bool buttonDown) ValidateMouseClick()
        {
            RunDiagnostics();

            var mouse = Mouse.current;
            bool mouseExists = mouse != null;
            bool buttonDown = mouseExists && mouse.leftButton.isPressed;

            return (mouseExists, mouseExists, buttonDown);
        }

        /// <summary>
        /// Validate that mouse position will be read.
        /// Returns (canRead, mouseExists, lastPosition).
        /// </summary>
        public static (bool canRead, bool mouseExists, Vector2 lastPosition) ValidateMousePosition()
        {
            RunDiagnostics();

            var mouse = Mouse.current;
            bool mouseExists = mouse != null;
            Vector2 position = mouseExists ? mouse.position.ReadValue() : Vector2.zero;

            return (mouseExists, mouseExists, position);
        }

        /// <summary>
        /// Ensure EventSystem is set up properly for UI interactions.
        /// Creates one if missing and ensures it uses the Input System UI module.
        /// </summary>
        public static EventSystem EnsureEventSystem()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                eventSystem = Object.FindObjectOfType<EventSystem>();

            if (eventSystem == null)
            {
                Debug.Log("[InputDiagnostics] EventSystem missing, creating one...");
                var go = new GameObject("EventSystem");
                eventSystem = go.AddComponent<EventSystem>();
            }

            eventSystem.enabled = true;
            RemoveDuplicateEventSystems(eventSystem);
            EnsureInputSystemUIModule(eventSystem);
            return eventSystem;
        }

        public static InputSystemUIInputModule EnsureInputSystemUIModule(EventSystem eventSystem)
        {
            if (eventSystem == null)
                return null;

            var module = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (module == null)
                module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

            module.enabled = true;
            EnsureDefaultUIActions(module);

            var legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
                legacyModule.enabled = false;

            return module;
        }

        private static void EnsureDefaultUIActions(InputSystemUIInputModule module)
        {
            if (module == null)
                return;

            if (!HasUsableUIActions(module))
                module.AssignDefaultActions();

            module.actionsAsset?.Enable();
            EnableAction(module.point);
            EnableAction(module.leftClick);
            EnableAction(module.rightClick);
            EnableAction(module.middleClick);
            EnableAction(module.scrollWheel);
            EnableAction(module.move);
            EnableAction(module.submit);
            EnableAction(module.cancel);
        }

        private static bool HasUsableUIActions(InputSystemUIInputModule module)
        {
            return module.actionsAsset != null &&
                   HasAction(module.point) &&
                   HasAction(module.leftClick) &&
                   HasAction(module.rightClick) &&
                   HasAction(module.middleClick) &&
                   HasAction(module.scrollWheel) &&
                   HasAction(module.move) &&
                   HasAction(module.submit) &&
                   HasAction(module.cancel);
        }

        private static bool HasAction(InputActionReference reference)
        {
            return reference != null && reference.action != null;
        }

        private static void EnableAction(InputActionReference reference)
        {
            if (reference != null && reference.action != null && !reference.action.enabled)
                reference.action.Enable();
        }

        private static void RemoveDuplicateEventSystems(EventSystem keep)
        {
            if (keep == null)
                return;

            var eventSystems = Object.FindObjectsOfType<EventSystem>();
            for (int i = 0; i < eventSystems.Length; i++)
            {
                var eventSystem = eventSystems[i];
                if (eventSystem == null || eventSystem == keep)
                    continue;

                if (Application.isPlaying)
                    Object.Destroy(eventSystem.gameObject);
                else
                    Object.DestroyImmediate(eventSystem.gameObject);
            }
        }
    }
}
