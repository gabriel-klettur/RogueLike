using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
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

        private void RefreshPropertiesPanel()
        {
            if (_ui.PropsText == null) return;

            // Show the Delete button only when a spawner is selected, so the
            // panel only exposes destructive actions in a meaningful state.
            if (_ui.DeleteFromPropsBtnGo != null)
                _ui.DeleteFromPropsBtnGo.SetActive(_selectedInstance != null);

            if (_selectedInstance == null || _selectedInstance.Template == null)
            {
                _ui.PropsText.text =
                    "<i>No spawner selected.</i>\n\n" +
                    "Pick a template and click the map to place, drag a template " +
                    "from the picker, or click on a spawner to inspect it.";
                return;
            }

            var t   = _selectedInstance.Template;
            var pos = _selectedInstance.transform.position;

            var sb = new StringBuilder();
            sb.AppendLine($"<b>Identity</b>");
            sb.AppendLine($"  ID:        {_selectedInstance.InstanceId}");
            sb.AppendLine($"  Template:  {t.templateId}");
            sb.AppendLine($"  Zone:      {_selectedInstance.Zone}");
            sb.AppendLine($"  Pos:       ({pos.x:F2}, {pos.y:F2})");
            sb.AppendLine();
            sb.AppendLine($"<b>Trigger</b>");
            sb.AppendLine($"  Type:      {t.triggerType}");
            sb.AppendLine($"  Radius:    {t.triggerRadius}");
            sb.AppendLine($"  AutoStart: {t.autoStart}");
            sb.AppendLine();
            sb.AppendLine($"<b>Policy</b>");
            sb.AppendLine($"  Mode:      {t.spawnMode}");
            sb.AppendLine($"  Cooldown:  {t.cooldownSeconds}s");
            sb.AppendLine($"  AdvanceOn: {t.advanceOn}");
            sb.AppendLine($"  MaxActive: {t.maxActive}");
            sb.AppendLine($"  Persistent:{t.persistent}");
            sb.AppendLine();
            sb.AppendLine($"<b>Waves</b>");
            sb.AppendLine($"  Count:     {t.waves?.Count ?? 0}");
            sb.AppendLine($"  WavesId:   {(string.IsNullOrEmpty(t.wavesId) ? "(inline)" : t.wavesId)}");
            sb.AppendLine();
            sb.AppendLine($"<b>Runtime</b>");
            sb.AppendLine($"  State:     {_selectedInstance.State}");
            sb.AppendLine($"  WaveIdx:   {_selectedInstance.CurrentWaveIndex}");
            sb.AppendLine($"  Active:    {_selectedInstance.ActiveEntityCount}");

            _ui.PropsText.text = sb.ToString();
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

            string id  = _selectedInstance.InstanceId;
            var    go  = _selectedInstance.gameObject;
            // Editor-mode safe destruction — same pattern Particles uses for
            // its delete-instance path so EditMode tests don't trip on
            // "Destroy may not be called from edit mode" warnings.
            if (Application.isPlaying) Destroy(go);
            else                       DestroyImmediate(go);
            _selectedInstance = null;
            // Cancel any in-progress drag tied to the now-dead instance.
            _dragging = false;
            SetStatus($"Deleted '{id}'.");
            RefreshPropertiesPanel();
        }
    }
}
