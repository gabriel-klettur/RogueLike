using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
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

        public void CreateActions()
        {
            _toggleAction = new InputAction("ToggleTileEditor", InputActionType.Button, "<Keyboard>/f8");
            _toggleAction.Enable();

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
            _ctrlModifier = new InputAction("CtrlMod", InputActionType.Button);
            _ctrlModifier.AddBinding("<Keyboard>/leftCtrl");
            _ctrlModifier.AddBinding("<Keyboard>/rightCtrl");
            _ctrlModifier.Enable();
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
            if (_toolBrushAction.WasPerformedThisFrame()) return TileEditorState.Tool.Brush;
            if (_toolEraserAction.WasPerformedThisFrame()) return TileEditorState.Tool.Eraser;
            if (_toolFillAction.WasPerformedThisFrame()) return TileEditorState.Tool.Fill;
            if (_toolEyedropperAction.WasPerformedThisFrame()) return TileEditorState.Tool.Eyedropper;
            if (_toolSelectAction.WasPerformedThisFrame() && !ctrl) return TileEditorState.Tool.Select;
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
            if (Mathf.Abs(scroll) < 0.1f) return 0f;

            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return 0f;

            return scroll;
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

        public void Dispose()
        {
            DisposeAction(ref _toggleAction);
            DisposeAction(ref _toolBrushAction);
            DisposeAction(ref _toolEraserAction);
            DisposeAction(ref _toolFillAction);
            DisposeAction(ref _toolEyedropperAction);
            DisposeAction(ref _toolSelectAction);
            DisposeAction(ref _undoAction);
            DisposeAction(ref _redoAction);
            DisposeAction(ref _ctrlModifier);
        }

        private static void DisposeAction(ref InputAction action)
        {
            if (action == null) return;
            action.Disable();
            action.Dispose();
            action = null;
        }
    }
}
