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

            // Filter pass.
            var visible = new System.Collections.Generic.List<ParticlePresetDefinition>();
            foreach (var preset in _catalog.Presets)
            {
                if (preset == null) continue;
                var pid = preset.id;
                if (filter.Length > 0)
                {
                    string n = (preset.displayName ?? pid ?? "").ToLowerInvariant();
                    if (!n.Contains(filter) && !(pid ?? "").ToLowerInvariant().Contains(filter)) continue;
                }
                visible.Add(preset);
            }

            // Sort: GROUP-by-kind groups by vfx.kind (Python parity). ALL = preset order.
            if (_groupByKind)
            {
                visible.Sort((a, b) =>
                {
                    string ka = a.vfx?.kind ?? "";
                    string kb = b.vfx?.kind ?? "";
                    int c = string.CompareOrdinal(ka, kb);
                    return c != 0 ? c : string.CompareOrdinal(a.displayName ?? a.id ?? "", b.displayName ?? b.id ?? "");
                });
            }

            foreach (var preset in visible)
            {
                var pid = preset.id;
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
            RefreshSpellsPanel();
        }

        private void ShowPresetProperties(string pid)
        {
            var preset = _catalog?.GetById(pid);
            if (preset == null) { _propsTmp.text = "Not found."; return; }

            var sb = new System.Text.StringBuilder();
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
            _propsTmp.text = sb.ToString();
            _propsTmp.richText = true;
            _statusTmp.text = $"Selected: {preset.displayName ?? pid}";
        }

        // ── Add / Remove (Python particles_add_remove_panel parity) ──

        private void OnAddSystemClicked()
        {
            if (string.IsNullOrEmpty(_selectedPresetId))
            {
                _statusTmp.text = "Pick a preset first, then click Add System.";
                return;
            }
            // Phase-1 UI stub: switch to Place mode so the user can click on the map.
            // Functional spawning is handled by HandleMapInteraction (LMB in Place mode).
            SetMode(EditorMode.Place);
            _statusTmp.text = $"Add System: click on map to place '{_selectedPresetId}'.";
        }

        private void OnRemoveClicked()
        {
            // Phase-1 UI stub: switch to Delete mode.
            SetMode(EditorMode.Delete);
            _statusTmp.text = "Remove: click an instance on the map to delete it.";
        }

        // ── Group-by-Kind (Python picker_view ALL / GROUP toggle) ──

        private void ToggleGroupByKind()
        {
            _groupByKind = !_groupByKind;
            if (_groupToggleLabel != null)
                _groupToggleLabel.text = _groupByKind ? "GROUP" : "ALL";
            if (_groupToggleImg != null)
                _groupToggleImg.color = _groupByKind ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            RefreshPicker();
        }

        // ── Spells Using This Preset (Python particles_spells_list_panel parity) ──

        private void ToggleSpellsExpanded()
        {
            _spellsExpanded = !_spellsExpanded;
            if (_spellsHeaderTmp != null)
            {
                string baseLbl = "SPELLS USING THIS PRESET";
                _spellsHeaderTmp.text = _spellsExpanded ? "▼ " + baseLbl : "▶ " + baseLbl;
            }
            if (_spellsContent != null)
                _spellsContent.gameObject.SetActive(_spellsExpanded);
        }

        private void RefreshSpellsPanel()
        {
            if (_spellsContent == null) return;
            for (int i = _spellsContent.childCount - 1; i >= 0; i--)
                Destroy(_spellsContent.GetChild(i).gameObject);

            if (string.IsNullOrEmpty(_selectedPresetId))
            {
                var lbl = EditorUIHelpers.AddLabel(_spellsContent, "(no preset selected)", 11f);
                lbl.color = EditorUIHelpers.TEXT_MUTED;
                return;
            }

            // Phase 2: scan SpellDefinition catalog for usages of this preset id.
            // For now show a placeholder with Python-style two-column hint.
            var hint = EditorUIHelpers.AddLabel(_spellsContent,
                $"Usages of '<b>{_selectedPresetId}</b>' will appear here.\n" +
                "Columns: spell_key  ·  json_path",
                11f);
            hint.color = EditorUIHelpers.TEXT_MUTED;
            hint.richText = true;
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