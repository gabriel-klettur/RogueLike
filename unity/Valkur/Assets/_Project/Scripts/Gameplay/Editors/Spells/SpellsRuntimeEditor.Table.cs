using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — Table view (second tab of the Spells panel).
    ///
    /// Architecture
    /// ────────────
    /// • A two-ScrollRect design:
    ///     - _spellsHeaderScroll  — horizontal-only, holds the header strip (sticky).
    ///     - _spellsTableScroll   — horizontal + vertical, holds all data rows.
    ///   Both share the same horizontal scroll position via
    ///   <see cref="OnSpellsTableScrolled"/> so the header tracks the body.
    ///
    /// • Columns are driven purely by <see cref="SpellTableColumns.All"/>. Adding a
    ///   field to the registry is the only change required to extend the table.
    ///
    /// • Inline editing mutates the <see cref="SpellDefinition"/> ScriptableObject
    ///   and calls EditorUtility.SetDirty in UNITY_EDITOR builds. At runtime it
    ///   mutates the in-memory SO then calls <see cref="RefreshPicker"/> so the grid
    ///   thumbnail and label stay in sync.
    ///
    /// • Mirrors <c>ItemsRuntimeEditor.Table.cs</c> exactly; only the type parameters
    ///   and column registry differ.
    /// </summary>
    public partial class SpellsRuntimeEditor
    {
        // ── Table layout constants ────────────────────────────────────────────

        private const float SPELL_TABLE_ROW_H           = 26f;
        private const float SPELL_TABLE_HEADER_H        = 24f;
        private const float SPELL_TABLE_CELL_PAD_H      =  4f;
        private const float SPELL_TABLE_SB_W            = 12f;
        private const float SPELL_TABLE_SPRITE_SZ       = 20f;
        private const float SPELL_TABLE_CATEGORY_BAND_H =  3f;

        // Status-bar text restored when the cursor leaves a header cell.
        private string _spellStatusBeforeHeaderHover;
        private bool   _spellHoveringHeader;

        // Hidden columns — persisted in PlayerPrefs (see TableColumnsConfig.cs).
        private readonly HashSet<string> _hiddenColumns
            = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>True when the column should be drawn in the table view.</summary>
        internal bool IsColumnVisible(SpellTableColumn col)
            => col != null && !_hiddenColumns.Contains(col.Header);

        // Header ScrollRect — horizontal only, driven programmatically.
        private ScrollRect _spellsHeaderScroll;
        // Body ScrollRect — horizontal + vertical, driven by user input.
        private ScrollRect _spellsTableScroll;
        // Content container inside _spellsTableScroll (rows land here).
        private RectTransform _spellsTableBodyContent;
        // Content container inside _spellsHeaderScroll (header cells land here).
        private RectTransform _spellsTableHeaderContent;

        // Realised rows, by index into _filtered. The table is virtualised: only the rows
        // inside the viewport (plus overscan) exist as GameObjects.
        private readonly Dictionary<int, GameObject> _spellTableRowsByIndex
            = new Dictionary<int, GameObject>();
        private readonly List<int> _spellRowScratch = new List<int>();

        // The window realised by the last UpdateVisibleSpellRows, so a scroll that does not
        // cross a row boundary costs one comparison and nothing else.
        private int _spellVisibleFirst = -1;
        private int _spellVisibleLast  = -1;

        /// <summary>Which columns the pooled rows were built for. See DestroyAllSpellTableRows.</summary>
        private int _spellRowShapeSignature = -1;

        // Rows kept alive above and below the viewport so a wheel tick never shows an empty
        // band before the next frame fills it.
        private const int SPELL_TABLE_OVERSCAN_ROWS = 4;

        // uGUI performs no layout in Edit Mode, so a viewport can legitimately report a
        // zero-height rect. Realising this many rows there keeps the table usable without
        // ever falling back to "all of them", which is what virtualisation exists to avoid.
        private const int SPELL_TABLE_FALLBACK_VISIBLE_ROWS = 24;

        // ── Public wiring: called by SpellsEditorUIBuilder ───────────────────

        /// <summary>
        /// Receives the two pre-built ScrollRects from the UI builder.
        /// Must be called before <see cref="RefreshTable"/> is ever invoked.
        /// </summary>
        internal void SetTableScrollRects(ScrollRect headerScroll, ScrollRect bodyScroll,
            RectTransform headerContent, RectTransform bodyContent)
        {
            _spellsHeaderScroll       = headerScroll;
            _spellsTableScroll        = bodyScroll;
            _spellsTableHeaderContent = headerContent;
            _spellsTableBodyContent   = bodyContent;

            if (_spellsTableScroll != null)
                _spellsTableScroll.onValueChanged.AddListener(OnSpellsTableScrolled);

            BuildTableHeader();
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds all data rows from <see cref="_filtered"/>. Call this whenever
        /// <see cref="_filtered"/> changes (same code path as RefreshPicker so both
        /// views stay in sync).
        /// </summary>
        private void RefreshTable()
        {
            if (_spellsTableBodyContent == null) return;

            DestroyAllSpellTableRows();

            // Both axes are load-bearing. sizeDelta.x is the total column width, or the
            // horizontal scrollbar's range collapses to zero and the rows appear stuck.
            // sizeDelta.y is rows x ROW_H: with no layout group and no ContentSizeFitter on
            // the body, this is the ONLY thing that tells the ScrollRect how tall the table
            // is, and therefore the only thing that makes the vertical scrollbar honest.
            _spellsTableBodyContent.sizeDelta = new Vector2(
                ComputeSpellTableTotalWidth(), _filtered.Count * SPELL_TABLE_ROW_H);

            UpdateVisibleSpellRows();
            _tableDirty = false;
        }

        /// <summary>
        /// Realise the rows inside the viewport (plus overscan) and drop the ones outside it.
        /// Cheap when nothing crossed a row boundary; otherwise it touches only the delta, so
        /// a wheel tick that moves three rows builds three rows.
        /// </summary>
        private void UpdateVisibleSpellRows()
        {
            if (_spellsTableBodyContent == null) return;

            int count = _filtered.Count;
            if (count == 0) { DestroyAllSpellTableRows(); return; }

            float viewportH = ResolveSpellTableViewportHeight();
            int visibleRows = viewportH > 1f
                ? Mathf.CeilToInt(viewportH / SPELL_TABLE_ROW_H)
                : SPELL_TABLE_FALLBACK_VISIBLE_ROWS;

            // The content is top-anchored with a top pivot, so scrolling DOWN the list moves
            // it up and anchoredPosition.y grows positive.
            float scrollY = Mathf.Max(0f, _spellsTableBodyContent.anchoredPosition.y);
            int first = Mathf.FloorToInt(scrollY / SPELL_TABLE_ROW_H) - SPELL_TABLE_OVERSCAN_ROWS;
            first = Mathf.Clamp(first, 0, count - 1);
            int last = Mathf.Clamp(first + visibleRows + SPELL_TABLE_OVERSCAN_ROWS * 2, 0, count - 1);

            if (first == _spellVisibleFirst && last == _spellVisibleLast) return;

            _spellRowScratch.Clear();
            foreach (var kv in _spellTableRowsByIndex)
                if (kv.Key < first || kv.Key > last) _spellRowScratch.Add(kv.Key);
            for (int i = 0; i < _spellRowScratch.Count; i++)
            {
                int index = _spellRowScratch[i];
                ForgetSpellRowKey(index);
                ReleaseSpellRow(_spellTableRowsByIndex[index]);
                _spellTableRowsByIndex.Remove(index);
            }

            // Built in index order, so after a fresh RefreshTable the body's first child is
            // the first realised row -- which is what the editor tests read via GetChild(0).
            for (int i = first; i <= last; i++)
            {
                if (_spellTableRowsByIndex.ContainsKey(i)) continue;
                _spellTableRowsByIndex[i] = AcquireSpellRow(_filtered[i], i);
            }

            _spellVisibleFirst = first;
            _spellVisibleLast  = last;
        }

        private float ResolveSpellTableViewportHeight()
        {
            if (_spellsTableScroll == null) return 0f;
            var vp = _spellsTableScroll.viewport != null
                ? _spellsTableScroll.viewport
                : _spellsTableScroll.GetComponent<RectTransform>();
            return vp != null ? vp.rect.height : 0f;
        }

        /// <summary>
        /// Reset the body for a fresh fill.
        ///
        /// <para>WHAT A ROW CAN SURVIVE. A pooled row is reusable exactly while it carries the
        /// columns the table is drawing, and a filter change does not touch those — so on a
        /// filter change, or on closing and reopening the editor, the realised rows are handed
        /// to the POOL rather than destroyed and the next fill rebinds them. Only a column
        /// being shown or hidden makes them the wrong shape, and that is what the signature
        /// detects. Measured, clearing unconditionally made every open of the Table tab pay for
        /// 24 rows built from nothing.</para>
        /// </summary>
        private void DestroyAllSpellTableRows()
        {
            int signature = ComputeSpellRowShapeSignature();
            bool shapeChanged = signature != _spellRowShapeSignature;
            _spellRowShapeSignature = signature;

            _tableRowsByKey.Clear();

            if (shapeChanged)
            {
                _spellTableRowsByIndex.Clear();
                ClearSpellRowPool();
                // Anything else under the body -- a row from an older build, a stray child --
                // goes as well, so the body only ever holds what this class put there.
                DetachAndDestroyChildren(_spellsTableBodyContent);
            }
            else
            {
                foreach (var kv in _spellTableRowsByIndex) ReleaseSpellRow(kv.Value);
                _spellTableRowsByIndex.Clear();
            }

            _spellVisibleFirst = -1;
            _spellVisibleLast  = -1;
        }

        /// <summary>
        /// Identifies the row SHAPE: which columns are visible, in order. Two builds that agree
        /// on this produce rows that are interchangeable, which is the whole precondition
        /// <see cref="RebindSpellRow"/> checks cell by cell — this just answers it once for the
        /// whole table instead of rejecting forty rows one at a time.
        /// </summary>
        private int ComputeSpellRowShapeSignature()
        {
            unchecked
            {
                int hash = 17;
                var cols = SpellTableColumns.All;
                for (int i = 0; i < cols.Count; i++)
                {
                    if (!IsColumnVisible(cols[i])) continue;
                    hash = hash * 31 + (cols[i].Header != null ? cols[i].Header.GetHashCode() : 0);
                }
                return hash;
            }
        }

        /// <summary>Drop one row's selection-repaint entry as that row is de-realised.</summary>
        private void ForgetSpellRowKey(int index)
        {
            if (index < 0 || index >= _filtered.Count) return;
            var def = _filtered[index];
            if (def != null && !string.IsNullOrEmpty(def.spellKey)) _tableRowsByKey.Remove(def.spellKey);
        }

        private static void DetachAndDestroy(GameObject go)
        {
            if (go == null) return;
            go.transform.SetParent(null, false);
            Valkur.Core.SafeDestroy.Of(go);
        }

        // ── Header ────────────────────────────────────────────────────────────

        private void BuildTableHeader()
        {
            if (_spellsTableHeaderContent == null) return;

            for (int i = _spellsTableHeaderContent.childCount - 1; i >= 0; i--)
            {
                var ch = _spellsTableHeaderContent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(ch);
                else DestroyImmediate(ch);
            }

            float totalW = ComputeSpellTableTotalWidth();
            _spellsTableHeaderContent.sizeDelta = new Vector2(totalW, SPELL_TABLE_HEADER_H);

            var cols = SpellTableColumns.All;
            float xCursor = 0f;
            int placed = 0;
            for (int c = 0; c < cols.Count; c++)
            {
                var col = cols[c];
                if (!IsColumnVisible(col)) continue;

                var cellGo = UIFactory.CreateUI("Hdr_" + col.Header, _spellsTableHeaderContent);
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
                    // Left-border divider (1 px wide, full height).
                    var div    = UIFactory.CreateUI("Div", cellGo.transform);
                    var divRt  = div.GetComponent<RectTransform>();
                    divRt.anchorMin        = new Vector2(0f, 0f);
                    divRt.anchorMax        = new Vector2(0f, 1f);
                    divRt.pivot            = new Vector2(0f, 0.5f);
                    divRt.anchoredPosition = Vector2.zero;
                    divRt.sizeDelta        = new Vector2(1f, 0f);
                    div.AddComponent<Image>().color = TileEditorTheme.Separator;
                }

                // Category band — 3 px coloured strip at the top edge.
                var bandGo = UIFactory.CreateUI("CategoryBand", cellGo.transform);
                var bandRt = bandGo.GetComponent<RectTransform>();
                bandRt.anchorMin        = new Vector2(0f, 1f);
                bandRt.anchorMax        = new Vector2(1f, 1f);
                bandRt.pivot            = new Vector2(0.5f, 1f);
                bandRt.anchoredPosition = Vector2.zero;
                bandRt.sizeDelta        = new Vector2(0f, SPELL_TABLE_CATEGORY_BAND_H);
                bandGo.AddComponent<Image>().color = SpellTableColumns.CategoryColor(col.Category);

                var tmp = UILabel.AddCenteredText(cellGo.transform,
                    col.Header, 9f, FontStyles.Bold, TileEditorTheme.HeaderTitle);
                tmp.enableWordWrapping = false;
                tmp.overflowMode       = TextOverflowModes.Truncate;
                tmp.margin             = new Vector4(SPELL_TABLE_CELL_PAD_H, SPELL_TABLE_CATEGORY_BAND_H,
                                                     SPELL_TABLE_CELL_PAD_H, 0f);

                AttachSpellHeaderHover(cellGo, col);

                xCursor += col.Width;
                placed++;
            }
        }

        // ── Header tooltip (hover -> status bar) ──────────────────────────────

        private void AttachSpellHeaderHover(GameObject cellGo, SpellTableColumn col)
        {
            if (cellGo == null) return;

            var tip = !string.IsNullOrEmpty(col.Tooltip)
                ? $"<b>{col.Header}</b> ({col.Category}) — {col.Tooltip}"
                : $"<b>{col.Header}</b> ({col.Category})";

            var trigger = cellGo.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (!_spellHoveringHeader && _uiRefs.StatusText != null)
                    _spellStatusBeforeHeaderHover = _uiRefs.StatusText.text;
                _spellHoveringHeader = true;
                SetStatus(tip);
            });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                _spellHoveringHeader = false;
                if (_spellStatusBeforeHeaderHover != null)
                {
                    SetStatus(_spellStatusBeforeHeaderHover);
                    _spellStatusBeforeHeaderHover = null;
                }
            });
            trigger.triggers.Add(exit);
        }

        // ── Row builder ───────────────────────────────────────────────────────

        private static readonly Color ROW_ZEBRA_A  = new Color(0.10f, 0.11f, 0.14f, 0.90f);
        private static readonly Color ROW_ZEBRA_B  = new Color(0.12f, 0.13f, 0.17f, 0.90f);
        private static readonly Color ROW_SELECTED = new Color(0.22f, 0.35f, 0.55f, 0.95f);

        /// <summary>
        /// How much a row lifts under the pointer. ADDED to whatever the row is resting at,
        /// so one value serves the zebra stripes and the selected row alike. Shared because
        /// three places set a row's Button colours — the builder, the selection repaint and
        /// the recycler — and a hover that differed between them would flicker as a row was
        /// reused.
        /// </summary>
        internal static readonly Color ROW_HOVER_LIFT = new Color(0.05f, 0.05f, 0.08f, 0f);

        /// <summary>The plate behind a sprite cell that has no sprite to show.</summary>
        internal static readonly Color SPRITE_CELL_EMPTY_BG = new Color(0.25f, 0.25f, 0.30f, 0.60f);

        private GameObject BuildSpellTableRow(SpellDefinition def, int rowIndex)
        {
            float totalW = ComputeSpellTableTotalWidth();

            var rowGo = UIFactory.CreateUI(
                "Row_" + (def.spellKey ?? rowIndex.ToString()), _spellsTableBodyContent);
            var rowRt = rowGo.GetComponent<RectTransform>();
            // Placed by INDEX. The body is virtualised, so the rows that exist are an
            // arbitrary window of the list and nothing may stack them from the top.
            rowRt.anchorMin        = new Vector2(0f, 1f);
            rowRt.anchorMax        = new Vector2(0f, 1f);
            rowRt.pivot            = new Vector2(0f, 1f);
            rowRt.anchoredPosition = new Vector2(0f, -rowIndex * SPELL_TABLE_ROW_H);

            var rowBg = rowGo.AddComponent<Image>();
            rowBg.color = (def.spellKey == _selectedKey)
                ? ROW_SELECTED
                : rowIndex % 2 == 0 ? ROW_ZEBRA_A : ROW_ZEBRA_B;

            // Click anywhere on the row → select this spell and update Properties.
            var capturedKey = def.spellKey;
            var btn = rowGo.AddComponent<Button>();
            btn.targetGraphic = rowBg;
            var bc = btn.colors;
            bc.normalColor      = rowBg.color;
            bc.highlightedColor = rowBg.color + ROW_HOVER_LIFT;
            bc.pressedColor     = ROW_SELECTED;
            bc.selectedColor    = ROW_SELECTED;
            bc.fadeDuration     = 0.08f;
            btn.colors          = bc;
            btn.onClick.AddListener(() => SelectSpell(capturedKey));
            if (!string.IsNullOrEmpty(capturedKey))
                _tableRowsByKey[capturedKey] = new RowRefs { Background = rowBg, Button = btn, Index = rowIndex };

            var cols = SpellTableColumns.All;
            float xCursor = 0f;
            for (int c = 0; c < cols.Count; c++)
            {
                var col = cols[c];
                if (!IsColumnVisible(col)) continue;

                var cellGo = UIFactory.CreateUI("Cell_" + col.Header, rowGo.transform);
                var cellRt = cellGo.GetComponent<RectTransform>();
                cellRt.anchorMin        = new Vector2(0f, 0f);
                cellRt.anchorMax        = new Vector2(0f, 1f);
                cellRt.pivot            = new Vector2(0f, 0.5f);
                cellRt.anchoredPosition = new Vector2(xCursor, 0f);
                cellRt.sizeDelta        = new Vector2(col.Width, 0f);

                BuildSpellCell(cellGo.transform, col, def);

                xCursor += col.Width;
            }

            rowRt.sizeDelta = new Vector2(totalW, SPELL_TABLE_ROW_H);
            return rowGo;
        }

        // ── Cell builder ──────────────────────────────────────────────────────

        private void BuildSpellCell(Transform cellT, SpellTableColumn col, SpellDefinition def)
        {
            switch (col.EditorKind)
            {
                case SpellTableEditorKind.Text:
                case SpellTableEditorKind.Int:
                case SpellTableEditorKind.Float:
                    BuildSpellTextCell(cellT, col, def);
                    break;

                case SpellTableEditorKind.Toggle:
                    BuildSpellToggleCell(cellT, col, def);
                    break;

                case SpellTableEditorKind.Dropdown:
                    BuildSpellDropdownCell(cellT, col, def);
                    break;

                case SpellTableEditorKind.SpriteThumbnail:
                    BuildSpellSpriteCell(cellT, col, def);
                    break;
            }
        }

        private void BuildSpellTextCell(Transform cellT, SpellTableColumn col, SpellDefinition def)
        {
            if (col.SetString == null)
            {
                var tmp = UILabel.AddCenteredText(cellT,
                    col.GetString(def), 10f, FontStyles.Normal, UITheme.TEXT_PRIMARY);
                tmp.enableWordWrapping = false;
                tmp.overflowMode       = TextOverflowModes.Truncate;
                tmp.margin             = new Vector4(SPELL_TABLE_CELL_PAD_H, 0f, SPELL_TABLE_CELL_PAD_H, 0f);
                return;
            }

            // Rests as a label and becomes a real input field on click, which also focuses
            // it. See SpellsRuntimeEditor.TableCells.cs for the measurement behind that.
            BuildLazyEditableCell(cellT, col, def);
        }

        private void BuildSpellToggleCell(Transform cellT, SpellTableColumn col, SpellDefinition def)
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
                toggle.onValueChanged.AddListener(v => OnSpellCellCommit(col, def, v.ToString()));
        }

        private void BuildSpellDropdownCell(Transform cellT, SpellTableColumn col, SpellDefinition def)
        {
            var dGo = UIFactory.CreateUI("Dropdown", cellT);
            var dRt = dGo.GetComponent<RectTransform>();
            dRt.anchorMin = Vector2.zero;
            dRt.anchorMax = Vector2.one;
            dRt.sizeDelta = Vector2.zero;

            // Same builder the properties panel uses. A bare AddComponent<TMP_Dropdown>
            // has no template, so the cell rendered fine and threw the moment anyone
            // clicked it — the list could never open.
            var dd = UIDropdown.Add(dGo.transform, col.DropdownOptions,
                                    col.GetDropdownIndex(def), fontSize: 10f);
            dd.onValueChanged.AddListener(i => OnSpellDropdownCellCommit(col, def, i));
        }

        private void BuildSpellSpriteCell(Transform cellT, SpellTableColumn col, SpellDefinition def)
        {
            // The "sprite" column shows the HUD icon (transparent PNG) preferentially,
            // falling back to the legacy in-world sprite for unmigrated spells.
            Sprite sprite = null;
            if (col.Header == "sprite")
                sprite = IceLanceArt.ResolveIcon(def);

            // Solid black backdrop so the alpha-transparent HUD icons read
            // against the row's striped background.
            var bgGo = UIFactory.CreateUI("SpriteBg", cellT);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin        = new Vector2(0.5f, 0.5f);
            bgRt.anchorMax        = new Vector2(0.5f, 0.5f);
            bgRt.pivot            = new Vector2(0.5f, 0.5f);
            bgRt.anchoredPosition = Vector2.zero;
            bgRt.sizeDelta        = new Vector2(SPELL_TABLE_SPRITE_SZ, SPELL_TABLE_SPRITE_SZ);
            var bg = bgGo.AddComponent<Image>();
            bg.color = sprite != null ? Color.black : SPRITE_CELL_EMPTY_BG;
            bg.raycastTarget = false;

            var imgGo = UIFactory.CreateUI("SpriteImg", cellT);
            var imgRt = imgGo.GetComponent<RectTransform>();
            imgRt.anchorMin        = new Vector2(0.5f, 0.5f);
            imgRt.anchorMax        = new Vector2(0.5f, 0.5f);
            imgRt.pivot            = new Vector2(0.5f, 0.5f);
            imgRt.anchoredPosition = Vector2.zero;
            imgRt.sizeDelta        = new Vector2(SPELL_TABLE_SPRITE_SZ, SPELL_TABLE_SPRITE_SZ);

            var img = imgGo.AddComponent<Image>();
            if (sprite != null)
            {
                img.sprite         = sprite;
                img.preserveAspect = true;
                img.color          = Color.white;
            }
            else
            {
                img.enabled = false;
            }
        }

        // ── Commit handlers ───────────────────────────────────────────────────

        private void OnSpellCellCommit(SpellTableColumn col, SpellDefinition def, string value)
        {
            if (col.SetString == null || def == null) return;
            col.SetString(def, value);
            MarkSpellDefinitionDirty(def);
            // The grid may now show a stale name or icon, but it is not on screen — the
            // table is — so it is marked and rebuilt when its tab comes back.
            _gridDirty = true;
        }

        private void OnSpellDropdownCellCommit(SpellTableColumn col, SpellDefinition def, int index)
        {
            if (col.SetDropdownIndex == null || def == null) return;
            col.SetDropdownIndex(def, index);
            MarkSpellDefinitionDirty(def);
            _gridDirty = true;
        }

        private static void MarkSpellDefinitionDirty(SpellDefinition def)
        {
            if (def == null) return;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(def);
#endif
        }

        // ── Scroll sync ───────────────────────────────────────────────────────

        /// <summary>
        /// Mirror the body content's horizontal scroll position onto the header
        /// content using absolute pixel offset. Pixel-mirror is exact regardless
        /// of viewport sizes (normalized sync drifts by the scrollbar gutter width).
        /// </summary>
        private void OnSpellsTableScrolled(Vector2 _normalizedPos)
        {
            if (_spellsTableHeaderContent == null || _spellsTableBodyContent == null) return;
            var hdr = _spellsTableHeaderContent.anchoredPosition;
            hdr.x = _spellsTableBodyContent.anchoredPosition.x;
            _spellsTableHeaderContent.anchoredPosition = hdr;

            // The same callback carries the vertical position, which is what decides which
            // rows exist.
            UpdateVisibleSpellRows();
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private float ComputeSpellTableTotalWidth()
        {
            float w = 0f;
            var cols = SpellTableColumns.All;
            for (int i = 0; i < cols.Count; i++)
            {
                if (!IsColumnVisible(cols[i])) continue;
                w += cols[i].Width;
            }
            return w;
        }
    }
}
