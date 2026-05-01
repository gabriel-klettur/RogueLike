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

            _undoAction = new InputAction("Undo", InputActionType.Button, "<Keyboard>/z");
            _undoAction.Enable();
            _redoAction = new InputAction("Redo", InputActionType.Button, "<Keyboard>/z");
            _redoAction.Enable();
        }

        public bool WasTogglePressed()
        {
            return _toggleAction != null && _toggleAction.WasPerformedThisFrame();
        }

        /// <summary>
        /// Check tool shortcut keys. Returns the tool if one was pressed, null otherwise.
        /// </summary>
        public TileEditorState.Tool? PollToolShortcut()
        {
            bool ctrl = _ctrlModifier != null && _ctrlModifier.IsPressed();
            if (_toolBrushAction != null && _toolBrushAction.WasPerformedThisFrame()) return TileEditorState.Tool.Brush;
            if (_toolEraserAction != null && _toolEraserAction.WasPerformedThisFrame()) return TileEditorState.Tool.Eraser;
            if (_toolFillAction != null && _toolFillAction.WasPerformedThisFrame()) return TileEditorState.Tool.Fill;
            if (_toolEyedropperAction != null && _toolEyedropperAction.WasPerformedThisFrame()) return TileEditorState.Tool.Eyedropper;
            if (_toolSelectAction != null && _toolSelectAction.WasPerformedThisFrame() && !ctrl) return TileEditorState.Tool.Select;
            return null;
        }

        /// <summary>
        /// Check mouse wheel input for camera zoom. Returns scroll delta or 0 if no scroll.
        /// </summary>
        public float PollZoom()
        {
            var mouse = Mouse.current;
            if (mouse == null) return 0f;
            float scroll = mouse.scroll.ReadValue().y;
            
            // Debug log to help diagnose input issues
            if (Mathf.Abs(scroll) >= 0.1f)
            {
                Debug.Log($"[TileEditor] Mouse scroll detected: {scroll:F2}");
                
                if (UnityEngine.EventSystems.EventSystem.current != null &&
                    UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                {
                    Debug.Log("[TileEditor] Scroll blocked - pointer over UI");
                    return 0f;
                }
                
                return scroll;
            }
            
            return 0f;
        }

        /// <summary>
        /// Check undo/redo keys. Returns: 1 = undo, 2 = redo, 0 = nothing.
        /// </summary>
        public int PollUndoRedo()
        {
            bool ctrl = _ctrlModifier != null && _ctrlModifier.IsPressed();
            if (!ctrl || !_undoAction.WasPerformedThisFrame()) return 0;

            var kb = Keyboard.current;
            bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            return shift ? 2 : 1;
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
