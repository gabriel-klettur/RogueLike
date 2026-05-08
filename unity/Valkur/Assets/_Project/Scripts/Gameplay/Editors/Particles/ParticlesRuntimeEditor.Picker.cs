using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Picker grid ─────────────────────────────────────────────────────────

        private void RefreshPicker()
        {
            if (_ui.PickerContent == null) return;
            for (int i = _ui.PickerContent.childCount - 1; i >= 0; i--)
            {
                var child = _ui.PickerContent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }

            if (_catalog == null)
            {
                SetStatus("No ParticlePresetCatalog assigned.");
                return;
            }

            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";

            var visible = new List<ParticlePresetDefinition>();
            foreach (var preset in _catalog.Presets)
            {
                if (preset == null) continue;
                if (filter.Length > 0)
                {
                    string pid = (preset.id ?? "").ToLowerInvariant();
                    string nm  = (preset.displayName ?? "").ToLowerInvariant();
                    if (!pid.Contains(filter) && !nm.Contains(filter)) continue;
                }
                visible.Add(preset);
            }

            // Feed the visible list to the preview service so it configures emitters.
            _previewService.SetVisiblePresets(visible);

            foreach (var preset in visible)
                AddPickerSlot(preset);

            // Sync the View panel RawImage with the currently selected preset.
            if (_ui.ViewRawImage != null)
            {
                var largeTex  = _previewService.GetLargePreviewTexture();
                bool hasLarge = largeTex != null && !string.IsNullOrEmpty(_selectedPresetId);
                _ui.ViewRawImage.texture = hasLarge ? largeTex : null;
                _ui.ViewRawImage.color   = hasLarge ? Color.white : new Color(0.08f, 0.08f, 0.10f, 1f);
            }

            SetStatus(filter.Length == 0
                ? $"{visible.Count} presets"
                : $"{visible.Count} match '{_searchFilter}'");
        }

        private void AddPickerSlot(ParticlePresetDefinition preset)
        {
            string pid = preset.id ?? "";

            var (btn, _, label) = EditorUIHelpers.MakeSlotButton(
                _ui.PickerContent, preset.displayName ?? pid, 64f,
                () => SelectPreset(pid));
            label.text = TruncateName(preset.displayName ?? pid, 8);

            // Slot background: dark neutral so the RenderTexture particles are readable.
            var slotImg = btn.GetComponent<Image>();
            if (slotImg != null)
                slotImg.color = new Color(0.08f, 0.08f, 0.10f, 1f);

            // RenderTexture thumbnail: live animated particle preview.
            var rawGo = new GameObject("PreviewRT", typeof(RectTransform));
            rawGo.transform.SetParent(btn.transform, false);
            var rawRt = rawGo.GetComponent<RectTransform>();
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = new Vector2(2f, 18f);  // leave room for label at bottom
            rawRt.offsetMax = new Vector2(-2f, -2f);
            var raw = rawGo.AddComponent<RawImage>();
            raw.raycastTarget = false;

            var rt = _previewService.GetPreviewTexture(pid);
            if (rt != null)
            {
                raw.texture = rt;
                raw.color   = Color.white;
            }
            else
            {
                // Service not ready yet: show dark bg, texture will be assigned on next RefreshPicker.
                raw.texture = null;
                raw.color   = new Color(0f, 0f, 0f, 0f);
            }

            // Selection highlight via existing outline system (slot image tint).
            if (slotImg != null && pid == _selectedPresetId)
                slotImg.color = UITheme.SLOT_SELECTED;

            // EventTrigger: register pointer-down so the picker drag system can
            // start tracking before Button.onClick fires (Entities/Buildings parity).
            var trig = btn.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            entry.callback.AddListener(_ => OnPickerSlotPointerDown(pid));
            trig.triggers.Add(entry);
        }

        private void SelectPreset(string pid)
        {
            _selectedPresetId = pid;

            // Reset zoom whenever the user picks a different preset so they don't end up
            // at 4x on a newly selected effect.
            _previewService.ResetZoom();

            // Notify preview service so the large preview RT starts rendering.
            var def = _catalog?.GetById(pid);
            _previewService.SetSelectedPreset(pid, def);

            // Update View panel RT and name label immediately.
            RefreshViewPanel();

            RefreshPicker();
            RefreshTable();
            ShowPresetProperties(pid);
            RefreshSpellsPanel();
            RebuildSamePresetFx();
            if (_mode == EditorMode.Place && !string.IsNullOrEmpty(pid))
                SetStatus($"Place: click on the map to spawn '{pid}'.");
        }

        private void ShowPresetProperties(string pid)
        {
            if (_ui.PresetPropsText == null) return;
            var preset = _catalog?.GetById(pid);
            if (preset == null) { _ui.PresetPropsText.text = "Not found."; return; }

            var sb = new StringBuilder();
            sb.AppendLine($"<b>ID:</b> {preset.id}");
            sb.AppendLine($"<b>Name:</b> {preset.displayName}");
            sb.AppendLine($"<b>Type:</b> {preset.type}");
            var v = preset.vfx;
            if (v != null)
            {
                sb.AppendLine();
                sb.AppendLine($"<b>Kind:</b> {v.kind}");
                sb.AppendLine($"<b>Emit Rate:</b> {v.emitRate:F1}/s");
                sb.AppendLine($"<b>Burst Count:</b> {v.count}");
                sb.AppendLine($"<b>Lifespan:</b> {v.lifespan:F2}s");
                sb.AppendLine($"<b>Speed:</b> {v.speed:F2} u/s");
                sb.AppendLine($"<b>Gravity:</b> {v.gravity:F2}");
                sb.AppendLine($"<b>Drag:</b> {v.drag:F2}");
                sb.AppendLine($"<b>Size:</b> {v.sizeMin:F2} – {v.sizeMax:F2}");
                sb.AppendLine($"<b>Radius:</b> {v.radius:F2}");
                sb.AppendLine($"<b>Additive:</b> {v.additive}");
            }
            _ui.PresetPropsText.text = sb.ToString();
            _ui.PresetPropsText.richText = true;

            // Sync the Loops toggle with the preset's current value.
            // We disable the callback temporarily to avoid re-triggering on programmatic set.
            if (_ui.LoopsToggle != null)
            {
                _ui.LoopsToggle.onValueChanged.RemoveListener(OnLoopsToggled);
                _ui.LoopsToggle.isOn = v != null && v.loops;
                _ui.LoopsToggle.onValueChanged.AddListener(OnLoopsToggled);
                _ui.LoopsToggle.interactable = preset != null;
            }
        }

        private void OnLoopsToggled(bool value)
        {
            if (string.IsNullOrEmpty(_selectedPresetId) || _catalog == null) return;
            var preset = _catalog.GetById(_selectedPresetId);
            if (preset?.vfx == null) return;

            preset.vfx.loops = value;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(preset);
#endif
            // Refresh properties text to reflect the new state.
            ShowPresetProperties(_selectedPresetId);
            SetStatus($"'{_selectedPresetId}' loops = {value}.");
        }

        private void ShowInstanceProperties(GameObject instance)
        {
            if (_ui.InstancePropsText == null) return;

            // Show/hide the Delete Instance button depending on selection.
            if (_ui.DeleteInstanceBtnGo != null)
                _ui.DeleteInstanceBtnGo.SetActive(instance != null);

            if (instance == null)
            {
                _ui.InstancePropsText.text = "Select an instance on the map.";
                _ui.InstancePropsText.color = UITheme.TEXT_SECONDARY;
                return;
            }
            var pos = instance.transform.position;
            var sb = new StringBuilder();
            sb.AppendLine($"<b>Name:</b> {instance.name}");
            sb.AppendLine($"<b>Position:</b> ({pos.x:F2}, {pos.y:F2})");
            string presetId = GetPresetIdFromGo(instance);
            if (!string.IsNullOrEmpty(presetId))
                sb.AppendLine($"<b>Preset:</b> {presetId}");
            _ui.InstancePropsText.text = sb.ToString();
            _ui.InstancePropsText.richText = true;
            _ui.InstancePropsText.color = UITheme.TEXT_PRIMARY;
        }

        // Spawned emitters are named "PE_<preset_id>_<inst_id>" by ParticleInstancesLoader.
        // Pull the preset id back out so the inspector can label the selection.
        private static string ExtractPresetIdFromName(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("PE_")) return null;
            int last = name.LastIndexOf('_');
            if (last <= 3) return null;
            return name.Substring(3, last - 3);
        }

        private static string TruncateName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= max ? name : name.Substring(0, max - 1) + "…";
        }
    }
}
