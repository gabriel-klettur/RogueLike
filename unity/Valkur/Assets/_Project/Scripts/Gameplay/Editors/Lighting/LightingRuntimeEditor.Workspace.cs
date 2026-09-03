using System;
using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.World
{
    /// <summary>Lighting Editor (Ctrl+F3) — what it remembers between sessions.</summary>
    public partial class LightingRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_MODE   = "mode";
        private const string WS_SEARCH = "search";
        private const string WS_PRESET = "selectedPreset";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_MODE, _mode.ToString());
            ws.SetString(WS_SEARCH, _searchFilter ?? string.Empty);
            ws.SetString(WS_PRESET, _selectedPresetKey ?? string.Empty);

            // _ambientEnabled / _pointLightsEnabled are deliberately NOT captured. They are
            // a diagnostic override on the LIVE day/night cycle, not an editor preference:
            // restoring "ambient off" in a later session would leave the world dark with
            // nothing on screen explaining why, and the cycle would disagree with its own
            // profile until someone thought to reopen this editor.
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            if (Enum.TryParse(ws.GetString(WS_MODE, null), out EditorMode mode)
                && mode != EditorMode.Delete)
                SetMode(mode);

            string search = ws.GetString(WS_SEARCH, null);
            if (search != null)
            {
                _searchFilter = search;
                if (_ui.SearchBox != null) _ui.SearchBox.SetTextWithoutNotify(search);
            }

            string key = ws.GetString(WS_PRESET, null);
            if (!string.IsNullOrEmpty(key)) SelectPreset(key);
        }
    }
}
