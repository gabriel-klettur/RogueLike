using System;
using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.Enemies.FSM
{
    /// <summary>FSM Editor (F12) — what it remembers between sessions.</summary>
    public partial class FSMRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_SEARCH = "search";
        private const string WS_ZOOM   = "graphZoom";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_SEARCH, _searchFilter ?? string.Empty);
            ws.SetFloat(WS_ZOOM, _zoom);

            // Neither _selectedSet nor _selectedState is captured. A state only exists
            // inside a set, so it would need a compound key that a set rename silently
            // invalidates — and this editor writes the authored half of the FSM to disk,
            // where selecting the wrong set is the difference between editing the melee
            // monsters and editing the bosses.
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            // The graph zoom is this editor OWN scalar, drawn inside its panel — unlike a
            // world camera zoom it touches no orthographicSize and no pixel-snap ladder, so
            // restoring it is safe. Clamped, or a value stored by a future build could
            // leave the graph at a scale with no visible nodes and no way back.
            _zoom = Mathf.Clamp(ws.GetFloat(WS_ZOOM, _zoom), 0.25f, 4f);

            string search = ws.GetString(WS_SEARCH, null);
            if (search != null)
            {
                _searchFilter = search;
                if (_uiRefs.SearchBox != null) _uiRefs.SearchBox.SetTextWithoutNotify(search);
            }
        }
    }
}
