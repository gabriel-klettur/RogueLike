using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.Editors;
using Valkur.Gameplay.Editors.Workspace;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Items Editor (F7) — what it remembers between sessions.
    ///
    /// Panel geometry and visibility are captured generically off <c>DraggablePanel</c> and
    /// cost this file nothing; everything here is the editor-specific half: which mode was
    /// active, which category tab, what was typed in the search box, which columns were
    /// hidden, which catalog item was picked, and which world drop was selected.
    ///
    /// First adopter of <see cref="IProvidesWorkspaceState"/> — the Phase 2 pilot. Every
    /// pattern here is meant to be copied by the other fourteen, so the awkward parts are
    /// documented rather than smoothed over.
    /// </summary>
    public partial class ItemsRuntimeEditor : IProvidesWorkspaceState
    {
        // Session-bag keys. String constants rather than nameof(): these are on-disk keys,
        // so renaming the C# field must NOT silently orphan an author's saved value.
        private const string WS_MODE          = "mode";
        private const string WS_SEARCH        = "search";
        private const string WS_CATEGORY_TAB  = "categoryTab";
        private const string WS_SELECTED_ITEM = "selectedItem";
        private const string WS_HIDDEN_COLS   = "hiddenColumns";

        /// <summary>The selection kind this editor writes. See <see cref="EditorSelectionRecord"/>.</summary>
        private const string WS_SELECTION_DROP = "drop";

        // ── IProvidesWorkspaceState ─────────────────────────────────────────────

        /// <summary>
        /// Null until the first Activate builds the UI. The service tolerates that and
        /// retries on the next open, which is why this must not force a build — building
        /// UI from a capture would create a canvas for an editor the author never opened.
        /// </summary>
        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            ws.SetString(WS_MODE, _mode.ToString());
            ws.SetString(WS_SEARCH, _searchFilter ?? string.Empty);
            ws.SetString(WS_SELECTED_ITEM, _selectedItemId ?? string.Empty);

            // The tab's own KEY, not the int it maps to: the key is the vocabulary the UI
            // and the author share ("equipment"), while the int is an internal id that a
            // catalog refactor is free to renumber. A renumbered int would silently restore
            // a different tab; an unknown key just falls back to "all".
            ws.SetString(WS_CATEGORY_TAB,
                _uiRefs.GridCategoryTabs != null ? (_uiRefs.GridCategoryTabs.ActiveKey ?? "") : "");

            ws.SetString(WS_HIDDEN_COLS, string.Join(",", _hiddenColumns));

            CaptureSelection(ws);
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            RestoreHiddenColumns(ws);
            RestoreMode(ws);
            RestoreCategoryTab(ws);
            RestoreSearch(ws);
            RestoreSelectedItem(ws);
            RestoreSelectedDrop(ws);
        }

        // ── Capture helpers ─────────────────────────────────────────────────────

        private void CaptureSelection(EditorWorkspace ws)
        {
            // Only a LIVE, PERSISTENT drop is worth remembering. A pickup with no DropId is
            // a runtime drop nothing will recreate next session, so storing its id would
            // guarantee an unresolvable record — and the whole point of the policy is that
            // an unresolvable record is the exception, not the norm.
            if (_selectedInstance != null && !string.IsNullOrEmpty(_selectedInstance.DropId))
            {
                ws.selection.Set(WS_SELECTION_DROP, _selectedInstance.DropId,
                    EditorWorkspaceContext.CurrentMapSlot, EditorWorkspaceContext.CurrentZone);
            }
            else
            {
                ws.selection.Clear();
            }
        }

        // ── Restore helpers ─────────────────────────────────────────────────────
        //
        // Every one of these validates against the LIVE domain and falls back silently.
        // A workspace can name a mode this build removed, a tab the UI no longer has, a
        // column header a refactor renamed or an item the author deleted — all of which
        // are normal, none of which is an error.

        private void RestoreHiddenColumns(EditorWorkspace ws)
        {
            string blob = ws.GetString(WS_HIDDEN_COLS, null);
            if (blob == null) return;

            _hiddenColumns.Clear();
            foreach (var header in blob.Split(','))
                if (!string.IsNullOrWhiteSpace(header)) _hiddenColumns.Add(header.Trim());

            // Drop headers no longer in the schema, or the count label lies and the popup
            // shows a checkbox for a column that cannot be un-hidden.
            var known = new HashSet<string>();
            foreach (var col in ItemTableColumns.All) known.Add(col.Header);
            _hiddenColumns.RemoveWhere(h => !known.Contains(h));

            BuildTableHeader();
            RefreshTable();
            RefreshColumnsCountLabel();
        }

        private void RestoreMode(EditorWorkspace ws)
        {
            string raw = ws.GetString(WS_MODE, null);
            if (string.IsNullOrEmpty(raw)) return;
            if (System.Enum.TryParse(raw, out EditorMode mode)) SetMode(mode);
        }

        private void RestoreCategoryTab(EditorWorkspace ws)
        {
            var tabs = _uiRefs.GridCategoryTabs;
            if (tabs == null) return;

            string key = ws.GetString(WS_CATEGORY_TAB, null);
            if (string.IsNullOrEmpty(key)) return;

            // SetActive(key) raises TabChanged, which is what actually updates
            // _categoryFilter — deliberately not set here as well, or the tab strip and the
            // filter become two owners of one fact. It returns false for a key the strip
            // does not have, which leaves the default tab selected.
            tabs.SetActive(key);
        }

        private void RestoreSearch(EditorWorkspace ws)
        {
            string text = ws.GetString(WS_SEARCH, null);
            if (string.IsNullOrEmpty(text)) return;

            _searchFilter = text;

            // SetTextWithoutNotify, so restoring does not re-enter OnSearchChanged — which
            // would refresh the picker and table a second time for no change. The refresh
            // below is the single one.
            if (_uiRefs.SearchBox != null) _uiRefs.SearchBox.SetTextWithoutNotify(text);

            RefreshPicker();
            RefreshTable();
        }

        private void RestoreSelectedItem(EditorWorkspace ws)
        {
            string itemId = ws.GetString(WS_SELECTED_ITEM, null);
            if (string.IsNullOrEmpty(itemId)) return;

            // Resolve against the live catalog. An item the author has since deleted leaves
            // the editor with nothing selected — never "the first one", which would put the
            // Properties panel on something they did not pick.
            EnsureCatalog();
            if (FindItemById(itemId) == null) return;

            SelectItem(itemId);
        }

        private void RestoreSelectedDrop(EditorWorkspace ws)
        {
            var record = ws.selection;
            if (record == null || !record.HasValue) return;
            if (record.type != WS_SELECTION_DROP) return;

            // Discarded up front when the context differs — cheaper than resolving, and it
            // dodges the false positive of a drop id reused across map slots.
            if (!record.AppliesTo(EditorWorkspaceContext.CurrentMapSlot,
                                  EditorWorkspaceContext.CurrentZone))
                return;

            var pickup = ResolveDropService()?.GetLivePickup(record.id);
            if (pickup == null)
            {
                // The expected outcome after a slot change or a deleted drop — reported on
                // the status line the author is actually looking at, never as a console
                // warning. A warning here would fire on an ordinary open and train the
                // reader to scroll past a console this project requires to be clean.
                SetStatus("La selección anterior ya no existe en este mapa.");
                return;
            }

            SetActiveInstance(pickup);
        }
    }
}
