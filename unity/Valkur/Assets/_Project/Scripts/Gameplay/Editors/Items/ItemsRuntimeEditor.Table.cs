using System.Collections.Generic;
using UnityEngine;
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

        private const float TABLE_ROW_H      = 26f;
        private const float TABLE_HEADER_H   = 24f;
        private const float TABLE_CELL_PAD_H =  4f;  // left+right padding inside each cell
        private const float TABLE_SB_W       = 12f;  // scrollbar width
        private const float TABLE_SPRITE_SZ  = 20f;  // thumbnail size (square)

        // Header scroll rect — horizontal only, tracks body's normalizedPosition.x
        private ScrollRect _headerScroll;
        // Body scroll rect — horizontal + vertical
        private ScrollRect _tableScroll;
        // Content container inside _tableScroll (rows land here)
        private RectTransform _tableBodyContent;
        // Content container inside _headerScroll (header cells land here)
        private RectTransform _tableHeaderContent;

        // Cache of row GameObjects for efficient refresh-in-place (rebuild fully on filter change).
        private readonly List<GameObject> _tableRows = new List<GameObject>();

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
        /// Rebuilds all data rows from _filtered. Call this whenever _filtered changes
        /// (same code path as RefreshPicker so both views stay in sync).
        /// </summary>
        private void RefreshTable()
        {
            if (_tableBodyContent == null) return;

            // Destroy old rows.
            for (int i = _tableBodyContent.childCount - 1; i >= 0; i--)
            {
                var child = _tableBodyContent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            _tableRows.Clear();

            // One row per filtered item.
            for (int i = 0; i < _filtered.Count; i++)
            {
                var def = _filtered[i];
                var row = BuildTableRow(def, i);
                _tableRows.Add(row);
            }
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
            for (int c = 0; c < cols.Count; c++)
            {
                var col = cols[c];
                var cellGo = UIFactory.CreateUI("Hdr_" + col.Header, _tableHeaderContent);
                var cellRt = cellGo.GetComponent<RectTransform>();
                cellRt.anchorMin        = new Vector2(0f, 0f);
                cellRt.anchorMax        = new Vector2(0f, 1f);
                cellRt.pivot            = new Vector2(0f, 0.5f);
                cellRt.anchoredPosition = new Vector2(xCursor, 0f);
                cellRt.sizeDelta        = new Vector2(col.Width, 0f);

                var bg = cellGo.AddComponent<Image>();
                bg.color = TileEditorTheme.HeaderBg;

                if (c > 0)
                {
                    // Right-border divider (1 px wide, full height child).
                    var div    = UIFactory.CreateUI("Div", cellGo.transform);
                    var divRt  = div.GetComponent<RectTransform>();
                    divRt.anchorMin        = new Vector2(0f, 0f);
                    divRt.anchorMax        = new Vector2(0f, 1f);
                    divRt.pivot            = new Vector2(0f, 0.5f);
                    divRt.anchoredPosition = Vector2.zero;
                    divRt.sizeDelta        = new Vector2(1f, 0f);
                    div.AddComponent<Image>().color = TileEditorTheme.Separator;
                }

                var tmp = UILabel.AddCenteredText(cellGo.transform,
                    col.Header, 9f, FontStyles.Bold, TileEditorTheme.HeaderTitle);
                tmp.enableWordWrapping = false;
                tmp.overflowMode       = TextOverflowModes.Truncate;

                xCursor += col.Width;
            }
        }

        // ── Row builder ───────────────────────────────────────────────────────

        private GameObject BuildTableRow(ItemDefinition def, int rowIndex)
        {
            float totalW = ComputeTotalWidth();

            var rowGo = UIFactory.CreateUI("Row_" + (def.itemId ?? rowIndex.ToString()),
                _tableBodyContent);
            var rowRt = rowGo.GetComponent<RectTransform>();
            // Rows are laid out vertically by the VLG on _tableBodyContent.
            // Give each row an explicit preferred height so the CSF sizes correctly.
            rowGo.AddComponent<LayoutElement>().preferredHeight = TABLE_ROW_H;

            // Row background — alternating zebra stripe.
            var rowBg = rowGo.AddComponent<Image>();
            rowBg.color = rowIndex % 2 == 0
                ? new Color(0.10f, 0.11f, 0.14f, 0.90f)
                : new Color(0.12f, 0.13f, 0.17f, 0.90f);

            // The row itself holds cells in absolute positions (like the header).
            // We override the VLG by switching it off and placing manually.
            // Actually, rows are children of a VLG content so they stack vertically;
            // cells inside each row are positioned absolutely.

            var cols = ItemTableColumns.All;
            float xCursor = 0f;
            for (int c = 0; c < cols.Count; c++)
            {
                var col    = cols[c];
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

        private void OnTableScrolled(Vector2 normalizedPos)
        {
            if (_headerScroll != null)
                _headerScroll.horizontalNormalizedPosition = normalizedPos.x;
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private static float ComputeTotalWidth()
        {
            float w = 0f;
            var cols = ItemTableColumns.All;
            for (int i = 0; i < cols.Count; i++) w += cols[i].Width;
            return w;
        }
    }
}
