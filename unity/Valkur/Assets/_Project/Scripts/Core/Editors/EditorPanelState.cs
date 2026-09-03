using System;
using UnityEngine;

namespace Valkur.Core.Editors
{
    /// <summary>
    /// One floating panel's remembered geometry and visibility.
    ///
    /// Captured generically off <c>DraggablePanel</c>, so every editor that already uses
    /// one — thirteen of the sixteen — gets its layout persisted with no per-editor code.
    /// </summary>
    [Serializable]
    public sealed class EditorPanelState
    {
        /// <summary>
        /// Stable identity, namespaced by owning editor: <c>"Buildings/PropertiesPanel"</c>.
        ///
        /// The namespace is not decoration. Measured 2026-09-02, the sixteen editors build
        /// 68 distinct panel names and TWO of them collide: Buildings (F10) and Map (F11)
        /// both name theirs <c>"PropertiesPanel"</c>, and since
        /// <c>DraggablePanel.PersistenceKey</c> is assigned nowhere in the project, both fell
        /// back to the GameObject name and shared one remembered-closed bit — closing
        /// Properties in Buildings closed it in Map. Composing the owner in makes that class
        /// of collision impossible instead of relying on 68 literals staying unique.
        /// </summary>
        public string panelId = string.Empty;

        /// <summary>Anchored position in canvas units, after anchor normalization.</summary>
        public Vector2 anchoredPosition = Vector2.zero;

        /// <summary>Panel size in canvas units. Zero means "never captured" — keep the built size.</summary>
        public Vector2 size = Vector2.zero;

        /// <summary>Collapsed to header height.</summary>
        public bool minimized;

        /// <summary>Expanded to canvas height.</summary>
        public bool maximized;

        /// <summary>
        /// False when the author closed the panel with the header X. This is the bit
        /// <c>DraggablePanel</c> used to keep on its own in PlayerPrefs; it is a field of the
        /// workspace now, so there is exactly one owner of panel visibility.
        /// </summary>
        public bool open = true;

        /// <summary>
        /// Sibling index within the canvas — the paint order the author last left. Restoring
        /// it is what keeps a panel the author deliberately brought to the front from
        /// sinking behind its neighbours on the next open. -1 means unknown.
        /// </summary>
        public int siblingIndex = -1;

        /// <summary>
        /// True when this record has real geometry to apply. A record that only carries the
        /// <see cref="open"/> bit (written by a build before geometry was captured, or by a
        /// panel that was closed before it ever normalized) must not stamp a zero rect onto
        /// a freshly built panel.
        /// </summary>
        public bool HasGeometry => size.sqrMagnitude > 0f;
    }
}
