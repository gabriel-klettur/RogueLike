using System;
using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.Entities
{
    /// <summary>Entities Editor (F5) — what it remembers between sessions.</summary>
    public partial class EntitiesRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_MODE     = "mode";
        private const string WS_CATEGORY = "category";
        private const string WS_SEARCH   = "search";
        private const string WS_ENTITY   = "selectedEntity";
        private const string WS_ANIM_STATE = "animState";
        private const string WS_ANIM_DIR   = "animDirection";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_MODE, _mode.ToString());
            ws.SetString(WS_CATEGORY, _category.ToString());
            ws.SetString(WS_SEARCH, _searchFilter ?? string.Empty);
            ws.SetString(WS_ENTITY, _selectedKey ?? string.Empty);

            // Which animation the author was looking at. Not the panel's open state: opening it
            // builds a camera and a RenderTexture, and a session that starts by paying for a
            // panel nobody asked for is the opposite of what restoring a workspace is for.
            ws.SetString(WS_ANIM_STATE, _animPreview.CurrentState.ToString());
            ws.SetString(WS_ANIM_DIR,   _animPreview.CurrentDirection.ToString());
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            // Delete is not restored: reopening straight into it is how an author removes a
            // monster they only meant to inspect. Same rule as Buildings and Tile.
            if (Enum.TryParse(ws.GetString(WS_MODE, null), out EditorMode mode)
                && mode != EditorMode.Delete)
                SetMode(mode);

            if (Enum.TryParse(ws.GetString(WS_CATEGORY, null), out EntityCategory category))
                SelectCategory(category);

            string search = ws.GetString(WS_SEARCH, null);
            if (search != null)
            {
                _searchFilter = search;
                if (_ui.SearchBox != null) _ui.SearchBox.SetTextWithoutNotify(search);
            }

            // Resolved against the live picker: an entity key removed from the catalog
            // leaves nothing selected rather than selecting a neighbour.
            string key = ws.GetString(WS_ENTITY, null);
            if (!string.IsNullOrEmpty(key)) SelectEntity(key);

            if (Enum.TryParse(ws.GetString(WS_ANIM_STATE, null),
                              out DirectionalAnimator.AnimState animState))
                _animPreview.SetState(animState);
            if (Enum.TryParse(ws.GetString(WS_ANIM_DIR, null),
                              out DirectionalAnimator.Direction animDir))
                _animPreview.SetDirection(animDir);

            SyncDropdownStateFromPanels();
        }

        /// <summary>
        /// Re-read which panels are actually on screen, and tell the editor about it.
        ///
        /// <para><b>The workspace layer opens and closes panels behind this editor's back.</b>
        /// <c>EditorWorkspaceService.ApplyNow</c> restores every <c>DraggablePanel</c> under the
        /// root — geometry AND open state — by writing the GameObject directly, and it does so
        /// BEFORE calling <see cref="RestoreWorkspace"/>. Nothing in that path goes through
        /// <c>SetDropdownOpen</c>, so <c>_openDropdowns</c> keeps whatever it had.</para>
        ///
        /// <para>Measured live: <c>_animPanelOpen</c> true, the preview camera and its
        /// RenderTexture running, the menu button lit — and the panel GameObject INACTIVE. The
        /// author sees no panel, the menu says one is open, and their next click on that menu
        /// CLOSES it, so it takes two clicks to get a panel that was never visible. The two
        /// panels with a lifetime behind them (Animation's camera, Timeline's) were also left
        /// running for something nobody could see.</para>
        ///
        /// <para>Reconciled from the GameObjects because they are what the author is looking
        /// at: of the two disagreeing records, the one on screen is the true one.</para>
        /// </summary>
        private void SyncDropdownStateFromPanels()
        {
            foreach (string name in DropdownNames)
            {
                var go = GetDropdown(name);
                if (go == null) continue;
                SetDropdownOpen(name, go.activeSelf);
            }
            RefreshMenuBtnHighlights();
        }
    }
}
