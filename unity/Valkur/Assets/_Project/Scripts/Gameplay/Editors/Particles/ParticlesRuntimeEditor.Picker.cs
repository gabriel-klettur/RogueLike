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
            int hiddenLayerOnly = 0;
            foreach (var preset in _catalog.Presets)
            {
                if (preset == null) continue;
                if (!MatchesCategoryFilter(preset)) continue;
                if (filter.Length > 0)
                {
                    string pid = (preset.id ?? "").ToLowerInvariant();
                    string nm  = (preset.displayName ?? "").ToLowerInvariant();
                    if (!pid.Contains(filter) && !nm.Contains(filter)) continue;
                }
                // Layer-only presets are sub-layers of a composite — the three pollen
                // layers under flowers_pollen_soft are the case that motivated the flag.
                // Placing the composite and then one of its own layers beside it doubles
                // that layer, and nothing in the UI says so. They get no PLACEMENT tile;
                // they keep their Table row, which is where they stay selectable and
                // editable, and they keep working as layers and inside spells.
                if (preset.layerOnly) { hiddenLayerOnly++; continue; }
                visible.Add(preset);
            }

            // Feed the visible list to the preview service so it configures emitters.
            _previewService.SetVisiblePresets(visible);

            // One GridLayoutGroup lookup per rebuild, not one per tile. The budget is a
            // property of the grid's live cell width, so it is identical for all ~133 tiles,
            // while the GetComponent behind it is not free — and this rebuild runs on every
            // keystroke in the search box.
            int labelBudget = PickerLabelBudget();
            foreach (var preset in visible)
                AddPickerSlot(preset, labelBudget);

            // A tab can legitimately end up with nothing placeable in it (every preset in
            // it is a layer). A blank grid reads as a broken editor, so say what happened
            // and where the presets went.
            if (visible.Count == 0)
            {
                AddPickerEmptyNote(hiddenLayerOnly > 0
                    ? $"Nothing placeable here.\n{hiddenLayerOnly} layer-only preset(s) hidden — " +
                      "switch to the Table view to select and edit them."
                    : "No preset matches.");
            }

            // Sync the View panel RawImage with the currently selected preset.
            if (_ui.ViewRawImage != null)
            {
                var largeTex  = _previewService.GetLargePreviewTexture();
                bool hasLarge = largeTex != null && !string.IsNullOrEmpty(_selectedPresetId);
                _ui.ViewRawImage.texture = hasLarge ? largeTex : null;
                _ui.ViewRawImage.color   = hasLarge ? Color.white : new Color(0.24f, 0.25f, 0.28f, 1f);
            }

            string scope = IsCategoryFilterActive
                ? $" in {ParticlePresetCategory.Label(ActiveCategory)}"
                : "";
            // The hidden count is deliberately reported: a grid that silently shows fewer
            // presets than the catalog holds is the same class of lie as the truncated
            // labels below.
            string hidden = hiddenLayerOnly > 0 ? $" · {hiddenLayerOnly} layer-only hidden" : "";
            SetStatus((filter.Length == 0
                ? $"{visible.Count} presets{scope}"
                : $"{visible.Count} match '{_searchFilter}'{scope}") + hidden);
        }

        /// <summary>
        /// Full-width note drawn where the tiles would be when the grid has nothing to show.
        ///
        /// <c>ignoreLayout</c> is the load-bearing part: PickerContent is a GridLayoutGroup,
        /// which would otherwise squeeze this into one 64 px cell. LayoutGroup.rectChildren
        /// skips ILayoutIgnorer children, so the note can stretch across the panel while the
        /// grid's own cell maths (GridAutoSize) stays untouched — it derives columns from
        /// the container width, never from the child count.
        /// </summary>
        private void AddPickerEmptyNote(string message)
        {
            if (_ui.PickerContent == null) return;

            var go = new GameObject("EmptyNote", typeof(RectTransform));
            go.transform.SetParent(_ui.PickerContent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -10f);
            rt.sizeDelta        = new Vector2(-16f, 56f);

            // TMP alone on the GameObject — an Image on the same object would NRE.
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text          = message;
            tmp.fontSize      = 11f;
            tmp.alignment     = TextAlignmentOptions.Top;
            tmp.color         = UITheme.TEXT_SECONDARY;
            tmp.raycastTarget = false;
        }

        /// <summary>
        /// Active category tab, meaningful only when <see cref="IsCategoryFilterActive"/>.
        /// </summary>
        private ParticlePresetCategory.Category ActiveCategory
        {
            get
            {
                return System.Enum.TryParse(_categoryFilter,
                           out ParticlePresetCategory.Category c)
                    ? c
                    : ParticlePresetCategory.Category.SpellFx;
            }
        }

        /// <summary>False on the "All" tab and before any tab has been chosen.</summary>
        private bool IsCategoryFilterActive
            => !string.IsNullOrEmpty(_categoryFilter)
               && _categoryFilter != ParticlesEditorUIBuilder.CATEGORY_ALL_KEY
               && System.Enum.TryParse(_categoryFilter, out ParticlePresetCategory.Category _);

        /// <summary>
        /// Category gate, shared by the Grid and the Table so both agree on what is visible.
        /// </summary>
        private bool MatchesCategoryFilter(ParticlePresetDefinition preset)
            => !IsCategoryFilterActive || ParticlePresetCategory.Of(preset) == ActiveCategory;

        /// <summary>
        /// Builds one tile. <paramref name="labelBudget"/> comes from the caller rather than
        /// from <see cref="PickerLabelBudget"/> here: it is the same number for every tile in
        /// a rebuild, and computing it per tile meant one GetComponent per tile.
        /// </summary>
        private void AddPickerSlot(ParticlePresetDefinition preset, int labelBudget)
        {
            string pid = preset.id ?? "";

            var (btn, _, label) = EditorUIHelpers.MakeSlotButton(
                _ui.PickerContent, preset.displayName ?? pid, 64f,
                () => SelectPreset(pid));
            label.text = TruncateName(preset.displayName ?? pid, labelBudget);

            // The budget above is arithmetic on an average glyph width; TMP knows the real
            // advances, so it makes the final cut. Word wrap must go with it — the label
            // strip is a single 16 px line, and a wrapped second line spills out of the
            // tile (TMP's default overflow draws outside the rect rather than clipping).
            label.enableWordWrapping = false;
            label.overflowMode       = TextOverflowModes.Ellipsis;

            // Slot background: dark neutral so the RenderTexture particles are readable.
            var slotImg = btn.GetComponent<Image>();
            if (slotImg != null)
                slotImg.color = new Color(0.24f, 0.25f, 0.28f, 1f);

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
                raw.color   = Color.clear;
            }

            // Selection highlight. The background tint other pickers rely on is
            // useless here — the preview RawImage covers the whole cell — so the
            // selected slot also gets a thick opaque frame drawn on top of it.
            if (pid == _selectedPresetId)
            {
                if (slotImg != null) slotImg.color = UITheme.SLOT_SELECTED;
                EditorUIHelpers.MakeSelectionBorder(btn.GetComponent<RectTransform>());
            }

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
            {
                // A layer-only preset is still reachable from the Table, so Place mode has
                // to admit what it is rather than invite the author to double a layer.
                SetStatus(def != null && def.layerOnly
                    ? $"'{pid}' is layer-only — it belongs inside another preset's layers, " +
                      "not on the map as its own instance."
                    : $"Place: click on the map to spawn '{pid}'.");
            }
        }

        private void ShowPresetProperties(string pid)
        {
            var preset = _catalog?.GetById(pid);

            // The rows themselves — every scalar the emitter reads, editable in place.
            RebuildPresetPropertyForm(pid);

            // The text label survives as the footer for what the form cannot edit yet, so
            // a field that is not offered reads as a stated limit rather than a bug.
            if (_ui.PresetPropsText != null)
            {
                if (preset == null)
                {
                    _ui.PresetPropsText.text = "Not found.";
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.Append($"<b>ID:</b> {preset.id}   <b>Type:</b> {preset.type}");
                    if (preset.layerOnly)
                        sb.Append("   <b>Layer-only</b> (no placement tile)");
                    sb.Append('\n');

                    // A gradient richer than the three rows must announce itself, or the
                    // author reads "Birth / Middle / Death" as the whole gradient and
                    // wonders why the stops between them never move.
                    var grad = preset.vfx?.colorOverLife;
                    if (grad != null && grad.Length > 3)
                    {
                        int mid = ParticlePresetFieldWriter.MidStopIndex(grad);
                        sb.Append($"Gradient has {grad.Length} keys — Birth/Middle/Death edit " +
                                  $"keys 1/{mid + 1}/{grad.Length}; the rest keep their times " +
                                  "and colours.\n");
                    }

                    // ParticlePresetFieldWriter refuses arrays and UnityEngine.Object
                    // references outright, so flipbookFrames, customSprite, the layers list
                    // and the size/alpha curves cannot have a row until a real widget backs
                    // them; gravityVector is refused one step later, in TryConvert, for want
                    // of a two-field row. Stated here, because a field that is silently
                    // absent reads as a bug.
                    sb.Append("Inspector-only for now: sprites (customSprite, flipbookFrames), " +
                              "the layers list, the size/alpha curves and the gravity vector — " +
                              "each needs a widget the form does not have.\n");

                    // The one question this panel gets asked: why did editing one emitter
                    // change all of them.
                    sb.Append("<b>Every field here belongs to the preset</b>, so an edit reaches " +
                              "every placement of it, the preview and everything placed from it " +
                              "afterwards. What belongs to ONE placement: its position, its " +
                              "scale, and the two size boxes you drag on the map.");

                    _ui.PresetPropsText.text = sb.ToString();
                    _ui.PresetPropsText.richText = true;
                }
            }
            var v = preset?.vfx;

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

            MarkParticlePresetDirty(preset);
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
                if (_ui.ReapplyInstanceBtnGo != null)
                    _ui.ReapplyInstanceBtnGo.SetActive(instance != null);

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

        /// <summary>Project-wide picker convention — Entities and Spells both cut at 9.</summary>
        private const int PICKER_LABEL_MIN_CHARS = 9;

        /// <summary>Label point size baked into UIButton.MakeSlot.</summary>
        private const float PICKER_LABEL_FONT_PT = 9f;

        /// <summary>
        /// Mean glyph advance as a fraction of the em, for title-case Latin in the default
        /// TMP face. Lowercase runs ~0.5 em and uppercase ~0.68 em; 0.55 is the mixed-case
        /// average, deliberately pessimistic so the arithmetic under-counts rather than
        /// over-counts. TMP applies the exact cut afterwards.
        /// </summary>
        private const float PICKER_LABEL_AVG_ADVANCE_EM = 0.55f;

        /// <summary>
        /// How many characters of a display name fit on one picker tile.
        ///
        /// The old fixed 8 was measured against nothing. The grid is responsive
        /// (GridAutoSize, 64–96 px cells — see ParticlesEditorUIBuilder.PresetsPanel.cs),
        /// the label spans the full cell width (MakeSlot anchors it 0→1 on x) and renders at
        /// 9 pt, so a tile affords roughly 64 / (9 × 0.55) ≈ 12 characters at the narrow end
        /// and ≈ 19 at the wide end. Eight collapsed the whole Plants tab into "Falling ",
        /// "Falling ", "Falling ", "Autumn L", "Flower P", "Flowers " ×4 — nine tiles, four
        /// of them character-for-character identical, and the "Falling Leaf (30s)" /
        /// "Falling Leaf (Canopy)" pair stays ambiguous even at the project's 9.
        ///
        /// So 9 is the FLOOR, not the value: below it we would be narrower than every other
        /// picker in the project, above it we simply spend the width the live cell has.
        ///
        /// Call this ONCE per rebuild and pass the result down: the GetComponent is the whole
        /// cost of the function, and the answer cannot differ between two tiles of one grid.
        /// </summary>
        private int PickerLabelBudget()
        {
            float cell = 64f;
            var grid = _ui.PickerContent != null
                ? _ui.PickerContent.GetComponent<GridLayoutGroup>()
                : null;
            // cellSize is a placeholder until GridAutoSize sees a real container width.
            if (grid != null && grid.cellSize.x > 1f) cell = grid.cellSize.x;

            int fits = Mathf.FloorToInt(cell / (PICKER_LABEL_FONT_PT * PICKER_LABEL_AVG_ADVANCE_EM));
            return Mathf.Max(PICKER_LABEL_MIN_CHARS, fits);
        }
    }
}
