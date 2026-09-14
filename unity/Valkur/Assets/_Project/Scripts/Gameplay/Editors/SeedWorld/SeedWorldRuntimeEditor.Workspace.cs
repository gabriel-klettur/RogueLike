using System;
using UnityEngine;
using Valkur.Core.Editors;
using Valkur.Data.WorldGen;

namespace Valkur.Gameplay.Editors.SeedWorld
{
    /// <summary>
    /// Seed World editor — what it remembers between sessions.
    ///
    /// <para>The tab, the preview layer and the WHOLE settings value. The settings are the
    /// author's work in this editor and nothing else holds them yet (saving a preset asset is
    /// phase 2), so losing them on a Play-mode restart would lose the only copy. They go through
    /// <see cref="WorldGenSettings.FromJson"/>, which clamps, so a document edited by hand or
    /// written by an older build cannot put the generator outside its range. The undo history is
    /// deliberately NOT persisted.</para>
    /// </summary>
    public partial class SeedWorldRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_TAB = "tab";
        private const string WS_LAYER = "layer";
        private const string WS_SETTINGS = "settings";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_TAB, _tab.ToString());
            ws.SetString(WS_LAYER, _layer.ToString());
            ws.SetString(WS_SETTINGS, _settings.ToJson());
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            if (Enum.TryParse(ws.GetString(WS_TAB, null), out Tab tab)) _tab = tab;
            if (Enum.TryParse(ws.GetString(WS_LAYER, null), out PreviewLayer layer)) _layer = layer;

            var restored = WorldGenSettings.FromJson(ws.GetString(WS_SETTINGS, null));
            if (restored == null) return;

            _settings = restored;
            _undo.Clear();
            _redo.Clear();
            if (_active)
            {
                RebuildBody();
                Regenerate();
            }
        }
    }
}
