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
        private InputAction _ctrlModifier;

        // The eight tool / undo / redo InputActions this used to build in code are GONE.
        // They lived outside ValkurInputActions, so no audit could see them and nobody could
        // rebind them — the same defect as InventoryUI's tab — and they hid a real bug:
        // _redoAction was constructed on "<Keyboard>/z", the SAME path as _undoAction, so the
        // InputSystem half of redo had been firing on Ctrl+Z for the life of this file and
        // only the legacy Ctrl+Y read below ever did the right thing. They are asset actions
        // in the Editor.Tile map now, reached through EditorInput.Tool.

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

            // The tool shortcuts used to be built here as ad-hoc InputActions "to avoid
            // polluting the canonical asset with editor-internals". That reasoning is what
            // made them unauditable and unbindable; the asset is where a binding belongs, and
            // the Editor.Tile map is how it stays scoped to this editor.
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

            // EditorInput.Tool ORs both backends AND checks that the Tile editor is the one
            // currently open, so these can share keys with another editor's tools freely —
            // which is the point of an editor owning the whole board. The raw
            // UnityEngine.Input.GetKeyDown fallbacks are gone with the same change: they
            // answered while the chat had focus, and they did not move when a key was rebound.
            const string M = InputActionCatalog.MapTileEditor;
            if (EditorInput.Tool(M, "ToolBrush"))              return TileEditorState.Tool.Brush;
            if (EditorInput.Tool(M, "ToolEraser"))             return TileEditorState.Tool.Eraser;
            if (EditorInput.Tool(M, "ToolFill"))               return TileEditorState.Tool.Fill;
            if (EditorInput.Tool(M, "ToolEyedropper"))         return TileEditorState.Tool.Eyedropper;
            if (EditorInput.Tool(M, "ToolSelect")   && !ctrl)  return TileEditorState.Tool.Select;
            if (EditorInput.Tool(M, "ToolAutoTile") && !ctrl)  return TileEditorState.Tool.AutoTileRegion;
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

            // Ctrl+Y is the shared Redo verb; Ctrl+Shift+Z is this editor's historical alias
            // for the same thing, published on the Tools panel button since before Ctrl+Y
            // existed here. Both resolve through the shared bindings, so moving Redo in the
            // Controls editor moves the first and leaves the alias on whatever Undo is.
            if (EditorInput.RedoPressed()) return 2;
            if (!EditorInput.UndoPressed()) return 0;

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
            Debug.Log($"[TileEditor] Tool bindings: brush={InputBindingResolver.PrimaryLabel(ToolAction("ToolBrush"))} " +
                      $"eraser={InputBindingResolver.PrimaryLabel(ToolAction("ToolEraser"))} " +
                      $"fill={InputBindingResolver.PrimaryLabel(ToolAction("ToolFill"))}");
            Debug.Log($"[TileEditor] Live context: {InputContexts.Current}");
            
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

            // Nothing else to dispose: the tool, undo and redo actions belong to the
            // canonical asset now, and disposing an InputService-owned action would take it
            // away from every other consumer for the rest of the session.
        }

        /// <summary>One of this editor's own actions, from the canonical asset. Null when the
        /// asset has no such action, which the catalog coverage test reports.</summary>
        private static InputAction ToolAction(string action)
        {
            var map = InputService.Instance?.Asset?.FindActionMap(
                InputActionCatalog.MapTileEditor, throwIfNotFound: false);
            return map?.FindAction(action, throwIfNotFound: false);
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
