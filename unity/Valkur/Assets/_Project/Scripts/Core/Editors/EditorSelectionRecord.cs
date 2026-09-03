using System;

namespace Valkur.Core.Editors
{
    /// <summary>
    /// What the author had selected when the editor was last closed.
    ///
    /// This is the fragile third of the workspace and the rules around it are deliberate:
    ///
    /// <list type="bullet">
    /// <item><b>A stable id, never an index.</b> An index into a list points at a different
    /// object the moment that list reorders — a building deleted, a catalog re-authored —
    /// and it fails SILENTLY, which is the worst failure available here.</item>
    /// <item><b>Carries the context it was taken in</b> (<see cref="mapSlot"/>,
    /// <see cref="zone"/>). If the editor opens in a different context the record is
    /// discarded up front, without even attempting to resolve: cheaper, and it dodges the
    /// false positive of an id reused across map slots.</item>
    /// <item><b>An unresolved selection leaves the editor EMPTY.</b> Never "the closest
    /// match", never "the first one" — selecting the wrong object is worse than selecting
    /// nothing, because the author's next action edits something they did not choose.</item>
    /// <item><b>An unresolved selection is not a warning.</b> It is the expected outcome
    /// after a slot or zone change. Report it through the editor's own status line, never
    /// <c>Debug.LogWarning</c> — this project requires a clean console, and in a build
    /// there is no console to read anyway.</item>
    /// </list>
    /// </summary>
    [Serializable]
    public sealed class EditorSelectionRecord
    {
        /// <summary>
        /// What KIND of thing was selected, so an editor with several selectable kinds
        /// (Entities selects monsters and players; FSM selects states and transitions)
        /// does not try to resolve an id against the wrong catalog.
        /// </summary>
        public string type = string.Empty;

        /// <summary>The stable id itself — a placement GUID, a catalog key, a state name.</summary>
        public string id = string.Empty;

        /// <summary>Map slot the selection was taken in. Empty means "not slot-scoped".</summary>
        public string mapSlot = string.Empty;

        /// <summary>Zone the selection was taken in. Empty means "not zone-scoped".</summary>
        public string zone = string.Empty;

        public bool HasValue => !string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(id);

        public void Clear()
        {
            type    = string.Empty;
            id      = string.Empty;
            mapSlot = string.Empty;
            zone    = string.Empty;
        }

        public void Set(string selectionType, string selectionId,
                        string currentMapSlot = "", string currentZone = "")
        {
            type    = selectionType ?? string.Empty;
            id      = selectionId   ?? string.Empty;
            mapSlot = currentMapSlot ?? string.Empty;
            zone    = currentZone    ?? string.Empty;
        }

        /// <summary>
        /// Whether this record is even worth trying to resolve, given where the editor is
        /// opening now. Both comparisons are <see cref="StringComparison.OrdinalIgnoreCase"/>
        /// because zone names are compared that way everywhere else in the project.
        ///
        /// An empty stored context matches anything — that is how a non-scoped editor
        /// (Spells, Items catalog) opts out without a special case.
        /// </summary>
        public bool AppliesTo(string currentMapSlot, string currentZone)
        {
            if (!HasValue) return false;

            if (!string.IsNullOrEmpty(mapSlot) &&
                !string.Equals(mapSlot, currentMapSlot ?? string.Empty,
                               StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(zone) &&
                !string.Equals(zone, currentZone ?? string.Empty,
                               StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
    }
}
