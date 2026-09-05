using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — which of the three list views gets built, and when.
    ///
    /// <para>ONLY THE VISIBLE VIEW IS BUILT. Measured before this, a warm F4 took 2,959 ms,
    /// and 2,040 of it was <c>RefreshTable</c> building 104 rows x 29 columns of live uGUI
    /// widgets while the Grid tab was the one on screen — 69 % of the freeze spent on a view
    /// nobody was looking at, every single open. The other two views are now marked DIRTY
    /// and built the moment their tab is picked, which is the first moment anyone can see
    /// them.</para>
    ///
    /// <para>SELECTION IS A REPAINT, NOT A REBUILD. Clicking a tile used to rebuild the whole
    /// grid, the whole outline and the form to change the colour of two slots — 366 ms per
    /// click, measured. Each view now keeps a map from spell key to the graphic it drew, so
    /// a selection change recolours the old row and the new one and touches nothing else.
    /// </para>
    ///
    /// <para>DETACH BEFORE DESTROY. In Play Mode <c>Destroy</c> is deferred to end of frame,
    /// so a row that is merely destroyed is still a child of the layout group for the rest
    /// of the frame the rebuild runs in — measured, 208 rows under the body during a
    /// 104-row rebuild, and <c>ForceRebuildLayoutImmediate</c> laid out all of them.
    /// Reparenting to null first takes it out of the layout at once.</para>
    /// </summary>
    public partial class SpellsRuntimeEditor
    {
        private const string VIEW_TAB_GRID = "grid";
        private const string VIEW_TAB_TABLE = "table";
        private const string VIEW_TAB_TREE = "tree";

        private bool _gridDirty = true;
        private bool _tableDirty = true;
        private bool _treeDirty = true;

        /// <summary>One grid slot's repaintable parts, keyed by spell key.</summary>
        private struct SlotRefs
        {
            public Button Button;
            public Color RestingTint;
        }

        private readonly Dictionary<string, SlotRefs> _gridSlotsByKey =
            new Dictionary<string, SlotRefs>(System.StringComparer.Ordinal);

        /// <summary>One list row's repaintable parts (table or outline), keyed by spell key.</summary>
        private struct RowRefs
        {
            public Image Background;
            public Button Button;
            public int Index;
        }

        private readonly Dictionary<string, RowRefs> _tableRowsByKey =
            new Dictionary<string, RowRefs>(System.StringComparer.Ordinal);
        private readonly Dictionary<string, RowRefs> _treeRowsByKey =
            new Dictionary<string, RowRefs>(System.StringComparer.Ordinal);

        private string ActiveViewTab
            => _uiRefs.SpellsViewTabs != null ? _uiRefs.SpellsViewTabs.ActiveKey ?? VIEW_TAB_GRID : VIEW_TAB_GRID;

        /// <summary>
        /// The filter changed (search, audience, add/remove, activate): every view is stale,
        /// and the one on screen is rebuilt now. The others wait for their tab.
        /// </summary>
        private void InvalidateAllViews()
        {
            _gridDirty = true;
            _tableDirty = true;
            _treeDirty = true;
        }

        /// <summary>Build whichever view is on screen if it is stale. Cheap when it is not.</summary>
        private void RefreshVisibleView()
        {
            switch (ActiveViewTab)
            {
                case VIEW_TAB_TABLE:
                    if (_tableDirty) RefreshTable();
                    break;
                case VIEW_TAB_TREE:
                    if (_treeDirty) RefreshTree();
                    break;
                case VIEW_TAB_GRAPH:
                    // The constellation owns its own board and rebuilds itself.
                    break;
                default:
                    if (_gridDirty) RefreshPicker();
                    break;
            }
        }

        /// <summary>
        /// Recolour the previously selected and the newly selected entries in every BUILT
        /// view. A view that has not been built since the last filter change has nothing to
        /// repaint and will draw the selection correctly when it is.
        /// </summary>
        private void RepaintSelection(string previousKey, string newKey)
        {
            RepaintGridSlot(previousKey, selected: false);
            RepaintGridSlot(newKey, selected: true);
            RepaintRow(_tableRowsByKey, previousKey, selected: false);
            RepaintRow(_tableRowsByKey, newKey, selected: true);
            RepaintRow(_treeRowsByKey, previousKey, selected: false);
            RepaintRow(_treeRowsByKey, newKey, selected: true);
        }

        private void RepaintGridSlot(string key, bool selected)
        {
            if (string.IsNullOrEmpty(key) || !_gridSlotsByKey.TryGetValue(key, out var slot)) return;
            if (slot.Button == null) return;

            // Through SetSlotTint, never by writing the Image — RefreshPicker records why: a
            // Button owns its targetGraphic's colour and reverts a direct write on the next
            // state transition.
            EditorUIHelpers.SetSlotTint(slot.Button,
                selected ? EditorUIHelpers.SLOT_SELECTED : slot.RestingTint);

            var rt = slot.Button.GetComponent<RectTransform>();
            var border = rt.Find("SelectionBorder");
            if (selected && border == null) EditorUIHelpers.MakeSelectionBorder(rt);
            else if (!selected && border != null)
            {
                border.SetParent(null, false);
                Valkur.Core.SafeDestroy.Of(border.gameObject);
            }
        }

        private static void RepaintRow(Dictionary<string, RowRefs> rows, string key, bool selected)
        {
            if (string.IsNullOrEmpty(key) || !rows.TryGetValue(key, out var row)) return;
            if (row.Background == null) return;

            Color color = selected
                ? ROW_SELECTED
                : row.Index % 2 == 0 ? ROW_ZEBRA_A : ROW_ZEBRA_B;

            row.Background.color = color;
            if (row.Button == null) return;

            // The Button keeps its own copy of the resting colour and writes it back on the
            // next pointer transition, so both have to agree or the repaint lasts one hover.
            var colors = row.Button.colors;
            colors.normalColor = color;
            colors.highlightedColor = color + ROW_HOVER_LIFT;
            row.Button.colors = colors;
        }

        /// <summary>
        /// Empty a list container so a rebuild starts from nothing THIS frame. See the class
        /// doc for why the detach is not optional in Play Mode.
        /// </summary>
        private static void DetachAndDestroyChildren(Transform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                child.SetParent(null, false);
                Valkur.Core.SafeDestroy.Of(child.gameObject);
            }
        }
    }
}
