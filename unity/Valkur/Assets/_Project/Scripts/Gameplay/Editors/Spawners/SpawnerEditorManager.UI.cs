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
            // WHAT does this preset spawn, and how much of it. The old subtitle led with
            // `spawnerType`, one of the twelve fields no runtime code read, and never named
            // the roster — so the picker described every preset by a value that meant nothing
            // and omitted the only thing an author picks on.
            subTmp.text      = DescribePreset(tmpl);
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

        // ── Preset description (picker subtitle + properties header) ────────────

        /// <summary>
        /// One line saying what a preset actually produces. Shared by the picker row and the
        /// properties header so the two can never describe the same thing differently.
        /// </summary>
        private static string DescribePreset(SpawnerTemplateData preset)
        {
            if (preset == null) return "—";
            return DescribeRoster(preset.waves, preset.triggerType);
        }

        private static string DescribeRoster(System.Collections.Generic.List<WaveDefinition> waves,
                                             TriggerType trigger)
        {
            int waveCount = waves?.Count ?? 0;
            string primary = null;
            int total = 0;

            if (waves != null)
            {
                foreach (var wave in waves)
                {
                    if (wave?.spawns == null) continue;
                    foreach (var entry in wave.spawns)
                    {
                        if (entry == null) continue;
                        total += Mathf.Max(0, entry.count);
                        if (primary == null && !string.IsNullOrEmpty(entry.entityId))
                            primary = entry.entityId;
                    }
                }
            }

            string what = primary ?? "empty";
            if (total > 1) what += $" x{total}";
            return $"{what} · {trigger} · {waveCount}w";
        }

        // ── Properties panel ────────────────────────────────────────────────────
        //
        // Rebuilt from scratch on every selection change (same "clear the section,
        // repopulate" pattern EntitiesRuntimeEditor.ShowMonsterProperties uses) rather than
        // diffed in place — the form has few enough rows that a full rebuild is imperceptible,
        // and it keeps the code from tracking which row belongs to which field across
        // refreshes.
        //
        // WHAT IT EDITS IS THE SELECTED PLACEMENT, never the preset. Every row used to write
        // straight onto the shared SpawnerTemplateData, so tuning the spawner you had just
        // placed retuned every other placement of its kind — and because a ScriptableObject
        // edited in Play Mode survives until the next domain reload, the change followed the
        // author back into the Editor and rewrote shipped balance. Copy-on-place
        // (SpawnerInstanceConfig) is what makes per-placement editing possible at all; the
        // old coupling is still reachable, deliberately, through the two Reapply actions.
        //
        // The first row SAYS which scope is being edited. Without that line the two modes
        // (a placement selected, or nothing selected and the preset in scope) are
        // indistinguishable on screen, and the old defect is one click away from returning.

        private void RefreshPropertiesPanel()
        {
            if (_ui.PropsFormRoot == null) return;

            if (_ui.DeleteFromPropsBtnGo != null)
                _ui.DeleteFromPropsBtnGo.SetActive(_selectedInstance != null);

            EntitiesEditorUIBuilder.ClearSection(_ui.PropsFormRoot);

            if (_selectedInstance == null)
            {
                BuildPresetScopeForm();
                return;
            }

            BuildInstanceForm(_selectedInstance);
        }

        /// <summary>
        /// What the panel shows with nothing selected: the preset currently armed in the
        /// picker, editable, so authoring a starting point is still reachable from inside the
        /// game. A preset edit is loud about its blast radius because it genuinely has one.
        /// </summary>
        private void BuildPresetScopeForm()
        {
            var root = _ui.PropsFormRoot;

            if (_selectedTemplate == null)
            {
                EntitiesEditorUIBuilder.AddPropertyRow(root, "Scope", "Nothing selected");
                EntitiesEditorUIBuilder.AddPropertyRow(root, "—",
                    "Pick a preset to edit its defaults, drag one onto the map to place, or " +
                    "click a placed spawner to edit that one on its own.");
                return;
            }

            EntitiesEditorUIBuilder.AddPropertyRow(root, "Scope",
                $"PRESET '{_selectedTemplate.templateId}' — edits reach the NEXT placement only");
            EntitiesEditorUIBuilder.AddPropertyRow(root, "Spawns", DescribePreset(_selectedTemplate));
            EntitiesEditorUIBuilder.AddPropertyRow(root, "—",
                "Placed spawners own their own copy and are unaffected. Use a placement's " +
                "'Reapply preset' to pull these values into one.");
        }

        /// <summary>
        /// The real form: every field of THIS placement's <see cref="SpawnerInstanceConfig"/>.
        /// </summary>
        private void BuildInstanceForm(SpawnerInstance instance)
        {
            var root = _ui.PropsFormRoot;
            var c    = instance.Config;
            var pos  = instance.transform.position;

            EntitiesEditorUIBuilder.AddPropertyRow(root, "Scope",
                $"THIS spawner '{instance.InstanceId}'");
            EntitiesEditorUIBuilder.AddPropertyRow(root, "From preset",
                instance.Preset != null ? instance.Preset.templateId : "(preset deleted)");
            EntitiesEditorUIBuilder.AddPropertyRow(root, "Zone", instance.Zone);
            EntitiesEditorUIBuilder.AddPropertyRow(root, "Pos", $"({pos.x:F2}, {pos.y:F2})");

            // ── Roster ──────────────────────────────────────────────────────────
            EntitiesEditorUIBuilder.AddPropertyRow(root, "ROSTER", DescribeRoster(c.waves, c.triggerType));
            BuildRosterRows(root, instance, c);

            // ── Trigger ─────────────────────────────────────────────────────────
            AddEnumRow(root, "Trigger", c.triggerType, v => c.triggerType = v, instance);
            AddFloatRow(root, "Trigger Radius", c.triggerRadius, 0f, v => c.triggerRadius = v, instance);
            AddBoolRow(root, "Auto Start", c.autoStart, v => c.autoStart = v, instance);
            AddBoolRow(root, "Re-arm on exit", c.proximityRearms, v => c.proximityRearms = v, instance);

            // ── Policy ──────────────────────────────────────────────────────────
            AddEnumRow(root, "Mode", c.spawnMode, v => c.spawnMode = v, instance);
            AddFloatRow(root, "Cooldown (s)", c.cooldownSeconds, 0f, v => c.cooldownSeconds = v, instance);
            AddFloatRow(root, "Between Waves (s)", c.betweenWavesCooldownSeconds, 0f,
                        v => c.betweenWavesCooldownSeconds = v, instance);
            AddEnumRow(root, "Advance On", c.advanceOn, v => c.advanceOn = v, instance);
            AddIntRow(root, "Max Active (0=inf)", c.maxActive, 0, v => c.maxActive = v, instance);
            AddBoolRow(root, "Restart on done", c.restartOnDone, v => c.restartOnDone = v, instance);
            AddFloatRow(root, "Restart CD (s)", c.restartCooldownSeconds, 0f,
                        v => c.restartCooldownSeconds = v, instance);
            AddBoolRow(root, "Persistent", c.persistent, v => c.persistent = v, instance);

            // ── Area ────────────────────────────────────────────────────────────
            AddIntRow(root, "Spawn Radius (0=inf)", c.spawnRadius, 0, v => c.spawnRadius = v, instance);
            AddEnumRow(root, "Spawn Shape", c.spawnerShape, v => c.spawnerShape = v, instance);

            // ── Difficulty ──────────────────────────────────────────────────────
            // Live since encounter difficulty shipped and absent from this panel until now,
            // which made the two knobs that decide how hard a camp is the only ones an author
            // had to leave Play Mode to touch.
            AddIntRow(root, "Level Bonus", c.levelBonus, 0, v => c.levelBonus = v, instance);
            AddFloatRow(root, "Scale w/ Player", c.scaleWithPlayerLevel, 0f,
                        v => c.scaleWithPlayerLevel = Mathf.Clamp01(v), instance);

            // ── Brain ───────────────────────────────────────────────────────────
            AddFloatRow(root, "Defend Leash", c.defendLeashRadius, 0f,
                        v => c.defendLeashRadius = v, instance);
            AddTextRow(root, "FSM Set", c.fsmSetOverride, v => c.fsmSetOverride = v ?? string.Empty,
                       instance);

            // ── Live state (read-only) ──────────────────────────────────────────
            EntitiesEditorUIBuilder.AddPropertyRow(root, "State", instance.State.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(root, "Wave Idx", instance.CurrentWaveIndex.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(root, "Active", instance.ActiveEntityCount.ToString());

            BuildReapplyRows(root, instance);
        }

        // ── Roster editing ──────────────────────────────────────────────────────

        /// <summary>
        /// One row per wave entry: which entity, how many, how far apart, and a remove button;
        /// then an "add" row backed by the monster catalogue.
        ///
        /// <para>The entity is picked from a DROPDOWN over the shipped catalogue, never typed.
        /// <c>entityId</c> is a string and nothing validated it, so a typo produced one warning
        /// at spawn time and a camp that never filled — the failure is invisible until someone
        /// walks to that clearing. A picker makes the illegal value unrepresentable, which is
        /// the only fix that also covers the author who never reads the console.</para>
        /// </summary>
        private void BuildRosterRows(RectTransform root, SpawnerInstance instance,
                                     SpawnerInstanceConfig c)
        {
            if (c.waves == null) c.waves = new System.Collections.Generic.List<WaveDefinition>();

            var keys = ResolveMonsterKeys();

            for (int w = 0; w < c.waves.Count; w++)
            {
                var wave = c.waves[w];
                if (wave?.spawns == null) continue;

                for (int e = 0; e < wave.spawns.Count; e++)
                {
                    var entry = wave.spawns[e];
                    if (entry == null) continue;

                    int waveIdx = w, entryIdx = e;
                    SpawnerEditorUIBuilder.AddRosterRow(
                        root, c.waves.Count > 1 ? $"w{w}" : "•", keys, entry,
                        onEntityChanged: id => { entry.entityId = id; CommitInstanceEdit(instance, "Entity"); },
                        onCountChanged:  raw => CommitParsedInt(raw, "Count", 1, v =>
                                             entry.count = Mathf.Max(1, v), instance),
                        onSpreadChanged: raw => CommitParsedFloat(raw, "Spread", 0f, v =>
                                             entry.spreadRadius = v, instance),
                        onRemove: () => RemoveRosterEntry(instance, c, waveIdx, entryIdx));
                }
            }

            if (keys.Count > 0)
                SpawnerEditorUIBuilder.AddRosterAddRow(root, keys,
                    onAdd: id => AddRosterEntry(instance, c, id));
            else
                EntitiesEditorUIBuilder.AddPropertyRow(root, "Add",
                    "No monster catalog available — cannot add entries.");
        }

        private void AddRosterEntry(SpawnerInstance instance, SpawnerInstanceConfig c, string entityId)
        {
            if (string.IsNullOrEmpty(entityId)) return;
            if (c.waves.Count == 0) c.waves.Add(new WaveDefinition());

            // Appended to the LAST wave, which is what "add one more to this camp" means for
            // the single-wave presets that are almost all of them. A multi-wave spawner is a
            // sequence, and inserting into the middle of one from a single button would be a
            // guess about which wave was meant.
            c.waves[c.waves.Count - 1].spawns.Add(new WaveSpawnEntry { entityId = entityId });
            CommitInstanceEdit(instance, $"Added '{entityId}'");
        }

        private void RemoveRosterEntry(SpawnerInstance instance, SpawnerInstanceConfig c,
                                       int waveIdx, int entryIdx)
        {
            if (waveIdx < 0 || waveIdx >= c.waves.Count) return;
            var wave = c.waves[waveIdx];
            if (wave?.spawns == null || entryIdx < 0 || entryIdx >= wave.spawns.Count) return;

            wave.spawns.RemoveAt(entryIdx);

            // An empty wave is dropped rather than kept, unless it is the only one: a wave
            // with no entries is a tick of betweenWavesCooldown that produces nothing, which
            // reads on screen as the spawner having stalled.
            if (wave.spawns.Count == 0 && c.waves.Count > 1) c.waves.RemoveAt(waveIdx);

            CommitInstanceEdit(instance, "Removed entry");
        }

        /// <summary>
        /// The monster keys the roster picker offers. Falls back to an empty list rather than
        /// to free text, because a picker that silently degrades to "type anything" is the
        /// unvalidated field this replaced.
        /// </summary>
        private System.Collections.Generic.List<string> ResolveMonsterKeys()
        {
            var keys = new System.Collections.Generic.List<string>();
            ResolveMonsterCatalogFallback();
            if (_monsterCatalog?.Definitions == null) return keys;

            foreach (var def in _monsterCatalog.Definitions)
                if (def != null && !string.IsNullOrEmpty(def.monsterKey)) keys.Add(def.monsterKey);

            keys.Sort(System.StringComparer.OrdinalIgnoreCase);
            return keys;
        }

        // ── Reapply preset ──────────────────────────────────────────────────────

        /// <summary>
        /// The deliberate way back to the old coupling. Copy-on-place makes a global retune
        /// impossible by accident; removing it entirely would make it impossible on purpose,
        /// which is a different mistake. Same pair the Particles editor offers.
        /// </summary>
        private void BuildReapplyRows(RectTransform root, SpawnerInstance instance)
        {
            if (instance.Preset == null) return;

            SpawnerEditorUIBuilder.AddReapplyRows(root,
                onReapplyThis: () => ReapplyPreset(instance, allPlacements: false),
                onReapplyAll:  () => ReapplyPreset(instance, allPlacements: true));
        }

        private void ReapplyPreset(SpawnerInstance instance, bool allPlacements)
        {
            var preset = instance.Preset;
            if (preset == null) return;

            var targets = new System.Collections.Generic.List<SpawnerInstance>();
            if (allPlacements)
            {
                foreach (var si in FindObjectsOfType<SpawnerInstance>())
                    if (si != null && si.Preset == preset) targets.Add(si);
            }
            else
            {
                targets.Add(instance);
            }

            // Captured BEFORE the overwrite so undo restores each placement's own values —
            // one shared "before" snapshot would give every target the first one's config.
            var before = new System.Collections.Generic.List<SpawnerInstanceConfig>(targets.Count);
            foreach (var si in targets) before.Add(si.Config.Clone());

            void Apply()
            {
                foreach (var si in targets)
                    if (si != null) si.ApplyConfig(SpawnerInstanceConfig.SnapshotOf(preset));
            }

            void Undo()
            {
                for (int i = 0; i < targets.Count; i++)
                    if (targets[i] != null) targets[i].ApplyConfig(before[i].Clone());
            }

            Apply();
            _undo.Record(new UndoStack.LambdaCommand(
                $"Reapply '{preset.templateId}' to {targets.Count}", Apply, Undo));

            MarkInstancesDirty();
            SetStatus($"Reapplied preset '{preset.templateId}' to {targets.Count} placement(s).");
            RefreshPropertiesPanel();
        }

        // ── Row helpers, all writing the INSTANCE ───────────────────────────────

        private void AddFloatRow(RectTransform section, string label, float current, float min,
                                 System.Action<float> apply, SpawnerInstance instance)
        {
            EntitiesEditorUIBuilder.AddEditableRow(section, label,
                current.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                raw => CommitParsedFloat(raw, label, min, apply, instance));
        }

        private void AddIntRow(RectTransform section, string label, int current, int min,
                               System.Action<int> apply, SpawnerInstance instance)
        {
            EntitiesEditorUIBuilder.AddEditableRow(section, label, current.ToString(),
                raw => CommitParsedInt(raw, label, min, apply, instance),
                TMP_InputField.ContentType.IntegerNumber);
        }

        private void AddTextRow(RectTransform section, string label, string current,
                                System.Action<string> apply, SpawnerInstance instance)
        {
            EntitiesEditorUIBuilder.AddEditableRow(section, label, current ?? string.Empty,
                raw => { apply(raw?.Trim()); CommitInstanceEdit(instance, label); },
                TMP_InputField.ContentType.Standard);
        }

        private void AddBoolRow(RectTransform section, string label, bool current,
                                System.Action<bool> apply, SpawnerInstance instance)
        {
            EntitiesEditorUIBuilder.AddToggleRow(section, label, current,
                v => { apply(v); CommitInstanceEdit(instance, label); });
        }

        /// <summary>
        /// An enum row as a dropdown over the enum's own names.
        ///
        /// <para>Trigger, Mode, Advance On and Spawn Shape were read-only LABELS — four of the
        /// decisions that most define a spawner, displayed and unreachable. They are what six
        /// of the seven never-placed presets differ by, so the catalogue grew an asset every
        /// time somebody wanted a different one.</para>
        /// </summary>
        private void AddEnumRow<T>(RectTransform section, string label, T current,
                                   System.Action<T> apply, SpawnerInstance instance)
            where T : struct, System.Enum
        {
            var names = new System.Collections.Generic.List<string>(System.Enum.GetNames(typeof(T)));
            int index = names.IndexOf(current.ToString());

            SpawnerEditorUIBuilder.AddChoiceRow(section, label, names, index, chosen =>
            {
                if (!System.Enum.TryParse(chosen, out T parsed)) return;
                apply(parsed);
                CommitInstanceEdit(instance, label);
            });
        }

        private void CommitParsedFloat(string raw, string label, float min,
                                       System.Action<float> apply, SpawnerInstance instance)
        {
            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float parsed))
            {
                SetStatus($"'{raw}' is not a number — {label} unchanged.");
                RefreshPropertiesPanel();
                return;
            }
            apply(Mathf.Max(min, parsed));
            CommitInstanceEdit(instance, label);
        }

        private void CommitParsedInt(string raw, string label, int min,
                                     System.Action<int> apply, SpawnerInstance instance)
        {
            if (!int.TryParse(raw, out int parsed))
            {
                SetStatus($"'{raw}' is not a whole number — {label} unchanged.");
                RefreshPropertiesPanel();
                return;
            }
            apply(Mathf.Max(min, parsed));
            CommitInstanceEdit(instance, label);
        }

        /// <summary>
        /// Closes an edit: re-seed the live state machine from the changed config, persist,
        /// and redraw.
        ///
        /// <para><see cref="SpawnerInstance.ApplyConfig"/> is called with the SAME object the
        /// rows just mutated — the rows write through to the live config, and this exists to
        /// make the state machine notice. Without it, flipping Trigger from Proximity to Auto
        /// would take effect only after a reload, which reads as the control not working.</para>
        ///
        /// <para>No <c>SetDirty</c> and no <c>Undo.RecordObject</c>: nothing here touches a
        /// ScriptableObject any more, which is the entire point.</para>
        /// </summary>
        private void CommitInstanceEdit(SpawnerInstance instance, string label)
        {
            if (instance == null) return;

            instance.ApplyConfig(instance.Config);
            MarkInstancesDirty();
            SetStatus($"{label} updated on '{instance.InstanceId}' — this placement only.");
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
