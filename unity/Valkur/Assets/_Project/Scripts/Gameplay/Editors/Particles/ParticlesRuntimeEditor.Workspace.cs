using System;
using UnityEngine;
using Valkur.Core.Editors;
using Valkur.Gameplay.Editors.Workspace;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Particles Editor (F1) — what it remembers between sessions.
    ///
    /// Also the second of the three duplicated PlayerPrefs column stores to be folded into
    /// the workspace; see <c>ParticlesRuntimeEditor.Table.cs</c>.
    /// </summary>
    public partial class ParticlesRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_MODE            = "mode";
        private const string WS_PRESET          = "selectedPreset";
        private const string WS_SEARCH          = "search";
        private const string WS_CATEGORY        = "categoryTab";
        private const string WS_SPELLS_EXPANDED = "spellsExpanded";
        private const string WS_HIDDEN_COLS     = "hiddenColumns";

        private const string WS_SELECTION_EMITTER = "emitter";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            ws.SetString(WS_MODE, _mode.ToString());
            ws.SetString(WS_PRESET, _selectedPresetId ?? string.Empty);
            ws.SetString(WS_SEARCH, _searchFilter ?? string.Empty);
            ws.SetString(WS_CATEGORY, _categoryFilter ?? string.Empty);
            ws.SetBool(WS_SPELLS_EXPANDED, _spellsExpanded);
            ws.SetString(WS_HIDDEN_COLS, string.Join(",", _hiddenParticleColumns));

            // A placed emitter's stable identity is its PersistedParticleInstance.StableGuid —
            // the same id the instances file is keyed by, so a selection survives exactly as
            // long as the placement it points at.
            var identity = _activeInstance != null
                ? _activeInstance.GetComponentInParent<PersistedParticleInstance>()
                : null;

            if (identity != null && !string.IsNullOrEmpty(identity.StableGuid))
            {
                ws.selection.Set(WS_SELECTION_EMITTER, identity.StableGuid,
                    EditorWorkspaceContext.CurrentMapSlot, EditorWorkspaceContext.CurrentZone);
            }
            else
            {
                ws.selection.Clear();
            }
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            RestoreHiddenColumns(ws);

            if (Enum.TryParse(ws.GetString(WS_MODE, null), out EditorMode mode)) SetMode(mode);

            _spellsExpanded = ws.GetBool(WS_SPELLS_EXPANDED, _spellsExpanded);

            string search = ws.GetString(WS_SEARCH, null);
            if (search != null)
            {
                _searchFilter = search;
                if (_ui.SearchBox != null) _ui.SearchBox.SetTextWithoutNotify(search);
            }

            string category = ws.GetString(WS_CATEGORY, null);
            if (category != null)
            {
                _categoryFilter = category;
                if (_ui.PresetsCategoryTabStrip != null) _ui.PresetsCategoryTabStrip.SetActive(category);
            }

            RefreshPicker();
            RefreshTable();

            RestoreSelectedPreset(ws);
            RestoreSelectedEmitter(ws);
        }

        // ── Restore helpers ─────────────────────────────────────────────────────

        private void RestoreHiddenColumns(EditorWorkspace ws)
        {
            // Absent is NOT the same as empty here, and the difference is visible: with no
            // stored value this editor seeds ParticleTableColumns.DefaultHidden so the table
            // opens on a readable subset of its columns, while an empty stored value means
            // the author deliberately revealed all of them. GetString's null fallback is what
            // keeps those two apart.
            string blob = ws.GetString(WS_HIDDEN_COLS, null);
            if (blob == null) return;

            _hiddenParticleColumns.Clear();
            foreach (var header in blob.Split(','))
                if (!string.IsNullOrWhiteSpace(header)) _hiddenParticleColumns.Add(header.Trim());

            // Drop headers the schema no longer has, or the counter lies and the popup shows
            // no checkbox to un-hide them.
            var known = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var col in ParticleTableColumns.All) known.Add(col.Header);
            _hiddenParticleColumns.RemoveWhere(h => !known.Contains(h));

            BuildPresetsTableHeader();
            UpdateParticleColumnsCountLabelPopup();
            UpdateParticleColumnsBtnLabel();
        }

        private void RestoreSelectedPreset(EditorWorkspace ws)
        {
            string presetId = ws.GetString(WS_PRESET, null);
            if (string.IsNullOrEmpty(presetId)) return;

            // Resolved against the live catalog: a preset asset the author deleted between
            // sessions leaves nothing selected rather than selecting a neighbour.
            if (_catalog == null || _catalog.GetById(presetId) == null) return;
            SelectPreset(presetId);
        }

        private void RestoreSelectedEmitter(EditorWorkspace ws)
        {
            var record = ws.selection;
            if (record == null || !record.HasValue) return;
            if (record.type != WS_SELECTION_EMITTER) return;

            if (!record.AppliesTo(EditorWorkspaceContext.CurrentMapSlot,
                                  EditorWorkspaceContext.CurrentZone))
                return;

            var all = FindObjectsOfType<PersistedParticleInstance>(includeInactive: true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].StableGuid != record.id) continue;
                SetActiveInstance(all[i].gameObject);
                return;
            }

            // A placement removed, or a different map slot loaded. Ordinary — the author
            // hears about it on the status line, never through the console.
            SetStatus("El emisor seleccionado antes ya no está en este mapa.");
        }
    }
}
