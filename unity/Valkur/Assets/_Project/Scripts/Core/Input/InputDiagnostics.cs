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
                var actions = Resources.Load<InputActionAsset>(CanonicalUIActionsResourcePath);
                if (actions == null)
                {
                    Debug.LogWarning(
                        $"[InputDiagnostics] Canonical UI actions asset is missing at " +
                        $"Resources/{CanonicalUIActionsResourcePath}. Menus will fall back to a " +
                        "runtime-built action set (still functional, but not designer-tweakable).");
                    return;
                }

                Debug.Log($"[InputDiagnostics] Canonical UI actions found at Resources/{CanonicalUIActionsResourcePath} " +
                          $"({actions.actionMaps.Count} action maps).");
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
        ///
        /// In play mode this delegates to <see cref="PersistentEventSystem.Ensure"/>
        /// so callers (MainMenuUI, TileEditorInputHandler, etc.) all converge on the
        /// same singleton with InputService-backed action references. This prevents
        /// the "two EventSystems with conflicting wiring" bug where one is
        /// configured by the canonical bootstrap and a second is reconfigured by a
        /// per-screen helper using the legacy fallback path.
        ///
        /// In EditMode tests the legacy construction path is preserved so unit
        /// tests can build an isolated EventSystem without bootstrapping the
        /// process-wide singleton.
        /// </summary>
        public static EventSystem EnsureEventSystem()
        {
            if (Application.isPlaying)
                return PersistentEventSystem.Ensure();

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

            // Single canonical source of truth: if the scene's authored config
            // is already usable (developer-customised), respect it. Otherwise
            // assign the Valkur canonical asset (Resources/Input/ValkurInputActions).
            //
            // We deliberately do NOT call Unity's AssignDefaultActions: in
            // certain Unity 2022.3 builds it silently produces unbound action
            // refs, leaving keyboard nav working but mouse hover/click dead —
            // exactly the bug we're protecting against.
            //
            // Final defensive layer: if the canonical Resources asset is also
            // missing (unusual in a clean build), AssignValkurFallbackUIActions
            // builds an explicit in-memory asset with the same bindings.
            if (!HasUsableUIActions(module))
            {
                // Reassigning action references on a live module does not
                // always rewire its internal pointer-callback subscriptions
                // (Unity 2022.3 caches them in OnEnable). Toggle enabled
                // around the assignment so the module re-subscribes against
                // the freshly-bound actions on the next OnEnable.
                bool wasEnabled = module.enabled;
                if (wasEnabled) module.enabled = false;

                AssignValkurFallbackUIActions(module);

                if (wasEnabled) module.enabled = true;
            }

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

        // Cached so we don't churn ScriptableObjects on every scene load.
        // Reset in ResetStaticsOnPlayModeEnter (separate field, see below).
        private static InputActionAsset _fallbackUIActions;
        private static InputActionReference _fallbackPoint;
        private static InputActionReference _fallbackLeftClick;
        private static InputActionReference _fallbackRightClick;
        private static InputActionReference _fallbackMiddleClick;
        private static InputActionReference _fallbackScrollWheel;
        private static InputActionReference _fallbackMove;
        private static InputActionReference _fallbackSubmit;
        private static InputActionReference _fallbackCancel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFallbackStatics()
        {
            // With Domain Reload off the static refs would point at destroyed
            // assets across Play sessions. Wipe so the next request rebuilds.
            _fallbackUIActions   = null;
            _fallbackPoint       = null;
            _fallbackLeftClick   = null;
            _fallbackRightClick  = null;
            _fallbackMiddleClick = null;
            _fallbackScrollWheel = null;
            _fallbackMove        = null;
            _fallbackSubmit      = null;
            _fallbackCancel      = null;
            _initialized         = false;
        }

        /// <summary>
        /// Assigns a runtime-built <see cref="InputActionAsset"/> to the module so
        /// the EventSystem can route mouse + keyboard events even when the scene's
        /// authored actions asset is missing or AssignDefaultActions failed.
        ///
        /// Public so EditMode tests can call it directly without an EventSystem.
        /// </summary>
        public static void AssignValkurFallbackUIActions(InputSystemUIInputModule module)
        {
            if (module == null) return;

            EnsureFallbackAssetBuilt();

            module.actionsAsset = _fallbackUIActions;
            module.point        = _fallbackPoint;
            module.leftClick    = _fallbackLeftClick;
            module.rightClick   = _fallbackRightClick;
            module.middleClick  = _fallbackMiddleClick;
            module.scrollWheel  = _fallbackScrollWheel;
            module.move         = _fallbackMove;
            module.submit       = _fallbackSubmit;
            module.cancel       = _fallbackCancel;
        }

        /// <summary>
        /// Resources path of the canonical Valkur UI input asset. Designers can
        /// edit this asset in the Inspector; runtime code loads it as the single
        /// source of truth for menu mouse + keyboard navigation.
        /// </summary>
        public const string CanonicalUIActionsResourcePath = "Input/ValkurInputActions";

        /// <summary>
        /// Name of the action map inside the canonical asset that owns UI bindings.
        /// </summary>
        public const string CanonicalUIActionsMapName = "UI";

        private static void EnsureFallbackAssetBuilt()
        {
            if (_fallbackUIActions != null && _fallbackPoint != null) return;

            // Stage A — preferred path: the canonical Valkur asset shipped at
            // Resources/Input/ValkurInputActions. Single source of truth that
            // designers can tweak in the Inspector. We only fall through to
            // building a runtime asset (Stage B) when the canonical asset is
            // missing or incomplete (which should never happen in a clean build).
            var canonical = Resources.Load<InputActionAsset>(CanonicalUIActionsResourcePath);
            if (canonical != null && TryAdoptCanonicalAsset(canonical))
                return;

            BuildRuntimeFallbackAsset();
        }

        private static bool TryAdoptCanonicalAsset(InputActionAsset asset)
        {
            var map = asset.FindActionMap(CanonicalUIActionsMapName);
            if (map == null) return false;

            // Required actions for InputSystemUIInputModule's 8 properties.
            // Move is named "Navigate" in the canonical asset (Unity's UI module
            // exposes it via `module.move`, but the name is irrelevant — we
            // bind by reference, not by string match).
            var point        = map.FindAction("Point");
            var leftClick    = map.FindAction("Click");
            var rightClick   = map.FindAction("RightClick");
            var middleClick  = map.FindAction("MiddleClick");
            var scrollWheel  = map.FindAction("ScrollWheel");
            var move         = map.FindAction("Navigate") ?? map.FindAction("Move");
            var submit       = map.FindAction("Submit");
            var cancel       = map.FindAction("Cancel");

            if (point == null || leftClick == null || rightClick == null ||
                middleClick == null || scrollWheel == null || move == null ||
                submit == null || cancel == null)
            {
                Debug.LogWarning(
                    $"[InputDiagnostics] Canonical UI actions asset at " +
                    $"Resources/{CanonicalUIActionsResourcePath} is missing required actions " +
                    $"in map '{CanonicalUIActionsMapName}'. Falling back to runtime-built asset. " +
                    $"Required: Point, Click, RightClick, MiddleClick, ScrollWheel, Navigate, Submit, Cancel.");
                return false;
            }

            _fallbackUIActions   = asset;
            _fallbackPoint       = InputActionReference.Create(point);
            _fallbackLeftClick   = InputActionReference.Create(leftClick);
            _fallbackRightClick  = InputActionReference.Create(rightClick);
            _fallbackMiddleClick = InputActionReference.Create(middleClick);
            _fallbackScrollWheel = InputActionReference.Create(scrollWheel);
            _fallbackMove        = InputActionReference.Create(move);
            _fallbackSubmit      = InputActionReference.Create(submit);
            _fallbackCancel      = InputActionReference.Create(cancel);
            return true;
        }

        /// <summary>
        /// Last-resort builder: synthesises an in-memory UI input asset when
        /// the canonical Resources asset is absent (unusual in a clean build).
        /// Bindings mirror the canonical asset so menu navigation behaviour is
        /// identical regardless of which path was taken.
        /// </summary>
        private static void BuildRuntimeFallbackAsset()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "Valkur.UIFallback";
            asset.hideFlags = HideFlags.HideAndDontSave;

            var map = asset.AddActionMap(CanonicalUIActionsMapName);

            var point        = map.AddAction("Point",       InputActionType.PassThrough, "<Mouse>/position");
            point.expectedControlType = "Vector2";

            var leftClick    = map.AddAction("Click",       InputActionType.PassThrough, "<Mouse>/leftButton");
            leftClick.expectedControlType = "Button";

            var rightClick   = map.AddAction("RightClick",  InputActionType.PassThrough, "<Mouse>/rightButton");
            rightClick.expectedControlType = "Button";

            var middleClick  = map.AddAction("MiddleClick", InputActionType.PassThrough, "<Mouse>/middleButton");
            middleClick.expectedControlType = "Button";

            var scroll       = map.AddAction("ScrollWheel", InputActionType.PassThrough, "<Mouse>/scroll");
            scroll.expectedControlType = "Vector2";

            var move = map.AddAction("Navigate", InputActionType.PassThrough);
            move.expectedControlType = "Vector2";
            move.AddCompositeBinding("2DVector")
                .With("Up",    "<Keyboard>/upArrow")
                .With("Down",  "<Keyboard>/downArrow")
                .With("Left",  "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            move.AddCompositeBinding("2DVector")
                .With("Up",    "<Keyboard>/w")
                .With("Down",  "<Keyboard>/s")
                .With("Left",  "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            var submit = map.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            submit.AddBinding("<Keyboard>/numpadEnter");
            submit.AddBinding("<Keyboard>/space");

            var cancel = map.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape");

            _fallbackUIActions   = asset;
            _fallbackPoint       = InputActionReference.Create(point);
            _fallbackLeftClick   = InputActionReference.Create(leftClick);
            _fallbackRightClick  = InputActionReference.Create(rightClick);
            _fallbackMiddleClick = InputActionReference.Create(middleClick);
            _fallbackScrollWheel = InputActionReference.Create(scroll);
            _fallbackMove        = InputActionReference.Create(move);
            _fallbackSubmit      = InputActionReference.Create(submit);
            _fallbackCancel      = InputActionReference.Create(cancel);
        }

        public static bool HasUsableUIActions(InputSystemUIInputModule module)
        {
            return module != null &&
                   module.actionsAsset != null &&
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
