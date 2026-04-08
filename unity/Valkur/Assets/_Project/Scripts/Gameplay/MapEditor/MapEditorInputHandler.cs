using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// InputAction wrapper for map editor hotkeys and click intent.
    /// Keeps input concerns isolated from map editor orchestration.
    /// </summary>
    public class MapEditorInputHandler : IDisposable
    {
        private InputAction _toggleAction;
        private InputAction _selectAction;
        private InputAction _createAction;
        private InputAction _duplicateAction;
        private InputAction _deleteAction;
        private InputAction _renameAction;
        private InputAction _toggleEditableAction;

        public void CreateActions()
        {
            _toggleAction = new InputAction("ToggleMapEditor", InputActionType.Button, "<Keyboard>/f11");
            _toggleAction.Enable();

            _selectAction = new InputAction("MapEditorSelect", InputActionType.Button, "<Mouse>/leftButton");
            _selectAction.Enable();

            _createAction = new InputAction("MapEditorCreate", InputActionType.Button, "<Keyboard>/n");
            _createAction.Enable();

            _duplicateAction = new InputAction("MapEditorDuplicate", InputActionType.Button, "<Keyboard>/d");
            _duplicateAction.Enable();

            _deleteAction = new InputAction("MapEditorDelete", InputActionType.Button, "<Keyboard>/delete");
            _deleteAction.Enable();

            _renameAction = new InputAction("MapEditorRename", InputActionType.Button, "<Keyboard>/r");
            _renameAction.Enable();

            _toggleEditableAction = new InputAction("MapEditorToggleEditable", InputActionType.Button, "<Keyboard>/e");
            _toggleEditableAction.Enable();
        }

        public bool WasTogglePressed()
        {
            return _toggleAction != null && _toggleAction.WasPerformedThisFrame();
        }

        public bool WasSelectPressed()
        {
            return _selectAction != null && _selectAction.WasPerformedThisFrame();
        }

        public bool WasCreatePressed()
        {
            return _createAction != null && _createAction.WasPerformedThisFrame();
        }

        public bool WasDuplicatePressed()
        {
            return _duplicateAction != null && _duplicateAction.WasPerformedThisFrame();
        }

        public bool WasDeletePressed()
        {
            return _deleteAction != null && _deleteAction.WasPerformedThisFrame();
        }

        public bool WasRenamePressed()
        {
            return _renameAction != null && _renameAction.WasPerformedThisFrame();
        }

        public bool WasToggleEditablePressed()
        {
            return _toggleEditableAction != null && _toggleEditableAction.WasPerformedThisFrame();
        }

        public bool IsPointerOverUI()
        {
            return UnityEngine.EventSystems.EventSystem.current != null &&
                   UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        }

        public void Dispose()
        {
            DisposeAction(ref _toggleAction);
            DisposeAction(ref _selectAction);
            DisposeAction(ref _createAction);
            DisposeAction(ref _duplicateAction);
            DisposeAction(ref _deleteAction);
            DisposeAction(ref _renameAction);
            DisposeAction(ref _toggleEditableAction);
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
