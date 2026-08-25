using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Core.Input;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Manages all InputAction creation, tool shortcuts, layer scroll,
    /// undo/redo keys, and mouse input dispatch for the tile editor.
    /// Extracted from TileEditorManager to isolate input concerns.
    /// </summary>
    public class TileEditorInputHandler : IDisposable
    {
        private InputAction _toggleAction;
        private InputAction _toolBrushAction;
        private InputAction _toolEraserAction;
        private InputAction _toolFillAction;
        private InputAction _toolEyedropperAction;
        private InputAction _toolSelectAction;
        private InputAction _toolAutoTileAction;
        private InputAction _undoAction;
        private InputAction _redoAction;
        private InputAction _ctrlModifier;

        private bool _ownsToggleAction;
        private bool _ownsCtrlModifier;

        public void CreateActions()
        {
            TileEditorInputDevices.EnsureAvailable();
            EnsureEventSystem();

            // F8 toggle and Ctrl modifier come from the canonical InputService when
            // running under the play-mode bootstrap; otherwise (EditMode tests) the
            // resolver builds an ad-hoc binding so isolated handler tests still work.
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleTile, out _ownsToggleAction);
            _ctrlModifier = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.CtrlModifier, out _ownsCtrlModifier);

            // Tool-specific shortcuts are scoped to the Tile Editor only — kept
            // ad-hoc to avoid polluting the canonical asset with editor-internals.
            _toolBrushAction = new InputAction("ToolBrush", InputActionType.Button, "<Keyboard>/b");
            _toolBrushAction.Enable();
            _toolEraserAction = new InputAction("ToolEraser", InputActionType.Button, "<Keyboard>/e");
            _toolEraserAction.Enable();
            _toolFillAction = new InputAction("ToolFill", InputActionType.Button, "<Keyboard>/f");
            _toolFillAction.Enable();
            _toolEyedropperAction = new InputAction("ToolEyedropper", InputActionType.Button, "<Keyboard>/i");
            _toolEyedropperAction.Enable();
            _toolSelectAction = new InputAction("ToolSelect", InputActionType.Button, "<Keyboard>/s");
            _toolSelectAction.Enable();
            _toolAutoTileAction = new InputAction("ToolAutoTile", InputActionType.Button, "<Keyboard>/a");
            _toolAutoTileAction.Enable();

            _undoAction = new InputAction("Undo", InputActionType.Button, "<Keyboard>/z");
            _undoAction.Enable();
            _redoAction = new InputAction("Redo", InputActionType.Button, "<Keyboard>/z");
            _redoAction.Enable();
        }

        public bool WasTogglePressed()
        {
            // Stateless query — immune to the InputAction zombification that
            // happens after hot-recompile with Domain Reload off.
            return EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleTile);
        }

        /// <summary>
        /// Check tool shortcut keys. Returns the tool if one was pressed, null otherwise.
        /// OR'd with the legacy backend so the editor stays usable when the new
        /// InputSystem package drops OS event delivery (recurring Unity 2022.3 bug).
        /// </summary>
        public TileEditorState.Tool? PollToolShortcut()
        {
            bool ctrl = (_ctrlModifier != null && _ctrlModifier.IsPressed())
                     || EditorHotkeyBindings.IsPressed(EditorHotkeyBindings.Hotkey.CtrlModifier);

            bool brushNew = _toolBrushAction != null && _toolBrushAction.WasPerformedThisFrame();
            if (brushNew || UnityEngine.Input.GetKeyDown(KeyCode.B))         return TileEditorState.Tool.Brush;
            bool eraserNew = _toolEraserAction != null && _toolEraserAction.WasPerformedThisFrame();
            if (eraserNew || UnityEngine.Input.GetKeyDown(KeyCode.E))        return TileEditorState.Tool.Eraser;
            bool fillNew = _toolFillAction != null && _toolFillAction.WasPerformedThisFrame();
            if (fillNew || UnityEngine.Input.GetKeyDown(KeyCode.F))          return TileEditorState.Tool.Fill;
            bool eyeNew = _toolEyedropperAction != null && _toolEyedropperAction.WasPerformedThisFrame();
            if (eyeNew || UnityEngine.Input.GetKeyDown(KeyCode.I))           return TileEditorState.Tool.Eyedropper;
            bool selNew = _toolSelectAction != null && _toolSelectAction.WasPerformedThisFrame();
            if ((selNew || UnityEngine.Input.GetKeyDown(KeyCode.S)) && !ctrl) return TileEditorState.Tool.Select;
            bool autoNew = _toolAutoTileAction != null && _toolAutoTileAction.WasPerformedThisFrame();
            if ((autoNew || UnityEngine.Input.GetKeyDown(KeyCode.A)) && !ctrl) return TileEditorState.Tool.AutoTileRegion;
            return null;
        }

        /// <summary>
        /// Check mouse wheel input for camera zoom. Returns scroll delta or 0 if no scroll.
        /// </summary>
        public float PollZoom()
        {
            // MouseInputManager.GetMouseWheelDelta() ORs new + legacy backends
            // so the wheel keeps firing when the new InputSystem package drops
            // OS events (Unity 2022.3 Editor bug).
            float scroll = MouseInputManager.GetMouseWheelDelta();
            if (Mathf.Abs(scroll) < 0.1f) return 0f;

            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                return 0f;
            }
            return scroll;
        }

        /// <summary>
        /// Check undo/redo keys. Returns: 1 = undo, 2 = redo, 0 = nothing.
        /// Redo accepts both Ctrl+Shift+Z (published on the Tools panel button
        /// since before this alias existed) and Ctrl+Y — the other five runtime
        /// editors (Items, Buildings, Lighting, Boss, Map) all bind Ctrl+Y for
        /// redo, so muscle memory from any of them must work here too.
        /// </summary>
        public int PollUndoRedo()
        {
            bool ctrl = (_ctrlModifier != null && _ctrlModifier.IsPressed())
                     || EditorHotkeyBindings.IsPressed(EditorHotkeyBindings.Hotkey.CtrlModifier);
            if (!ctrl) return 0;

            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.Y, KeyCode.Y))
                return 2;

            bool zPressed = (_undoAction != null && _undoAction.WasPerformedThisFrame())
                         || UnityEngine.Input.GetKeyDown(KeyCode.Z);
            if (!zPressed) return 0;

            // KeyboardInputManager folds the new+legacy OR for shift internally.
            return Valkur.Core.Input.KeyboardInputManager.IsShiftHeld() ? 2 : 1;
        }

        public bool IsPointerOverUI()
        {
            return UnityEngine.EventSystems.EventSystem.current != null &&
                   UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        }

        /// <summary>
        /// Diagnostic method to check Input System health and mouse availability.
        /// Call this if mouse is not working to identify the issue.
        /// </summary>
        public void DiagnoseInputSystem()
        {
            TileEditorInputDevices.EnsureAvailable();
            Debug.Log("[TileEditor] === Input System Diagnosis ===");
            
            // Check Input System availability
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            
            Debug.Log($"[TileEditor] Mouse available: {mouse != null}");
            Debug.Log($"[TileEditor] Keyboard available: {keyboard != null}");
            
            if (mouse != null)
            {
                Debug.Log($"[TileEditor] Mouse position: {mouse.position.ReadValue()}");
                Debug.Log($"[TileEditor] Mouse scroll: {mouse.scroll.ReadValue()}");
                Debug.Log($"[TileEditor] Mouse left button: {mouse.leftButton.isPressed}");
                Debug.Log($"[TileEditor] Mouse right button: {mouse.rightButton.isPressed}");
            }
            
            if (keyboard != null)
            {
                Debug.Log($"[TileEditor] Space key: {keyboard.spaceKey.isPressed}");
                Debug.Log($"[TileEditor] Escape key: {keyboard.escapeKey.isPressed}");
            }
            
            // Check EventSystem
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            Debug.Log($"[TileEditor] EventSystem available: {eventSystem != null}");
            
            if (eventSystem != null)
            {
                Debug.Log($"[TileEditor] EventSystem enabled: {eventSystem.enabled}");
                Debug.Log($"[TileEditor] Pointer over UI: {eventSystem.IsPointerOverGameObject()}");
            }
            
            // Check Input Actions
            Debug.Log($"[TileEditor] Toggle action: {_toggleAction != null} (enabled: {_toggleAction?.enabled})");
            Debug.Log($"[TileEditor] Tool actions: {_toolBrushAction != null && _toolEraserAction != null && _toolFillAction != null}");
            Debug.Log($"[TileEditor] Undo/Redo actions: {_undoAction != null && _redoAction != null}");
            
            // Test PollZoom method
            float scroll = PollZoom();
            Debug.Log($"[TileEditor] PollZoom result: {scroll}");
            
            Debug.Log("[TileEditor] === End Diagnosis ===");
        }

        public void Dispose()
        {
            // Shared (InputService-owned) actions must NOT be disposed by us; only
            // dispose the ones we created locally as fallbacks in EditMode tests.
            if (_ownsToggleAction) DisposeAction(ref _toggleAction); else _toggleAction = null;
            if (_ownsCtrlModifier) DisposeAction(ref _ctrlModifier); else _ctrlModifier = null;

            DisposeAction(ref _toolBrushAction);
            DisposeAction(ref _toolEraserAction);
            DisposeAction(ref _toolFillAction);
            DisposeAction(ref _toolEyedropperAction);
            DisposeAction(ref _toolSelectAction);
            DisposeAction(ref _toolAutoTileAction);
            DisposeAction(ref _undoAction);
            DisposeAction(ref _redoAction);
        }

        private static void DisposeAction(ref InputAction action)
        {
            if (action == null) return;
            action.Disable();
            action.Dispose();
            action = null;
        }

        private static void EnsureEventSystem()
        {
            InputDiagnostics.EnsureEventSystem();
        }
    }
}
