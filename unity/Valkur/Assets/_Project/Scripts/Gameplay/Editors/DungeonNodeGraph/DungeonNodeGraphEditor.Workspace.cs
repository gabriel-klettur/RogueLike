using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.Editors.DungeonNodeGraph
{
    /// <summary>Dungeon NodeGraph Editor — what it remembers between sessions.</summary>
    public partial class DungeonNodeGraphEditor : IProvidesWorkspaceState
    {
        private const string WS_GRAPH = "activeGraph";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetString(WS_GRAPH, _activeGraphName ?? string.Empty);

            // The NODES are not captured. They are the graph document itself, which this
            // editor already saves to disk under its own name — persisting them here would
            // create a second copy that disagrees with the file the moment either is edited.
            // The workspace remembers WHICH document was open, never its contents.
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            string graph = ws.GetString(WS_GRAPH, null);
            if (string.IsNullOrEmpty(graph)) return;
            if (graph == _activeGraphName) return;

            // Only reopened when the file still parses. Load already reports a failure on
            // its own toast, so this pre-check exists to keep a graph deleted between
            // sessions from producing that toast on an otherwise ordinary open — the editor
            // simply stays on its default empty document.
            if (LoadFromFile(graph) == null) return;
            Load(graph);
        }
    }
}
