using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.World
{
    public partial class LightingRuntimeEditor : SingletonMonoBehaviour<LightingRuntimeEditor>, GameEditorManager.IGameEditor
    {

        private void RefreshPresetButtons()
        {
            if (_presetButtonsParent == null) return;
            for (int i = _presetButtonsParent.childCount - 1; i >= 0; i--)
                Destroy(_presetButtonsParent.GetChild(i).gameObject);
            if (_catalog == null) return;
            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            foreach (var preset in _catalog.presets)
            {
                if (preset == null) continue;
                var key = preset.presetKey;
                if (filter.Length > 0 && !(key ?? "").ToLowerInvariant().Contains(filter)) continue;
                var btn = EditorUIHelpers.MakeButton(_presetButtonsParent, key, () => SelectPreset(key), 28f, 11f);
                if (key == _selectedPresetKey)
                    btn.GetComponent<Image>().color = EditorUIHelpers.BTN_ACTIVE;
            }
        }

        private void SelectPreset(string key)
        {
            _selectedPresetKey = key;
            ShowPresetProperties(key);
            RefreshPresetButtons();
            _statusTmp.text = $"Preset: {key}";
        }

        private void ShowPresetProperties(string key)
        {
            var preset = _catalog?.GetByKey(key);
            if (preset == null) { _propsTmp.text = "Not found."; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Key:</b> {preset.presetKey}");
            sb.AppendLine($"<b>Radius:</b> {preset.radius:F1}");
            sb.AppendLine($"<b>Intensity:</b> {preset.intensity:F2}");
            sb.AppendLine($"<b>Falloff:</b> {preset.falloff:F2}");
            sb.AppendLine($"<b>Color:</b> {ColorUtility.ToHtmlStringRGBA(preset.color)}");
            sb.AppendLine($"<b>Flicker Amp:</b> {preset.flickerAmplitude:F2}");
            sb.AppendLine($"<b>Flicker Speed:</b> {preset.flickerSpeed:F2} Hz");
            sb.AppendLine($"<b>Center Scale:</b> {preset.centerScale:F2}");
            _propsTmp.text = sb.ToString();
        }

        // ── Mode ──

        private void SetMode(EditorMode m)
        {
            _mode = m;
            RefreshModeButtons();
        }

        private void RefreshModeButtons()
        {
            if (_selectBtnImg) _selectBtnImg.color = _mode == EditorMode.Select ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_spawnBtnImg) _spawnBtnImg.color = _mode == EditorMode.Spawn ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_deleteBtnImg) _deleteBtnImg.color = _mode == EditorMode.Delete ? EditorUIHelpers.DANGER : new Color(0.55f, 0.15f, 0.15f, 1f);
        }

        // ── Day/Night ──

        private void AdjustTime(int minutes)
        {
            _dayTimeMinutes = Mathf.Repeat(_dayTimeMinutes + minutes, 1440f);
        }

        private void JumpToTime(int minuteOfDay)
        {
            _dayTimeMinutes = minuteOfDay;
        }

        private void UpdateDayTimeDisplay()
        {
            if (_dayTimeTmp == null) return;
            int h = Mathf.FloorToInt(_dayTimeMinutes / 60f);
            int m = Mathf.FloorToInt(_dayTimeMinutes % 60f);
            string phase = GetPhase(_dayTimeMinutes);
            _dayTimeTmp.text = $"{h:D2}:{m:D2} — {phase}";
        }

        private static string GetPhase(float minutes)
        {
            if (minutes < 300) return "Night";
            if (minutes < 420) return "Dawn";
            if (minutes < 720) return "Day";
            if (minutes < 780) return "Noon";
            if (minutes < 1140) return "Day";
            if (minutes < 1260) return "Dusk";
            return "Night";
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

            // Drag in Select mode
            if (_dragging && _dragTarget != null)
            {
                _dragTarget.transform.position = worldPos + _dragOffset;
                if (mouse.leftButton.wasReleasedThisFrame)
                    _dragging = false;
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (_mode == EditorMode.Spawn && !string.IsNullOrEmpty(_selectedPresetKey))
                {
                    _statusTmp.text = $"Placed light '{_selectedPresetKey}' at ({worldPos.x:F1}, {worldPos.y:F1})";
                    if (_singleShot) _mode = EditorMode.Select;
                    RefreshModeButtons();
                    Debug.Log($"[LightingEditor] Spawn {_selectedPresetKey} at {worldPos}");
                }
                else if (_mode == EditorMode.Delete)
                {
                    var hit = Physics2D.OverlapCircle(worldPos, 0.5f);
                    if (hit != null && hit.GetComponent<Light>() != null)
                    {
                        _statusTmp.text = $"Deleted light: {hit.gameObject.name}";
                        Destroy(hit.gameObject);
                    }
                }
                else if (_mode == EditorMode.Select)
                {
                    var hit = Physics2D.OverlapCircle(worldPos, 0.5f);
                    if (hit != null && hit.GetComponent<Light>() != null)
                    {
                        _dragTarget = hit.gameObject;
                        _dragging = true;
                        _dragOffset = _dragTarget.transform.position - worldPos;
                    }
                }
            }
        }

        private void SaveInstances()
        {
            _statusTmp.text = "Saved light instances.";
            Debug.Log("[LightingEditor] Save requested.");
        }

        // ── Helpers ──

        private static void MakeToggle(Transform parent, string label, bool initial, System.Action<bool> onChange)
        {
            var row = EditorUIHelpers.CreateUI($"Toggle_{label}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childForceExpandWidth = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            EditorUIHelpers.AddLabel(row.transform, label, 11f);

            bool state = initial;
            var btnGo = EditorUIHelpers.MakeButton(row.transform, state ? "ON" : "OFF", null, 24f, 10f);
            var btnImg = btnGo.GetComponent<Image>();
            var btnTmp = btnGo.GetComponentInChildren<TextMeshProUGUI>();
            btnImg.color = state ? EditorUIHelpers.SUCCESS : EditorUIHelpers.BTN_NORMAL;

            btnGo.GetComponent<Button>().onClick.AddListener(() =>
            {
                state = !state;
                btnTmp.text = state ? "ON" : "OFF";
                btnImg.color = state ? EditorUIHelpers.SUCCESS : EditorUIHelpers.BTN_NORMAL;
                onChange?.Invoke(state);
            });
        }

        private static void MakeStepper(Transform parent, string label, int initial, int min, int max,
            System.Action<int> onChange, string suffix = "")
        {
            var row = EditorUIHelpers.CreateUI($"Step_{label}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 24f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childForceExpandWidth = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            EditorUIHelpers.AddLabel(row.transform, label, 10f);

            int val = initial;
            var valLabel = EditorUIHelpers.AddLabel(row.transform, $"{val}{suffix}", 10f);
            valLabel.alignment = TextAlignmentOptions.Center;

            EditorUIHelpers.MakeButton(row.transform, "−", () =>
            {
                val = Mathf.Max(min, val - 1);
                valLabel.text = $"{val}{suffix}";
                onChange?.Invoke(val);
            }, 22f, 10f);

            EditorUIHelpers.MakeButton(row.transform, "+", () =>
            {
                val = Mathf.Min(max, val + 1);
                valLabel.text = $"{val}{suffix}";
                onChange?.Invoke(val);
            }, 22f, 10f);
        }
    }
}