using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Items Editor — Table view (second tab of the Items panel).
    ///
    /// Architecture
    /// ────────────
    /// • A two-ScrollRect design:
    ///     - _headerScroll  — horizontal-only, holds the header strip (sticky header).
    ///     - _tableScroll   — horizontal + vertical, holds all data rows.
    ///   Both share the same horizontal scroll position via
    ///   <see cref="SyncHeaderScroll"/> so the header tracks the body.
    ///
    /// • The body is VIRTUALISED: only the rows inside the viewport (plus a small
    ///   overscan) exist as GameObjects, placed by index at absolute positions. The
    ///   content rect is sized to <c>rows x TABLE_ROW_H</c> so the scrollbar range is
    ///   right, and <see cref="UpdateVisibleRows"/> realises / drops rows as the body
    ///   scrolls. Measured before this: 38 columns x 180 items = 6,840 live widgets on
    ///   open, at ~0.48 ms each — a 3.5 s freeze every time F7 was pressed, and 52 % of
    ///   the entire EditMode suite inside 37 tests. Nothing in a cell was expensive;
    ///   the volume was.
    ///
    /// • Columns are driven purely by <see cref="ItemTableColumns.All"/>. Adding a
    ///   field to the registry is the only change required to extend the table.
    ///
    /// • Inline editing mutates the <see cref="ItemDefinition"/> ScriptableObject
    ///   and calls EditorUtility.SetDirty in UNITY_EDITOR builds. At runtime it
    ///   mutates the in-memory SO then calls <see cref="RefreshPicker"/> so the grid
    ///   thumbnail and label stay in sync.
    /// </summary>
    public partial class ItemsRuntimeEditor
    {
        // ── Table layout constants ────────────────────────────────────────────

        private const float TABLE_ROW_H            = 26f;
        private const float TABLE_HEADER_H         = 24f;
        private const float TABLE_CELL_PAD_H       =  4f;  // left+right padding inside each cell
        private const float TABLE_SB_W             = 12f;  // scrollbar width
        private const float TABLE_SPRITE_SZ        = 20f;  // thumbnail size (square)
        private const float TABLE_CATEGORY_BAND_H  =  3f;  // colored category strip atop each header cell

        // Status-bar text restored when the cursor leaves a header cell. We
        // capture the live text on first hover so any prior toast / hint is
        // preserved across the hover gesture.
        private string _statusBeforeHeaderHover;
        private bool   _hoveringHeader;

        // Set of column headers (matching ItemTableColumn.Header verbatim) that
        // the user has hidden via the columns config popup. Persisted to
        // PlayerPrefs as a comma-separated string so the choice survives across
        // sessions — see ItemsRuntimeEditor.TableColumnsConfig.cs for the popup
        // builder + persistence helpers.
        private readonly HashSet<string> _hiddenColumns
            = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>True when the column should be drawn in the table view.</summary>
        internal bool IsColumnVisible(ItemTableColumn col)
            => col != null && !_hiddenColumns.Contains(col.Header);

        // Header scroll rect — horizontal only, tracks body's normalizedPosition.x
        private ScrollRect _headerScroll;
        // Body scroll rect — horizontal + vertical
        private ScrollRect _tableScroll;
        // Content container inside _tableScroll (rows land here)
        private RectTransform _tableBodyContent;
        // Content container inside _headerScroll (header cells land here)
        private RectTransform _tableHeaderContent;

        // ── Virtualisation state ─────────────────────────────────────────────

        // Rows that currently EXIST, keyed by their index into _filtered. Everything
        // outside the realised window is nothing at all — not hidden, not pooled.
        private readonly Dictionary<int, GameObject> _tableRowsByIndex
            = new Dictionary<int, GameObject>();
        private readonly List<int> _rowScratch = new List<int>();

        // The window realised by the last UpdateVisibleRows, so a scroll that does not
        // cross a row boundary costs one comparison and nothing else.
        private int _visibleFirst = -1;
        private int _visibleLast  = -1;

        // Rows kept alive above and below the viewport so a wheel tick never shows an
        // empty band before the next frame fills it.
        private const int TABLE_OVERSCAN_ROWS = 4;

        // uGUI performs no layout in Edit Mode, so a viewport can legitimately report a
        // zero-height rect (see the "uGUI does not lay out in EditMode" note in CLAUDE.md).
        // Realising this many rows there keeps the table usable without ever falling
        // back to "all of them", which is the exact thing virtualisation exists to avoid.
        private const int TABLE_FALLBACK_VISIBLE_ROWS = 24;

        // ── Public wiring: called by ItemsEditorUIBuilder to hand off the two
        //    ScrollRects created during BuildItemsPanel. ──────────────────────

        /// <summary>
        /// Receives the two pre-built ScrollRects from the UI builder.
        /// Must be called before <see cref="RefreshTable"/> is ever invoked.
        /// </summary>
        internal void SetTableScrollRects(ScrollRect headerScroll, ScrollRect bodyScroll,
            RectTransform headerContent, RectTransform bodyContent)
        {
            _headerScroll       = headerScroll;
            _tableScroll        = bodyScroll;
            _tableHeaderContent = headerContent;
            _tableBodyContent   = bodyContent;

            // Mirror body's horizontal position → header every time body scrolls.
            if (_tableScroll != null)
                _tableScroll.onValueChanged.AddListener(OnTableScrolled);

            BuildTableHeader();
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        /// <summary>
        /// Resets the table for the current <c>_filtered</c>. Call this whenever
        /// <c>_filtered</c> changes (same code path as RefreshPicker so both views stay
        /// in sync) or when the visible column set changes.
        ///
        /// <para>Two side-effects on the body content rect are load-bearing. Its
        /// <c>sizeDelta.x</c> is the total column width, or the horizontal scrollbar's
        /// draggable range collapses to zero and rows appear "stuck". Its
        /// <c>sizeDelta.y</c> is <c>rows x TABLE_ROW_H</c>: with no layout group and no
        /// <c>ContentSizeFitter</c> on the body — both removed on purpose, because a
        /// layout group would stack whatever rows happen to exist from the top and
        /// fight the absolute placement — this is the ONLY thing that tells the
        /// ScrollRect how tall the table is.</para>
        /// </summary>
        private void RefreshTable()
        {
            if (_tableBodyContent == null) return;

            DestroyAllTableRows();

            float totalW = ComputeTotalWidth();
            _tableBodyContent.sizeDelta = new Vector2(totalW, _filtered.Count * TABLE_ROW_H);

            UpdateVisibleRows();
        }

        /// <summary>
        /// Realises the rows inside the viewport (plus overscan) and drops the ones
        /// outside it. Cheap when nothing crossed a row boundary; otherwise it touches
        /// only the delta, so a wheel tick that moves three rows builds three rows.
        ///
        /// <para>Row objects are created in index order, so after a fresh
        /// <see cref="RefreshTable"/> the body's first child is the first realised row —
        /// which is what the column-config tests read through <c>GetChild(0)</c>.</para>
        /// </summary>
        private void UpdateVisibleRows()
        {
            if (_tableBodyContent == null) return;

            int count = _filtered.Count;
            if (count == 0)
            {
                DestroyAllTableRows();
                return;
            }

            float viewportH = ResolveTableViewportHeight();
            int visibleRows = viewportH > 1f
                ? Mathf.CeilToInt(viewportH / TABLE_ROW_H)
                : TABLE_FALLBACK_VISIBLE_ROWS;

            // The content is top-anchored with a top pivot, so scrolling DOWN the list
            // moves it up and anchoredPosition.y grows positive.
            float scrollY = Mathf.Max(0f, _tableBodyContent.anchoredPosition.y);
            int first = Mathf.FloorToInt(scrollY / TABLE_ROW_H) - TABLE_OVERSCAN_ROWS;
            first = Mathf.Clamp(first, 0, count - 1);
            int last = Mathf.Clamp(first + visibleRows + TABLE_OVERSCAN_ROWS * 2, 0, count - 1);

            if (first == _visibleFirst && last == _visibleLast) return;

            _rowScratch.Clear();
            foreach (var kv in _tableRowsByIndex)
                if (kv.Key < first || kv.Key > last) _rowScratch.Add(kv.Key);
            for (int i = 0; i < _rowScratch.Count; i++)
            {
                DestroyRowObject(_tableRowsByIndex[_rowScratch[i]]);
                _tableRowsByIndex.Remove(_rowScratch[i]);
            }

            for (int i = first; i <= last; i++)
            {
                if (_tableRowsByIndex.ContainsKey(i)) continue;
                _tableRowsByIndex[i] = BuildTableRow(_filtered[i], i);
            }

            _visibleFirst = first;
            _visibleLast  = last;
        }

        private float ResolveTableViewportHeight()
        {
            if (_tableScroll == null) return 0f;
            var vp = _tableScroll.viewport != null
                ? _tableScroll.viewport
                : _tableScroll.GetComponent<RectTransform>();
            return vp != null ? vp.rect.height : 0f;
        }

        private void DestroyAllTableRows()
        {
            foreach (var kv in _tableRowsByIndex) DestroyRowObject(kv.Value);
            _tableRowsByIndex.Clear();

            // Anything else under the body — a row from an older build, a stray child —
            // goes as well, so the body only ever holds what this class put there.
            if (_tableBodyContent != null)
                for (int i = _tableBodyContent.childCount - 1; i >= 0; i--)
                    DestroyRowObject(_tableBodyContent.GetChild(i).gameObject);

            _visibleFirst = -1;
            _visibleLast  = -1;
        }

        private static void DestroyRowObject(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        // ── Header ────────────────────────────────────────────────────────────

        private void BuildTableHeader()
        {
            if (_tableHeaderContent == null) return;

            // Clear any stale header (called once; safe to repeat for hot-reload).
            for (int i = _tableHeaderContent.childCount - 1; i >= 0; i--)
            {
                var ch = _tableHeaderContent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(ch);
                else DestroyImmediate(ch);
            }

            // Set total content width to match the sum of all column widths so
            // the horizontal scrollbar range is correct.
            float totalW = ComputeTotalWidth();
            _tableHeaderContent.sizeDelta = new Vector2(totalW, TABLE_HEADER_H);

            var cols = ItemTableColumns.All;
            float xCursor = 0f;
            int   placed  = 0;   // counts only visible columns; drives "first cell" border-skip
            for (int c = 0; c < cols.Count; c++)
            {
                var col = cols[c];
                if (!IsColumnVisible(col)) continue;
                var cellGo = UIFactory.CreateUI("Hdr_" + col.Header, _tableHeaderContent);
                var cellRt = cellGo.GetComponent<RectTransform>();
                cellRt.anchorMin        = new Vector2(0f, 0f);
                cellRt.anchorMax        = new Vector2(0f, 1f);
                cellRt.pivot            = new Vector2(0f, 0.5f);
                cellRt.anchoredPosition = new Vector2(xCursor, 0f);
                cellRt.sizeDelta        = new Vector2(col.Width, 0f);

                var bg = cellGo.AddComponent<Image>();
                bg.color = TileEditorTheme.HeaderBg;

                if (placed > 0)
                {
                    // Left-border divider (1 px wide, full height child) — drawn
                    // on this cell's left edge so adjacent groups read as
                    // separate columns even when their backgrounds match.
                    var div    = UIFactory.CreateUI("Div", cellGo.transform);
                    var divRt  = div.GetComponent<RectTransform>();
                    divRt.anchorMin        = new Vector2(0f, 0f);
                    divRt.anchorMax        = new Vector2(0f, 1f);
                    divRt.pivot            = new Vector2(0f, 0.5f);
                    divRt.anchoredPosition = Vector2.zero;
                    divRt.sizeDelta        = new Vector2(1f, 0f);
                    div.AddComponent<Image>().color = TileEditorTheme.Separator;
                }

                // Category band — 3 px coloured strip pinned to the top edge of
                // the header cell. Lets the user spot which group each column
                // belongs to (Identity, Equip, Economy, Consumable, …).
                var bandGo = UIFactory.CreateUI("CategoryBand", cellGo.transform);
                var bandRt = bandGo.GetComponent<RectTransform>();
                bandRt.anchorMin        = new Vector2(0f, 1f);
                bandRt.anchorMax        = new Vector2(1f, 1f);
                bandRt.pivot            = new Vector2(0.5f, 1f);
                bandRt.anchoredPosition = Vector2.zero;
                bandRt.sizeDelta        = new Vector2(0f, TABLE_CATEGORY_BAND_H);
                bandGo.AddComponent<Image>().color = ItemTableColumns.CategoryColor(col.Category);

                var tmp = UILabel.AddCenteredText(cellGo.transform,
                    col.Header, 9f, FontStyles.Bold, TileEditorTheme.HeaderTitle);
                tmp.enableWordWrapping = false;
                tmp.overflowMode       = TextOverflowModes.Truncate;
                tmp.margin             = new Vector4(TABLE_CELL_PAD_H, TABLE_CATEGORY_BAND_H,
                                                     TABLE_CELL_PAD_H, 0f);

                // Hover handler — exposes the column's tooltip text in the
                // status bar (no popup widget needed; reuses existing chrome).
                AttachHeaderHover(cellGo, col);

                xCursor += col.Width;
                placed++;
            }
        }

        // ── Header tooltip (hover -> status text) ─────────────────────────────

        /// <summary>
        /// Wires <see cref="EventTrigger"/> Enter/Exit handlers onto a header
        /// cell so the column's tooltip text shows in the panel's status bar
        /// while the cursor sits on the cell. The previous status text is
        /// captured on first enter and restored on exit so any prior toast or
        /// hint is preserved across the hover gesture.
        /// </summary>
        private void AttachHeaderHover(GameObject cellGo, ItemTableColumn col)
        {
            if (cellGo == null) return;

            var tip = !string.IsNullOrEmpty(col.Tooltip)
                ? $"<b>{col.Header}</b> ({col.Category}) — {col.Tooltip}"
                : $"<b>{col.Header}</b> ({col.Category})";

            var trigger = cellGo.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (!_hoveringHeader && _uiRefs.StatusText != null)
                    _statusBeforeHeaderHover = _uiRefs.StatusText.text;
                _hoveringHeader = true;
                SetStatus(tip);
            });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                _hoveringHeader = false;
                if (_statusBeforeHeaderHover != null)
                {
                    SetStatus(_statusBeforeHeaderHover);
                    _statusBeforeHeaderHover = null;
                }
            });
            trigger.triggers.Add(exit);
        }

        // ── Row builder ───────────────────────────────────────────────────────

        private GameObject BuildTableRow(ItemDefinition def, int rowIndex)
        {
            float totalW = ComputeTotalWidth();

            var rowGo = UIFactory.CreateUI("Row_" + (def.itemId ?? rowIndex.ToString()),
                _tableBodyContent);
            var rowRt = rowGo.GetComponent<RectTransform>();
            // Placed by INDEX. The body is virtualised, so the rows that exist are an
            // arbitrary window of the list and nothing may stack them from the top.
            rowRt.anchorMin        = new Vector2(0f, 1f);
            rowRt.anchorMax        = new Vector2(0f, 1f);
            rowRt.pivot            = new Vector2(0f, 1f);
            rowRt.anchoredPosition = new Vector2(0f, -rowIndex * TABLE_ROW_H);

            // Row background — alternating zebra stripe.
            var rowBg = rowGo.AddComponent<Image>();
            rowBg.color = rowIndex % 2 == 0
                ? new Color(0.10f, 0.11f, 0.14f, 0.90f)
                : new Color(0.12f, 0.13f, 0.17f, 0.90f);

            // Cells inside the row are positioned absolutely, exactly like the header's.

            var cols = ItemTableColumns.All;
            float xCursor = 0f;
            for (int c = 0; c < cols.Count; c++)
            {
                var col    = cols[c];
                if (!IsColumnVisible(col)) continue;
                var cellGo = UIFactory.CreateUI("Cell_" + col.Header, rowGo.transform);
                var cellRt = cellGo.GetComponent<RectTransform>();
                cellRt.anchorMin        = new Vector2(0f, 0f);
                cellRt.anchorMax        = new Vector2(0f, 1f);
                cellRt.pivot            = new Vector2(0f, 0.5f);
                cellRt.anchoredPosition = new Vector2(xCursor, 0f);
                cellRt.sizeDelta        = new Vector2(col.Width, 0f);

                BuildCell(cellGo.transform, col, def);

                xCursor += col.Width;
            }

            // Force the row width to match the content width so cells don't clip.
            rowRt.sizeDelta = new Vector2(totalW, TABLE_ROW_H);

            return rowGo;
        }

        // ── Cell builder (dispatches by EditorKind) ───────────────────────────

        private void BuildCell(Transform cellT, ItemTableColumn col, ItemDefinition def)
        {
            switch (col.EditorKind)
            {
                case ItemTableEditorKind.Text:
                case ItemTableEditorKind.Int:
                case ItemTableEditorKind.Float:
                    BuildTextCell(cellT, col, def);
                    break;

                case ItemTableEditorKind.Toggle:
                    BuildToggleCell(cellT, col, def);
                    break;

                case ItemTableEditorKind.Dropdown:
                    BuildDropdownCell(cellT, col, def);
                    break;

                case ItemTableEditorKind.SpriteThumbnail:
                    BuildSpriteCell(cellT, col, def);
                    break;
            }
        }

        private void BuildTextCell(Transform cellT, ItemTableColumn col, ItemDefinition def)
        {
            if (col.SetString == null)
            {
                // Read-only label.
                var tmp = UILabel.AddCenteredText(cellT,
                    col.GetString(def), 10f, FontStyles.Normal, UITheme.TEXT_PRIMARY);
                tmp.enableWordWrapping = false;
                tmp.overflowMode       = TextOverflowModes.Truncate;
                tmp.margin             = new Vector4(TABLE_CELL_PAD_H, 0f, TABLE_CELL_PAD_H, 0f);
                return;
            }

            var contentType = col.EditorKind == ItemTableEditorKind.Int
                ? TMP_InputField.ContentType.IntegerNumber
                : col.EditorKind == ItemTableEditorKind.Float
                    ? TMP_InputField.ContentType.DecimalNumber
                    : TMP_InputField.ContentType.Standard;

            var input = UIInputField.AddCommit(cellT,
                col.GetString(def),
                v => OnCellCommit(col, def, v),
                TABLE_ROW_H, 10f);
            input.contentType    = contentType;
            // Stretch input to fill the cell.
            var inputRt = input.GetComponent<RectTransform>();
            inputRt.anchorMin = Vector2.zero;
            inputRt.anchorMax = Vector2.one;
            inputRt.sizeDelta = Vector2.zero;
        }

        private void BuildToggleCell(Transform cellT, ItemTableColumn col, ItemDefinition def)
        {
            var holderGo = UIFactory.CreateUI("ToggleHolder", cellT);
            var holderRt = holderGo.GetComponent<RectTransform>();
            holderRt.anchorMin = Vector2.zero;
            holderRt.anchorMax = Vector2.one;
            holderRt.sizeDelta = Vector2.zero;

            var tGo   = UIFactory.CreateUI("Toggle", holderGo.transform);
            var tRt   = tGo.GetComponent<RectTransform>();
            const float tSz = 18f;
            tRt.anchorMin        = new Vector2(0.5f, 0.5f);
            tRt.anchorMax        = new Vector2(0.5f, 0.5f);
            tRt.pivot            = new Vector2(0.5f, 0.5f);
            tRt.anchoredPosition = Vector2.zero;
            tRt.sizeDelta        = new Vector2(tSz, tSz);

            var tImg   = tGo.AddComponent<Image>();
            tImg.color = UITheme.BG_SURFACE;

            var toggle       = tGo.AddComponent<Toggle>();
            toggle.targetGraphic = tImg;

            var checkGo = UIFactory.CreateUI("Check", tGo.transform);
            UIFactory.StretchFill(checkGo);
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color   = UITheme.ACCENT;
            toggle.graphic   = checkImg;

            bool current = false;
            bool.TryParse(col.GetString(def), out current);
            toggle.isOn = current;

            if (col.SetString != null)
                toggle.onValueChanged.AddListener(v => OnCellCommit(col, def, v.ToString()));
        }

        private void BuildDropdownCell(Transform cellT, ItemTableColumn col, ItemDefinition def)
        {
            var dGo = UIFactory.CreateUI("Dropdown", cellT);
            var dRt = dGo.GetComponent<RectTransform>();
            dRt.anchorMin = Vector2.zero;
            dRt.anchorMax = Vector2.one;
            dRt.sizeDelta = Vector2.zero;

            var bg = dGo.AddComponent<Image>();
            bg.color = UITheme.BG_SURFACE;

            var dd = dGo.AddComponent<TMP_Dropdown>();
            dd.targetGraphic = bg;
            dd.ClearOptions();
            var opts = new System.Collections.Generic.List<string>(col.DropdownOptions);
            dd.AddOptions(opts);
            dd.SetValueWithoutNotify(col.GetDropdownIndex(def));
            dd.onValueChanged.AddListener(i => OnDropdownCellCommit(col, def, i));

            // Dropdown label (child TMP added by TMP_Dropdown internals via template).
            // Build a minimal label child so it renders without a Canvas prefab template.
            var labelGo  = UIFactory.CreateUI("Label", dGo.transform);
            UIFactory.StretchFill(labelGo);
            var labelRt  = labelGo.GetComponent<RectTransform>();
            labelRt.offsetMin = new Vector2(4f, 2f);
            labelRt.offsetMax = new Vector2(-4f, -2f);
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.fontSize  = 10f;
            labelTmp.color     = UITheme.TEXT_PRIMARY;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
            labelTmp.enableWordWrapping = false;
            labelTmp.overflowMode       = TextOverflowModes.Truncate;
            dd.captionText = labelTmp;

            // Set caption after wiring so it displays the current value.
            labelTmp.text = col.DropdownOptions.Count > 0
                ? col.DropdownOptions[col.GetDropdownIndex(def)]
                : "";
        }

        private void BuildSpriteCell(Transform cellT, ItemTableColumn col, ItemDefinition def)
        {
            // Determine which sprite getter this column represents by checking
            // its Header and reading the corresponding Sprite field directly.
            Sprite sprite = null;
            switch (col.Header)
            {
                case "icon":      sprite = def.icon;      break;
                case "iconSmall": sprite = def.iconSmall; break;
                case "iconLarge": sprite = def.iconLarge; break;
            }

            var imgGo = UIFactory.CreateUI("SpriteImg", cellT);
            var imgRt = imgGo.GetComponent<RectTransform>();
            imgRt.anchorMin        = new Vector2(0.5f, 0.5f);
            imgRt.anchorMax        = new Vector2(0.5f, 0.5f);
            imgRt.pivot            = new Vector2(0.5f, 0.5f);
            imgRt.anchoredPosition = Vector2.zero;
            imgRt.sizeDelta        = new Vector2(TABLE_SPRITE_SZ, TABLE_SPRITE_SZ);

            var img = imgGo.AddComponent<Image>();
            if (sprite != null)
            {
                img.sprite         = sprite;
                img.preserveAspect = true;
                img.color          = Color.white;
            }
            else
            {
                img.color = new Color(0.25f, 0.25f, 0.30f, 0.60f);
            }
        }

        // ── Commit handlers ───────────────────────────────────────────────────

        private void OnCellCommit(ItemTableColumn col, ItemDefinition def, string value)
        {
            if (col.SetString == null || def == null) return;
            col.SetString(def, value);
            MarkDefinitionDirty(def);
            // Refresh picker so icon / label updates if the name column changed.
            RefreshPicker();
        }

        private void OnDropdownCellCommit(ItemTableColumn col, ItemDefinition def, int index)
        {
            if (col.SetDropdownIndex == null || def == null) return;
            col.SetDropdownIndex(def, index);
            MarkDefinitionDirty(def);
        }

        private static void MarkDefinitionDirty(ItemDefinition def)
        {
            if (def == null) return;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(def);
#endif
        }

        // ── Scroll sync ───────────────────────────────────────────────────────

        /// <summary>
        /// Mirror the body content's horizontal scroll position onto the header
        /// content using <b>absolute pixel offset</b>, not normalized position.
        /// Normalized sync would drift by 12 px (the vertical scrollbar gutter)
        /// because the body and header viewport widths differ. Pixel-mirror is
        /// exact regardless of viewport sizes.
        /// </summary>
        private void OnTableScrolled(Vector2 _normalizedPos)
        {
            if (_tableHeaderContent == null || _tableBodyContent == null) return;
            var hdr = _tableHeaderContent.anchoredPosition;
            hdr.x = _tableBodyContent.anchoredPosition.x;
            _tableHeaderContent.anchoredPosition = hdr;

            // The same callback carries the vertical position, which is what decides
            // which rows exist.
            UpdateVisibleRows();
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private float ComputeTotalWidth()
        {
            float w = 0f;
            var cols = ItemTableColumns.All;
            for (int i = 0; i < cols.Count; i++)
            {
                if (!IsColumnVisible(cols[i])) continue;
                w += cols[i].Width;
            }
            return w;
        }
    }
}
