using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — recycling table rows instead of rebuilding them.
    ///
    /// <para>WHY THIS EXISTS. Virtualising the table traded one cost for another and the
    /// trade was only half made: before it, every row existed, so opening the tab cost
    /// 4,490 ms and SCROLLING WAS FREE; after it, opening cost 228 ms and a fast scrollbar
    /// drag cost 111 ms a step, because every row entering the viewport was 29 fresh cells.
    /// A virtualised list that rebuilds is a list that moved its stutter somewhere else.</para>
    ///
    /// <para>Every row has the SAME shape — same visible columns, same order, same widths —
    /// so a row leaving the viewport is exactly the row that is about to enter it at the
    /// other end, with different values. Recycling it is a handful of assignments against
    /// building ~76 Graphics.</para>
    ///
    /// <para>THE POOL IS DISCARDED WHENEVER THE COLUMN SET CHANGES. A pooled row is only
    /// reusable while it carries the columns the table is currently drawing; hiding a column
    /// makes every pooled row the wrong shape, and <see cref="RebindSpellRow"/> would reject
    /// them one by one anyway. <c>DestroyAllSpellTableRows</c> clears it, which is the same
    /// path a filter change already takes.</para>
    /// </summary>
    public partial class SpellsRuntimeEditor
    {
        private readonly Stack<GameObject> _spellRowPool = new Stack<GameObject>();

        /// <summary>
        /// Rows kept for reuse. A little over one screenful plus overscan: the pool only ever
        /// holds what just left the viewport, and anything beyond that is memory held for a
        /// scroll that already happened.
        /// </summary>
        private const int SPELL_ROW_POOL_MAX = 40;

        /// <summary>Take a row for <paramref name="rowIndex"/>, recycled if one is free.</summary>
        private GameObject AcquireSpellRow(SpellDefinition def, int rowIndex)
        {
            while (_spellRowPool.Count > 0)
            {
                var candidate = _spellRowPool.Pop();
                if (candidate == null) continue;

                if (RebindSpellRow(candidate, def, rowIndex))
                {
                    candidate.SetActive(true);
                    return candidate;
                }

                // Wrong shape for the current columns — it can never be rebound, so it goes
                // rather than being pushed back for the next caller to reject again.
                DetachAndDestroy(candidate);
            }

            return BuildSpellTableRow(def, rowIndex);
        }

        /// <summary>Hand a row back. Kept parented and inactive; the body has no layout group.</summary>
        private void ReleaseSpellRow(GameObject rowGo)
        {
            if (rowGo == null) return;

            if (_spellRowPool.Count >= SPELL_ROW_POOL_MAX) { DetachAndDestroy(rowGo); return; }

            rowGo.SetActive(false);
            _spellRowPool.Push(rowGo);
        }

        private void ClearSpellRowPool()
        {
            while (_spellRowPool.Count > 0) DetachAndDestroy(_spellRowPool.Pop());
        }

        /// <summary>
        /// Point an existing row at a different spell. Returns false the moment anything is
        /// not the shape this row is expected to have, so a caller can fall back to building
        /// one — the row is never left half-rebound.
        /// </summary>
        private bool RebindSpellRow(GameObject rowGo, SpellDefinition def, int rowIndex)
        {
            if (rowGo == null || def == null) return false;

            var rowBg = rowGo.GetComponent<Image>();
            var btn = rowGo.GetComponent<Button>();
            if (rowBg == null || btn == null) return false;

            var cols = SpellTableColumns.All;
            int childIndex = 0;
            for (int c = 0; c < cols.Count; c++)
            {
                var col = cols[c];
                if (!IsColumnVisible(col)) continue;
                if (childIndex >= rowGo.transform.childCount) return false;

                var cell = rowGo.transform.GetChild(childIndex++);
                if (cell.name != "Cell_" + col.Header) return false;
                if (!RebindSpellCell(cell, col, def)) return false;
            }
            if (childIndex != rowGo.transform.childCount) return false;

            var rowRt = (RectTransform)rowGo.transform;
            rowRt.anchoredPosition = new Vector2(0f, -rowIndex * SPELL_TABLE_ROW_H);
            rowGo.name = "Row_" + (def.spellKey ?? rowIndex.ToString());

            Color background = def.spellKey == _selectedKey
                ? ROW_SELECTED
                : rowIndex % 2 == 0 ? ROW_ZEBRA_A : ROW_ZEBRA_B;
            rowBg.color = background;

            var colors = btn.colors;
            colors.normalColor = background;
            colors.highlightedColor = background + ROW_HOVER_LIFT;
            btn.colors = colors;

            string capturedKey = def.spellKey;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => SelectSpell(capturedKey));
            if (!string.IsNullOrEmpty(capturedKey))
                _tableRowsByKey[capturedKey] = new RowRefs { Background = rowBg, Button = btn, Index = rowIndex };

            return true;
        }

        private bool RebindSpellCell(Transform cell, SpellTableColumn col, SpellDefinition def)
        {
            switch (col.EditorKind)
            {
                case SpellTableEditorKind.Text:
                case SpellTableEditorKind.Int:
                case SpellTableEditorKind.Float:
                    return RebindTextCell(cell, col, def);

                case SpellTableEditorKind.Toggle:
                {
                    var toggle = cell.GetComponentInChildren<Toggle>(true);
                    if (toggle == null) return false;
                    bool.TryParse(col.GetString(def), out bool on);
                    toggle.SetIsOnWithoutNotify(on);
                    toggle.onValueChanged.RemoveAllListeners();
                    if (col.SetString != null)
                        toggle.onValueChanged.AddListener(v => OnSpellCellCommit(col, def, v.ToString()));
                    return true;
                }

                case SpellTableEditorKind.Dropdown:
                {
                    var dropdown = cell.GetComponentInChildren<TMP_Dropdown>(true);
                    if (dropdown == null) return false;
                    dropdown.SetValueWithoutNotify(col.GetDropdownIndex(def));
                    dropdown.RefreshShownValue();
                    dropdown.onValueChanged.RemoveAllListeners();
                    dropdown.onValueChanged.AddListener(i => OnSpellDropdownCellCommit(col, def, i));
                    return true;
                }

                case SpellTableEditorKind.SpriteThumbnail:
                {
                    var image = cell.Find("SpriteImg")?.GetComponent<Image>();
                    var backdrop = cell.Find("SpriteBg")?.GetComponent<Image>();
                    if (image == null || backdrop == null) return false;
                    Sprite sprite = col.Header == "sprite" ? IceLanceArt.ResolveIcon(def) : null;
                    image.sprite = sprite;
                    image.enabled = sprite != null;
                    backdrop.color = sprite != null ? Color.black : SPRITE_CELL_EMPTY_BG;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Re-label a value cell. A cell the author had PROMOTED to a live input field is
        /// demoted back first — a recycled row must come back in its resting state, or a
        /// field the author opened on one spell would reappear over another one's value.
        /// </summary>
        private bool RebindTextCell(Transform cell, SpellTableColumn col, SpellDefinition def)
        {
            if (cell.GetComponentInChildren<TMP_InputField>(true) != null)
            {
                for (int i = cell.childCount - 1; i >= 0; i--)
                {
                    var child = cell.GetChild(i);
                    child.SetParent(null, false);
                    SafeDestroy.Of(child.gameObject);
                }
                var staleImage = cell.gameObject.GetComponent<Image>();
                if (staleImage != null) SafeDestroy.Of(staleImage);
                var staleButton = cell.gameObject.GetComponent<Button>();
                if (staleButton != null) SafeDestroy.Of(staleButton);

                if (col.SetString == null) BuildSpellTextCell(cell, col, def);
                else BuildLazyEditableCell(cell, col, def);
                return true;
            }

            var label = cell.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null) return false;
            label.text = col.GetString(def);

            var button = cell.gameObject.GetComponent<Button>();
            if (col.SetString == null) return button == null;
            if (button == null) return false;

            var capturedCell = cell;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => PromoteCellToInput(capturedCell, col, def));
            return true;
        }
    }
}
