using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {


        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            RefreshModeButtons();
        }

        private void RefreshModeButtons()
        {
            if (_selectBtnImg) _selectBtnImg.color = _mode == EditorMode.Select ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_placeBtnImg) _placeBtnImg.color = _mode == EditorMode.Place ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_deleteBtnImg) _deleteBtnImg.color = _mode == EditorMode.Delete ? EditorUIHelpers.DANGER : new Color(0.55f, 0.15f, 0.15f, 1f);
        }

        // ── Picker ──

        private void RefreshPicker()
        {
            for (int i = _pickerContent.childCount - 1; i >= 0; i--)
                Destroy(_pickerContent.GetChild(i).gameObject);

            if (_catalog == null) return;

            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            int shown = 0;
            foreach (var preset in _catalog.Presets)
            {
                if (preset == null) continue;
                var pid = preset.id;
                if (filter.Length > 0)
                {
                    string n = (preset.displayName ?? pid ?? "").ToLowerInvariant();
                    if (!n.Contains(filter) && !(pid ?? "").ToLowerInvariant().Contains(filter)) continue;
                }
                shown++;
                var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                    _pickerContent, preset.displayName ?? pid, 64f,
                    () => SelectPreset(pid));
                label.text = TruncateName(preset.displayName ?? pid, 8);

                if (pid == _selectedPresetId)
                    btn.GetComponent<Image>().color = EditorUIHelpers.SLOT_SELECTED;
            }
            if (_statusTmp != null)
                _statusTmp.text = filter.Length == 0 ? $"{shown} presets" : $"{shown} match '{_searchFilter}'";
        }

        private void SelectPreset(string pid)
        {
            _selectedPresetId = pid;
            RefreshPicker();
            ShowPresetProperties(pid);
        }

        private void ShowPresetProperties(string pid)
        {
            var preset = _catalog?.GetById(pid);
            if (preset == null) { _propsTmp.text = "Not found."; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>ID:</b> {preset.id}");
            sb.AppendLine($"<b>Name:</b> {preset.displayName}");
            sb.AppendLine($"<b>Type:</b> {preset.type}");
            _propsTmp.text = sb.ToString();
            _propsTmp.richText = true;
            _statusTmp.text = $"Selected: {preset.displayName ?? pid}";
        }

        // ── Map Interaction ──

        private void HandleMapInteraction()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

            var cam = Camera.main;
            if (cam == null) return;
            var worldPos = (Vector3)cam.ScreenToWorldPoint(mouse.position.ReadValue());
            worldPos.z = 0;

            if (_dragging && _dragTarget != null)
            {
                _dragTarget.transform.position = worldPos + _dragOffset;
                if (mouse.rightButton.wasReleasedThisFrame)
                    _dragging = false;
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (_mode == EditorMode.Place && !string.IsNullOrEmpty(_selectedPresetId))
                {
                    _statusTmp.text = $"Placed {_selectedPresetId} at ({worldPos.x:F1}, {worldPos.y:F1})";
                    Debug.Log($"[ParticlesEditor] Place {_selectedPresetId} at {worldPos}");
                }
                else if (_mode == EditorMode.Delete)
                {
                    var ps = Physics2D.OverlapCircle(worldPos, 0.5f);
                    if (ps != null && ps.GetComponent<ParticleSystem>() != null)
                    {
                        _statusTmp.text = $"Deleted particle: {ps.gameObject.name}";
                        Destroy(ps.gameObject);
                    }
                }
            }

            if (mouse.rightButton.wasPressedThisFrame && _mode == EditorMode.Select)
            {
                var hit = Physics2D.OverlapCircle(worldPos, 0.5f);
                if (hit != null && hit.GetComponent<ParticleSystem>() != null)
                {
                    _dragTarget = hit.gameObject;
                    _dragging = true;
                    _dragOffset = _dragTarget.transform.position - worldPos;
                }
            }
        }

        private void SaveInstances()
        {
            _statusTmp.text = "Saved particle instances.";
            Debug.Log("[ParticlesEditor] Save requested.");
        }

        private static string TruncateName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= max ? name : name.Substring(0, max - 1) + "…";
        }
    }
}