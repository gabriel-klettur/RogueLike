using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Runtime UI for the map editor.
    /// Focuses on zone CRUD + editable-zone control for tile editor permissions.
    /// </summary>
    public partial class MapEditorUI : MonoBehaviour
    {
        private MapEditorState _state;

        private System.Action<string> _onZoneSelected;
        private System.Action _onBeginAddZoneFlow;
        private System.Action<string, bool, bool> _onConfirmAddZone;
        private System.Action _onCancelAddZoneFlow;
        private System.Action _onDuplicateSelectedZone;
        private System.Action _onRequestDeleteSelectedZone;
        private System.Action _onConfirmDeleteSelectedZone;
        private System.Action<string> _onRenameSelectedZone;
        private System.Action<string, string> _onRenameZoneByName;
        private System.Action _onToggleSelectedZoneEditable;
        private System.Action<string> _onToggleZoneEditableByName;
        private System.Action<Vector2Int> _onMoveSelectedZone;
        private System.Action<bool> _onRestrictEditChanged;

        private Transform _canvasRoot;
        private GameObject _root;
        private Transform _zonesListContent;
        private readonly List<Button> _zoneButtons = new List<Button>();
        private ZoneManager.ZoneDefinition[] _cachedZones = Array.Empty<ZoneManager.ZoneDefinition>();
        private string _inlineRenameZoneName;

        private TMP_Text _selectedZoneText;
        private TMP_Text _selectedEditableText;
        private TMP_Text _statusText;
        private TMP_InputField _nameInput;
        private Toggle _restrictToggle;

        private GameObject _addZoneDialog;
        private TMP_InputField _addZoneNameInput;
        private TMP_Text _addZoneSourceText;
        private TMP_Text _addZoneTargetText;
        private Toggle _addUseTemplateToggle;
        private Toggle _addEditableToggle;

        private GameObject _deleteZoneDialog;
        private TMP_Text _deleteZonePrompt;

        private static Sprite _whiteSprite;

        public string NameInput => _nameInput != null ? _nameInput.text : string.Empty;
        public bool IsTypingInput =>
            EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        public bool IsModalOpen =>
            (_addZoneDialog != null && _addZoneDialog.activeSelf) ||
            (_deleteZoneDialog != null && _deleteZoneDialog.activeSelf);

        public void Initialize(
            MapEditorState state,
            System.Action<string> onZoneSelected,
            System.Action onBeginAddZoneFlow,
            System.Action<string, bool, bool> onConfirmAddZone,
            System.Action onCancelAddZoneFlow,
            System.Action onDuplicateSelectedZone,
            System.Action onRequestDeleteSelectedZone,
            System.Action onConfirmDeleteSelectedZone,
            System.Action<string> onRenameSelectedZone,
            System.Action<string, string> onRenameZoneByName,
            System.Action onToggleSelectedZoneEditable,
            System.Action<string> onToggleZoneEditableByName,
            System.Action<Vector2Int> onMoveSelectedZone,
            System.Action<bool> onRestrictEditChanged)
        {
            _state = state;
            _onZoneSelected = onZoneSelected;
            _onBeginAddZoneFlow = onBeginAddZoneFlow;
            _onConfirmAddZone = onConfirmAddZone;
            _onCancelAddZoneFlow = onCancelAddZoneFlow;
            _onDuplicateSelectedZone = onDuplicateSelectedZone;
            _onRequestDeleteSelectedZone = onRequestDeleteSelectedZone;
            _onConfirmDeleteSelectedZone = onConfirmDeleteSelectedZone;
            _onRenameSelectedZone = onRenameSelectedZone;
            _onRenameZoneByName = onRenameZoneByName;
            _onToggleSelectedZoneEditable = onToggleSelectedZoneEditable;
            _onToggleZoneEditableByName = onToggleZoneEditableByName;
            _onMoveSelectedZone = onMoveSelectedZone;
            _onRestrictEditChanged = onRestrictEditChanged;

            BuildUI();
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (_root != null)
                _root.SetActive(visible);

            if (!visible)
            {
                HideAddZoneDialog();
                HideDeleteZoneDialog();
            }
        }

        public void RefreshZones(ZoneManager.ZoneDefinition[] zones)
        {
            _cachedZones = zones ?? Array.Empty<ZoneManager.ZoneDefinition>();

            bool inlineExists = false;
            for (int i = 0; i < _cachedZones.Length; i++)
            {
                if (_cachedZones[i].zoneName == _inlineRenameZoneName)
                {
                    inlineExists = true;
                    break;
                }
            }

            if (!inlineExists)
                _inlineRenameZoneName = null;

            RebuildZonesList();
        }

        public void SetSelectedZone(string zoneName, bool editable)
        {
            if (_selectedZoneText != null)
                _selectedZoneText.text = string.IsNullOrWhiteSpace(zoneName)
                    ? "Selected: (none)"
                    : $"Selected: {zoneName}";

            if (_selectedEditableText != null)
            {
                _selectedEditableText.text = string.IsNullOrWhiteSpace(zoneName)
                    ? "Editable: n/a"
                    : $"Editable: {(editable ? "YES" : "NO")}";
                _selectedEditableText.color = editable
                    ? new Color(0.62f, 1f, 0.62f, 1f)
                    : new Color(1f, 0.62f, 0.62f, 1f);
            }

            if (_nameInput != null && !string.IsNullOrWhiteSpace(zoneName))
                _nameInput.text = zoneName;
        }

        public void SetRestrictToggle(bool restrict)
        {
            if (_restrictToggle != null)
                _restrictToggle.SetIsOnWithoutNotify(restrict);
        }

        public void SetStatus(string text)
        {
            if (_statusText != null)
                _statusText.text = text;
        }

        public void ShowAddZoneDialog(string suggestedName, string sourceZoneName, bool sourceEditable)
        {
            if (_addZoneDialog == null) return;

            _addZoneDialog.SetActive(true);

            if (_addZoneNameInput != null)
                _addZoneNameInput.text = suggestedName;

            SetAddZoneSource(sourceZoneName, sourceEditable);

            if (_addUseTemplateToggle != null)
                _addUseTemplateToggle.SetIsOnWithoutNotify(true);
            if (_addEditableToggle != null)
                _addEditableToggle.SetIsOnWithoutNotify(sourceEditable);

            SetAddZoneTarget(Vector2Int.zero, 50, 50, false);
        }

        public void HideAddZoneDialog()
        {
            if (_addZoneDialog != null)
                _addZoneDialog.SetActive(false);
        }

        public void SetAddZoneSource(string sourceZoneName, bool editable)
        {
            if (_addZoneSourceText == null)
                return;

            string source = string.IsNullOrWhiteSpace(sourceZoneName) ? "(none)" : sourceZoneName;
            _addZoneSourceText.text = $"Source: {source} ({(editable ? "EDIT" : "LOCK")})";

            if (_addEditableToggle != null && _addUseTemplateToggle != null && _addUseTemplateToggle.isOn)
                _addEditableToggle.SetIsOnWithoutNotify(editable);
        }

        public void SetAddZoneTarget(Vector2Int gridOffset, int zoneWidth, int zoneHeight, bool hasTarget)
        {
            if (_addZoneTargetText == null)
                return;

            _addZoneTargetText.text = hasTarget
                ? $"Target: [{gridOffset.x},{gridOffset.y}] ({zoneWidth}x{zoneHeight})"
                : $"Target: click map to mark ({zoneWidth}x{zoneHeight})";
        }

        public void ShowDeleteZoneDialog(string zoneName)
        {
            if (_deleteZoneDialog == null || _deleteZonePrompt == null)
                return;

            _deleteZonePrompt.text = $"Delete zone '{zoneName}'?";
            _deleteZoneDialog.SetActive(true);
        }

        public void HideDeleteZoneDialog()
        {
            if (_deleteZoneDialog != null)
                _deleteZoneDialog.SetActive(false);
        }

        private void RebuildZonesList()
    }
}
