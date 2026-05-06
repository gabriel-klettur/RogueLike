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
        private System.Action<bool> _onRestrictEditChanged;
        private System.Action<MapEditorUIBuilder.BiomeDialogResult> _onConfirmGenerateBiomes;
        private MapEditorUIBuilder.MapSlotCallbacks _mapSlotCallbacks;

        // Runtime-only references set by BuildUI(). NOT [SerializeField] — ResolveCanvas()
        // handles domain-reload recovery via GetComponentInChildren fallback; keeping these
        // as plain private fields avoids Unity serialization writing stale destroyed-object
        // references from prior instances into freshly created components.
        private Transform _canvasRoot;
        private Canvas    _cachedCanvas;
        private MapEditorUIBuilder.UIRefs _refs;
        private readonly HashSet<string> _openDropdowns = new HashSet<string>();
        private readonly List<Button> _zoneButtons = new List<Button>();
        private ZoneManager.ZoneDefinition[] _cachedZones = Array.Empty<ZoneManager.ZoneDefinition>();
        private string _inlineRenameZoneName;

        // Add Zone blinking mode
        private bool _isAddZoneMode;

        private static Sprite _whiteSprite;

        public string NameInput => _refs.NameInput != null ? _refs.NameInput.text : string.Empty;
        public bool IsTypingInput =>
            EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        public bool IsModalOpen =>
            (_refs.AddZoneDialog != null && _refs.AddZoneDialog.activeSelf) ||
            (_refs.DeleteZoneDialog != null && _refs.DeleteZoneDialog.activeSelf) ||
            (_refs.MapsDeleteDialog != null && _refs.MapsDeleteDialog.activeSelf) ||
            (_refs.MapsNewDialog != null && _refs.MapsNewDialog.activeSelf) ||
            (_refs.MapsRenameDialog != null && _refs.MapsRenameDialog.activeSelf);

        /// <summary>Toggles the named floating panel open/closed and updates menu button styles.</summary>
        public void OnDropdownToggle(string key)
        {
            if (_openDropdowns.Contains(key))
                _openDropdowns.Remove(key);
            else
                _openDropdowns.Add(key);

            SetDropdownVisible("zones",    _refs.ZonesDropdown);
            SetDropdownVisible("actions",  _refs.ActionsDropdown);
            SetDropdownVisible("props",    _refs.PropsDropdown);
            SetDropdownVisible("biomes",   _refs.BiomesDropdown);
            SetDropdownVisible("maps",     _refs.MapsDropdown);
            UpdateMenuBtnStyles();
        }

        private void SetDropdownVisible(string key, GameObject panel)
        {
            if (panel != null)
                panel.SetActive(_openDropdowns.Contains(key));
        }

        private void UpdateMenuBtnStyles()
        {
            MapEditorUIBuilder.ApplyMenuBtnStyle(
                _refs.ZonesMenuBtnImg,    _refs.ZonesMenuBtnTmp,    _openDropdowns.Contains("zones"));
            MapEditorUIBuilder.ApplyMenuBtnStyle(
                _refs.ActionsMenuBtnImg,  _refs.ActionsMenuBtnTmp,  _openDropdowns.Contains("actions"));
            MapEditorUIBuilder.ApplyMenuBtnStyle(
                _refs.PropsMenuBtnImg,    _refs.PropsMenuBtnTmp,    _openDropdowns.Contains("props"));
            MapEditorUIBuilder.ApplyMenuBtnStyle(
                _refs.BiomesMenuBtnImg,   _refs.BiomesMenuBtnTmp,   _openDropdowns.Contains("biomes"));
            MapEditorUIBuilder.ApplyMenuBtnStyle(
                _refs.MapsMenuBtnImg,     _refs.MapsMenuBtnTmp,     _openDropdowns.Contains("maps"));
        }

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
            System.Action<bool> onRestrictEditChanged,
            System.Action<MapEditorUIBuilder.BiomeDialogResult> onConfirmGenerateBiomes,
            MapEditorUIBuilder.MapSlotCallbacks mapSlotCallbacks)
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
            _onRestrictEditChanged = onRestrictEditChanged;
            _onConfirmGenerateBiomes = onConfirmGenerateBiomes;
            _mapSlotCallbacks = mapSlotCallbacks;

            BuildUI();
            SetVisible(false);
        }

        /// <summary>
        /// Activates or deactivates the Add Zone blinking mode on the button.
        /// When active the button outline pulses yellow in Update().
        /// </summary>
        public void SetAddZoneMode(bool active)
        {
            _isAddZoneMode = active;
            if (!active)
            {
                if (_refs.AddZoneBtnOutline != null)
                    _refs.AddZoneBtnOutline.effectColor = new Color(0f, 0f, 0f, 0f);
                if (_refs.AddZoneBtnImage != null)
                    _refs.AddZoneBtnImage.color = new Color(0.16f, 0.16f, 0.21f, 1f); // BTN_NORMAL
            }
        }

        private void Update()
        {
            if (!_isAddZoneMode) return;
            if (_refs.AddZoneBtnOutline == null) return;
            float pulse = (Mathf.Sin(Time.unscaledTime * 5f) + 1f) * 0.5f;
            _refs.AddZoneBtnOutline.effectColor =
                new Color(1f, 0.85f, 0f, Mathf.Lerp(0.15f, 1f, pulse));
            if (_refs.AddZoneBtnImage != null)
                _refs.AddZoneBtnImage.color = Color.Lerp(
                    new Color(0.16f, 0.16f, 0.21f, 1f),
                    new Color(0.30f, 0.25f, 0.06f, 1f),
                    pulse * 0.45f);
        }

        public void SetVisible(bool visible)
        {
            var canvas = ResolveCanvas();
            if (canvas != null) canvas.enabled = visible;

            if (!visible)
            {
                _openDropdowns.Clear();
                SetDropdownVisible("zones",    _refs.ZonesDropdown);
                SetDropdownVisible("actions",  _refs.ActionsDropdown);
                SetDropdownVisible("props",    _refs.PropsDropdown);
                UpdateMenuBtnStyles();
                HideAddZoneDialog();
                HideDeleteZoneDialog();
                HideMapsDeleteDialog();
                HideMapsNewDialog();
                HideMapsRenameDialog();
            }
        }

        /// <summary>
        /// Robustly resolve the editor's canvas. Tolerates lost private references
        /// after Unity domain reloads / hot-reloads while in Play Mode by falling
        /// back to the actual canvas component in this UI's children.
        /// </summary>
        private Canvas ResolveCanvas()
        {
            // Unity's overloaded == treats destroyed objects as null.
            if (_cachedCanvas != null) return _cachedCanvas;

            if (_canvasRoot != null)
            {
                _cachedCanvas = _canvasRoot.GetComponent<Canvas>();
                if (_cachedCanvas != null) return _cachedCanvas;
            }

            _cachedCanvas = GetComponentInChildren<Canvas>(true);
            if (_cachedCanvas != null)
                _canvasRoot = _cachedCanvas.transform;

            return _cachedCanvas;
        }

        private void OnEnable()
        {
            // Re-bind canvas reference if it was lost across a hot-reload.
            ResolveCanvas();
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
            // Pre-fill the rename input in the Properties panel with the selected zone name.
            if (_refs.NameInput != null && !string.IsNullOrWhiteSpace(zoneName))
                _refs.NameInput.text = zoneName;
        }

        /// <summary>
        /// Populates the Properties panel with data for the currently selected zone,
        /// or clears it to the idle hint state when <paramref name="hasZone"/> is false.
        /// </summary>
        public void SetPropertiesData(bool hasZone, string zoneName,
            UnityEngine.Vector2Int gridOffset, bool editable,
            int widthTiles, int heightTiles)
        {
            if (_refs.PropsHintText != null)
                _refs.PropsHintText.text = hasZone
                    ? string.Empty
                    : "Select a zone to\nview its properties.";

            if (_refs.NameInput != null)
                _refs.NameInput.text = hasZone ? (zoneName ?? string.Empty) : string.Empty;

            if (_refs.PropsOffsetText != null)
                _refs.PropsOffsetText.text = hasZone
                    ? $"[{gridOffset.x}, {gridOffset.y}]"
                    : "\u2014";

            if (_refs.PropsDimText != null)
                _refs.PropsDimText.text = hasZone
                    ? $"{widthTiles}\u00D7{heightTiles} tiles"
                    : "\u2014";

            if (_refs.PropsEditableText != null)
            {
                _refs.PropsEditableText.text = hasZone ? (editable ? "YES" : "NO") : "\u2014";
                _refs.PropsEditableText.color = hasZone
                    ? (editable
                        ? new Color(0.4f, 0.95f, 0.4f, 1f)
                        : new Color(1f, 0.42f, 0.42f, 1f))
                    : new Color(0.55f, 0.60f, 0.70f, 0.85f);
            }

            // Enable/disable interactive controls based on whether a zone is selected.
            if (_refs.NameInput != null)
                _refs.NameInput.interactable = hasZone;
        }

        public void SetRestrictToggle(bool restrict)
        {
            if (_refs.RestrictToggle != null)
                _refs.RestrictToggle.SetIsOnWithoutNotify(restrict);
        }

        public void SetStatus(string text)
        {
            if (_refs.StatusBarText != null)
                _refs.StatusBarText.text = text;
        }

        public void ShowAddZoneDialog(string suggestedName, string sourceZoneName, bool sourceEditable)
        {
            if (_refs.AddZoneDialog == null) return;

            _refs.AddZoneDialog.SetActive(true);

            if (_refs.AddZoneNameInput != null)
                _refs.AddZoneNameInput.text = suggestedName;

            SetAddZoneSource(sourceZoneName, sourceEditable);

            // Template only makes sense when there is a source zone selected.
            // Without a source, default to OFF and disable the toggle so the
            // user can't trigger the "source required" failure path silently.
            bool hasSource = !string.IsNullOrWhiteSpace(sourceZoneName);
            if (_refs.AddUseTemplateToggle != null)
            {
                _refs.AddUseTemplateToggle.SetIsOnWithoutNotify(hasSource);
                _refs.AddUseTemplateToggle.interactable = hasSource;
            }
            if (_refs.AddEditableToggle != null)
                _refs.AddEditableToggle.SetIsOnWithoutNotify(sourceEditable);

            SetAddZoneTarget(Vector2Int.zero, 50, 50, false);
        }

        public void HideAddZoneDialog()
        {
            if (_refs.AddZoneDialog != null)
                _refs.AddZoneDialog.SetActive(false);
        }

        public void SetAddZoneSource(string sourceZoneName, bool editable)
        {
            if (_refs.AddZoneSourceText == null)
                return;

            string source = string.IsNullOrWhiteSpace(sourceZoneName) ? "(none)" : sourceZoneName;
            _refs.AddZoneSourceText.text = $"Source: {source} ({(editable ? "EDIT" : "LOCK")})";

            if (_refs.AddEditableToggle != null && _refs.AddUseTemplateToggle != null && _refs.AddUseTemplateToggle.isOn)
                _refs.AddEditableToggle.SetIsOnWithoutNotify(editable);
        }

        public void SetAddZoneTarget(Vector2Int gridOffset, int zoneWidth, int zoneHeight, bool hasTarget)
        {
            if (_refs.AddZoneTargetText == null)
                return;

            _refs.AddZoneTargetText.text = hasTarget
                ? $"Target: [{gridOffset.x},{gridOffset.y}] ({zoneWidth}x{zoneHeight})"
                : $"Target: click map to mark ({zoneWidth}x{zoneHeight})";
        }

        public void ShowDeleteZoneDialog(string zoneName)
        {
            if (_refs.DeleteZoneDialog == null || _refs.DeleteZonePrompt == null)
                return;

            _refs.DeleteZonePrompt.text = $"Delete zone '{zoneName}'?";
            _refs.DeleteZoneDialog.SetActive(true);
        }

        public void HideDeleteZoneDialog()
        {
            if (_refs.DeleteZoneDialog != null)
                _refs.DeleteZoneDialog.SetActive(false);
        }

    }
}
