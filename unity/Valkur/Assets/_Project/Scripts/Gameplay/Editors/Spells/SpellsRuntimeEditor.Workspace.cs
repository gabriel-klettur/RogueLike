using System;
using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor (F4) — what it remembers between sessions.
    ///
    /// Third and last of the duplicated PlayerPrefs column stores to be folded into the
    /// workspace; see <c>SpellsRuntimeEditor.TableColumnsConfig.cs</c>.
    ///
    /// This editor writes NO selection record: it edits a catalog, not the world, so its
    /// selection is not scoped by map slot or zone and belongs in the session bag with the
    /// rest of its filters.
    /// </summary>
    public partial class SpellsRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_SELECTED_SPELL = "selectedSpell";
        private const string WS_SEARCH         = "search";
        private const string WS_AUDIENCE       = "audienceFilter";
        private const string WS_HIDDEN_COLS    = "hiddenColumns";
        private const string WS_VIEW_TAB       = "viewTab";
        private const string WS_COLLAPSED      = "collapsedSchools";
        private const string WS_TREE_SCHOOL    = "treeSchool";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            ws.SetString(WS_SELECTED_SPELL, _selectedKey ?? string.Empty);
            ws.SetString(WS_SEARCH, _searchFilter ?? string.Empty);
            ws.SetString(WS_AUDIENCE, _audienceFilterKey ?? "all");
            ws.SetString(WS_HIDDEN_COLS, string.Join(",", _hiddenColumns));

            // Which of Grid / Table / Tree the author left open, and which sections of the
            // outline they had folded away. Both are pure view state: restoring them cannot
            // put the editor into a destructive mode, which is the one thing the workspace
            // layer refuses to bring back.
            if (_uiRefs.SpellsViewTabs != null)
                ws.SetString(WS_VIEW_TAB, _uiRefs.SpellsViewTabs.ActiveKey ?? "grid");
            ws.SetString(WS_COLLAPSED, string.Join(",", _collapsedSchools));
            ws.SetString(WS_TREE_SCHOOL, _treeSchoolFilter ?? TREE_SCHOOL_ALL);
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            RestoreHiddenColumns(ws);

            string audience = ws.GetString(WS_AUDIENCE, null);
            if (!string.IsNullOrEmpty(audience)) _audienceFilterKey = audience;

            string search = ws.GetString(WS_SEARCH, null);
            if (search != null)
            {
                _searchFilter = search;
                if (_uiRefs.SearchBox != null) _uiRefs.SearchBox.SetTextWithoutNotify(search);
            }

            string collapsed = ws.GetString(WS_COLLAPSED, null);
            if (!string.IsNullOrEmpty(collapsed))
            {
                _collapsedSchools.Clear();
                foreach (var key in collapsed.Split(','))
                    if (!string.IsNullOrEmpty(key)) _collapsedSchools.Add(key);
            }

            string treeSchool = ws.GetString(WS_TREE_SCHOOL, null);
            if (!string.IsNullOrEmpty(treeSchool))
            {
                _treeSchoolFilter = treeSchool;
                // SetActive raises TabChanged, which refreshes on its own; the assignment
                // above is what makes the strip and the model agree if the key no longer
                // exists (a school removed since the workspace was written).
                _uiRefs.SpellsTreeSchoolTabs?.SetActive(treeSchool);
            }

            // Everything is stale after a restore; SetActive below builds the one tab that
            // comes back on screen, and RefreshVisibleView covers a workspace that saved none.
            InvalidateAllViews();

            // Never restore straight into the constellation. It is a full-screen slab over
            // the game, and an editor that opens with one already up is the same surprise the
            // workspace layer refuses for a destructive MODE — the author asked for F4, not
            // for the screen to be covered.
            string viewTab = ws.GetString(WS_VIEW_TAB, null);
            if (viewTab == VIEW_TAB_GRAPH) viewTab = "tree";
            if (!string.IsNullOrEmpty(viewTab) && _uiRefs.SpellsViewTabs != null)
                _uiRefs.SpellsViewTabs.SetActive(viewTab);
            RefreshVisibleView();

            RestoreSelectedSpell(ws);
        }

        // ── Restore helpers ─────────────────────────────────────────────────────

        private void RestoreHiddenColumns(EditorWorkspace ws)
        {
            // Absent is not empty: with no stored value this editor seeds
            // SpellTableColumns.DefaultHidden so the table opens on a readable subset,
            // whereas an empty stored value means the author revealed every column on
            // purpose and must not be re-seeded on the next open.
            string blob = ws.GetString(WS_HIDDEN_COLS, null);
            if (blob == null) return;

            _hiddenColumns.Clear();
            foreach (var header in blob.Split(','))
                if (!string.IsNullOrWhiteSpace(header)) _hiddenColumns.Add(header.Trim());

            var known = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var col in SpellTableColumns.All) known.Add(col.Header);
            _hiddenColumns.RemoveWhere(h => !known.Contains(h));

            BuildTableHeader();
            RefreshColumnsCountLabel();
        }

        private void RestoreSelectedSpell(EditorWorkspace ws)
        {
            string key = ws.GetString(WS_SELECTED_SPELL, null);
            if (string.IsNullOrEmpty(key)) return;

            // Resolved against the live catalog. A spell deleted or renamed between
            // sessions leaves nothing selected — never a neighbour, which would put the
            // Properties panel on a spell the author never picked and, since LEFT CLICK
            // casts the selection while this editor is open, would let them fire it.
            if (_catalog == null || !_catalog.TryGet(key, out var spell) || spell == null) return;

            SelectSpell(key);
        }
    }
}
