using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Entities;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// SpawnerEditor — picker population + properties refresh.
    /// </summary>
    public partial class SpawnerEditorManager
    {
        // ── Picker (template list) ───────────────────────────────────────────────

        private void RefreshPicker()
        {
            if (_ui.PickerContent == null) return;

            ResolveCatalogFallback();

            foreach (var row in _pickerRows)
                if (row != null) Destroy(row);
            _pickerRows.Clear();

            if (_catalog == null)
            {
                if (_ui.StatusText != null) _ui.StatusText.text = "No catalog assigned.";
                return;
            }

            string filter = _searchFilter?.Trim().ToLowerInvariant();
            int shown = 0;

            foreach (var tmpl in _catalog.Templates)
            {
                if (tmpl == null) continue;
                if (!string.IsNullOrEmpty(filter) &&
                    (tmpl.templateId == null ||
                     tmpl.templateId.ToLowerInvariant().IndexOf(filter, System.StringComparison.Ordinal) < 0))
                    continue;

                _pickerRows.Add(BuildPickerRow(tmpl, _ui.PickerContent));
                shown++;
            }

            if (_ui.StatusText != null)
                _ui.StatusText.text = shown == 0
                    ? "No templates match filter."
                    : $"{shown} template(s) — pick one to enter Place mode.";
        }

        private GameObject BuildPickerRow(SpawnerTemplateData tmpl, Transform parent)
        {
            var go = new GameObject($"Row_{tmpl.templateId}", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 38f;

            bool isSelected = _selectedTemplate == tmpl;
            var img = go.AddComponent<Image>();
            img.color = isSelected ? UITheme.SLOT_SELECTED : UITheme.SLOT_BG;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = isSelected ? UITheme.SLOT_SELECTED : UITheme.SLOT_BG;
            c.highlightedColor = UITheme.SLOT_HOVER;
            c.pressedColor     = UITheme.SLOT_SELECTED;
            btn.colors         = c;
            btn.targetGraphic  = img;
            var captured = tmpl;
            btn.onClick.AddListener(() => OnPickTemplate(captured));

            // Drag-from-picker (Entities/Buildings parity): LMB-press on the slot
            // arms the drag; if the cursor moves past the threshold the row turns
            // into a floating ghost and releasing over the map places the spawner.
            var et  = go.AddComponent<EventTrigger>();
            var pde = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            pde.callback.AddListener(_ => OnPickerSlotPointerDown(captured));
            et.triggers.Add(pde);

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding                = new RectOffset(8, 8, 4, 4);
            vlg.spacing                = 0f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            vlg.childAlignment         = TextAnchor.MiddleLeft;

            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(go.transform, worldPositionStays: false);
            titleGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var titleTmp       = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text      = tmpl.templateId;
            titleTmp.fontSize  = 12f;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color     = UITheme.TEXT_PRIMARY;
            titleTmp.alignment = TextAlignmentOptions.MidlineLeft;
            titleTmp.enableWordWrapping = false;
            titleTmp.overflowMode       = TextOverflowModes.Truncate;

            var subGo = new GameObject("Sub", typeof(RectTransform));
            subGo.transform.SetParent(go.transform, worldPositionStays: false);
            subGo.AddComponent<LayoutElement>().preferredHeight = 12f;
            var subTmp       = subGo.AddComponent<TextMeshProUGUI>();
            subTmp.text      = $"{tmpl.spawnerType} · {tmpl.triggerType} · {tmpl.waves?.Count ?? 0}w";
            subTmp.fontSize  = 9f;
            subTmp.color     = UITheme.TEXT_MUTED;
            subTmp.alignment = TextAlignmentOptions.MidlineLeft;
            subTmp.enableWordWrapping = false;
            subTmp.overflowMode       = TextOverflowModes.Truncate;

            return go;
        }

        private void OnPickTemplate(SpawnerTemplateData tmpl)
        {
            _selectedTemplate = tmpl;
            _mode = EditorMode.Place;
            RefreshPicker(); // re-tint selected row
            SetStatus($"Place mode — click on map to place '{tmpl.templateId}'.");
        }

        // ── Properties panel ────────────────────────────────────────────────────
        //
        // Rebuilt from scratch on every selection change (same "clear the section,
        // repopulate" pattern EntitiesRuntimeEditor.ShowMonsterProperties uses for F5) rather
        // than diffed in place — a spawner-template form has few enough rows that a full
        // rebuild is imperceptible, and it keeps the code from tracking which row belongs to
        // which field across refreshes.
        //
        // Editing one of the numeric rows below writes straight onto the SHARED
        // SpawnerTemplateData asset, not onto the selected instance: unlike a MonsterDefinition
        // edit in F5 (which must be re-applied onto every already-alive monster because Health
        // snapshots stats at spawn time), every live SpawnerInstance holds a direct reference to
        // this same asset and reads its fields fresh every Update — so a change here reaches
        // every OTHER placed instance of the template immediately too, no re-apply pass needed.
        // That is "one key, every instance" — the same semantics F5 uses for monster stats.

        private void RefreshPropertiesPanel()
        {
            if (_ui.PropsFormRoot == null) return;

            // Show the Delete button only when a spawner is selected, so the
            // panel only exposes destructive actions in a meaningful state.
            if (_ui.DeleteFromPropsBtnGo != null)
                _ui.DeleteFromPropsBtnGo.SetActive(_selectedInstance != null);

            EntitiesEditorUIBuilder.ClearSection(_ui.PropsFormRoot);

            if (_selectedInstance == null || _selectedInstance.Template == null)
            {
                EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "—",
                    "No spawner selected. Pick a template and click the map to place, drag a " +
                    "template from the picker, or click on a spawner to inspect it.");
                return;
            }

            var t   = _selectedInstance.Template;
            var pos = _selectedInstance.transform.position;

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "ID",       _selectedInstance.InstanceId);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "Template", t.templateId);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "Zone",     _selectedInstance.Zone);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "Pos",      $"({pos.x:F2}, {pos.y:F2})");

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "Trigger",   t.triggerType.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "AutoStart", t.autoStart ? "yes" : "no");
            AddFloatStat(_ui.PropsFormRoot, "Trigger Radius", t.triggerRadius, 0f,
                v => t.triggerRadius = v, t);

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "Mode", t.spawnMode.ToString());
            AddFloatStat(_ui.PropsFormRoot, "Cooldown (s)", t.cooldownSeconds, 0f,
                v => t.cooldownSeconds = v, t);
            AddFloatStat(_ui.PropsFormRoot, "Between Waves (s)", t.betweenWavesCooldownSeconds, 0f,
                v => t.betweenWavesCooldownSeconds = v, t);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "Advance On", t.advanceOn.ToString());
            AddIntStat(_ui.PropsFormRoot, "Max Active (0=∞)", t.maxActive, 0,
                v => t.maxActive = v, t);
            AddFloatStat(_ui.PropsFormRoot, "Restart CD (s)", t.restartCooldownSeconds, 0f,
                v => t.restartCooldownSeconds = v, t);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "Persistent",
                t.persistent ? "yes (exempt from despawn)" : "no");

            AddIntStat(_ui.PropsFormRoot, "Spawn Radius (0=∞)", t.spawnRadius, 0,
                v => t.spawnRadius = v, t);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "Spawn Shape", t.spawnerShape.ToString());

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "Waves", (t.waves?.Count ?? 0).ToString());

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "State",    _selectedInstance.State.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "Wave Idx", _selectedInstance.CurrentWaveIndex.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsFormRoot, "Active",   _selectedInstance.ActiveEntityCount.ToString());
        }

        /// <summary>
        /// A float row on the shared template — parses, floors to <paramref name="min"/>,
        /// writes via <paramref name="apply"/>, then <see cref="CommitTemplateEdit"/>.
        /// Rejects unparsable input without touching the field (matches
        /// EntitiesRuntimeEditor.AddFloatStat's contract: a bad string never silently zeroes
        /// a value).
        /// </summary>
        private void AddFloatStat(RectTransform section, string label, float current, float min,
                                  System.Action<float> apply, SpawnerTemplateData template)
        {
            EntitiesEditorUIBuilder.AddEditableRow(section, label, current.ToString("0.###"), raw =>
            {
                if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    SetStatus($"'{raw}' is not a number — {label} unchanged.");
                    RefreshPropertiesPanel();
                    return;
                }
                apply(Mathf.Max(min, parsed));
                CommitTemplateEdit(template, label);
            });
        }

        /// <summary>Integer counterpart of <see cref="AddFloatStat"/>.</summary>
        private void AddIntStat(RectTransform section, string label, int current, int min,
                                System.Action<int> apply, SpawnerTemplateData template)
        {
            EntitiesEditorUIBuilder.AddEditableRow(section, label, current.ToString(), raw =>
            {
                if (!int.TryParse(raw, out int parsed))
                {
                    SetStatus($"'{raw}' is not a whole number — {label} unchanged.");
                    RefreshPropertiesPanel();
                    return;
                }
                apply(Mathf.Max(min, parsed));
                CommitTemplateEdit(template, label);
            }, TMP_InputField.ContentType.IntegerNumber);
        }

        /// <summary>
        /// Marks the shared template dirty (Editor-only) and refreshes the panel. No
        /// "re-apply to every live instance" pass is needed here — see the class-level
        /// remark above <see cref="RefreshPropertiesPanel"/> for why that differs from F5.
        /// </summary>
        private void CommitTemplateEdit(SpawnerTemplateData template, string label)
        {
            if (template == null) return;
#if UNITY_EDITOR
            // SetDirty alone, never Undo.RecordObject — a bulk/repeated editor that records
            // to the GLOBAL undo stack is what silently reverted 193 building templates in
            // memory the first time anything else popped it. See BuildingPropImporter incident.
            UnityEditor.EditorUtility.SetDirty(template);
#endif
            SetStatus($"{label} updated on '{template.templateId}' — affects every placed " +
                      "instance of this template.");
            RefreshPropertiesPanel();
        }

        // ── Delete from Properties (replaces the legacy Delete mode) ────────────

        /// <summary>
        /// Destroys the currently selected spawner and refreshes the panel.
        /// Wired to the Properties panel "Delete spawner" button (visible
        /// only when a spawner is selected). Internal so EditMode tests can
        /// drive it directly without simulating a Button.onClick event.
        /// </summary>
        internal void DeleteSelectedInstance()
        {
            if (_selectedInstance == null)
            {
                SetStatus("No spawner selected to delete.");
                return;
            }

            string id = _selectedInstance.InstanceId;
            // Edit-mode-safe destruction — branches on Application.isPlaying
            // internally so this code path works at runtime AND in EditMode tests
            // without tripping "Destroy may not be called from edit mode".
            SafeDestroy.Of(_selectedInstance.gameObject);
            _selectedInstance = null;
            // Cancel any in-progress drag tied to the now-dead instance.
            _dragging = false;
            MarkInstancesDirty();
            SetStatus($"Deleted '{id}'.");
            RefreshPropertiesPanel();
        }
    }
}
