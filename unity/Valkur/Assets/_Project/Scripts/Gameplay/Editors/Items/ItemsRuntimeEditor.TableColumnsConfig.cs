using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.UIKit;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Items Editor — column visibility popup for the Table tab.
    ///
    /// Lets the user pick which of the 38 ItemDefinition columns are
    /// rendered. Reasons to hide columns:
    ///  • Narrow workflows (e.g. balancing damage / cooldowns) only need a
    ///    handful of fields visible at a time.
    ///  • Wide tables make horizontal scrolling exhausting; hiding noise
    ///    columns reduces total width.
    ///
    /// Persistence
    /// ───────────
    /// Hidden column headers are stored in PlayerPrefs as a comma-separated
    /// string under <see cref="HIDDEN_COLUMNS_PREFS_KEY"/>, so the choice
    /// survives Editor restarts and Play / Stop cycles.
    ///
    /// UX
    /// ──
    ///  • Modal popup centred on the canvas, with a translucent scrim.
    ///  • Click the scrim or the "X" button to close.
    ///  • Each row shows a checkbox + a 3 px category-coloured stripe + the
    ///    column header. Categories visually separated by section labels.
    ///  • "All" / "None" / "Reset" buttons at the bottom.
    ///  • The popup is built lazily on first click and re-used afterwards.
    /// </summary>
    public partial class ItemsRuntimeEditor
    {
        private const string HIDDEN_COLUMNS_PREFS_KEY = "Valkur.ItemsEditor.HiddenColumns";

        private const float COLPOP_WIDTH      = 320f;
        private const float COLPOP_HEIGHT     = 460f;
        private const float COLPOP_HEADER_H   =  28f;
        private const float COLPOP_FOOTER_H   =  36f;
        private const float COLPOP_ROW_H      =  22f;
        private const float COLPOP_SECT_H     =  18f;
        private const float COLPOP_STRIPE_W   =   3f;

        private GameObject _columnsPopup;          // built lazily on first open
        private Toggle[]   _columnTogglesByIndex;  // indexed by ItemTableColumns.All
        private TextMeshProUGUI _columnsCountLabel; // header label "(N of M visible)"

        // Hooks called by the bar button → ItemsRuntimeEditor.cs wires this up.
        internal void OpenColumnsConfigPopup()
        {
            if (_columnsPopup == null) BuildColumnsConfigPopup();
            if (_columnsPopup == null) return;
            _columnsPopup.SetActive(true);
            _columnsPopup.transform.SetAsLastSibling();
            RefreshColumnsConfigState();
        }

        internal void CloseColumnsConfigPopup()
        {
            if (_columnsPopup != null) _columnsPopup.SetActive(false);
        }

        // ── Persistence ───────────────────────────────────────────────────────

        /// <summary>
        /// Hydrate <see cref="_hiddenColumns"/> from PlayerPrefs. Called once
        /// from Activate so the user's previous choice is restored on open.
        /// </summary>
        internal void LoadColumnPrefs()
        {
            _hiddenColumns.Clear();
            string blob = PlayerPrefs.GetString(HIDDEN_COLUMNS_PREFS_KEY, "");
            if (string.IsNullOrEmpty(blob)) return;
            foreach (var h in blob.Split(','))
            {
                if (!string.IsNullOrWhiteSpace(h))
                    _hiddenColumns.Add(h.Trim());
            }
        }

        /// <summary>Persist the current hidden-column set so it survives a session.</summary>
        internal void SaveColumnPrefs()
        {
            string blob = string.Join(",", _hiddenColumns);
            PlayerPrefs.SetString(HIDDEN_COLUMNS_PREFS_KEY, blob);
            PlayerPrefs.Save();
        }

        // ── Bar label helper ──────────────────────────────────────────────────

        /// <summary>
        /// Refresh the "Columns: N/M visible" indicator on the config bar.
        /// Called after every toggle so the bar reflects state in real time.
        /// </summary>
        internal void RefreshColumnsCountLabel()
        {
            if (_uiRefs.TableColumnsCountLabel == null) return;
            int total   = ItemTableColumns.All.Count;
            int visible = total - _hiddenColumns.Count;
            _uiRefs.TableColumnsCountLabel.text = $"Columns: {visible}/{total}";
        }

        // ── Popup builder ─────────────────────────────────────────────────────

        private void BuildColumnsConfigPopup()
        {
            if (_canvas == null) return;

            // Root + scrim — full-canvas image that swallows clicks behind the
            // popup and closes it on left-click.
            var rootGo = EditorUIHelpers.CreateUI("ItemsColumnsConfigPopup", _canvas.transform);
            EditorUIHelpers.StretchFill(rootGo);
            var scrim = rootGo.AddComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, 0.55f);
            scrim.raycastTarget = true;

            // Click-on-scrim closes (delegate via EventTrigger so we don't
            // accidentally swallow drags meant for the popup body).
            var scrimTrigger = rootGo.AddComponent<EventTrigger>();
            var scrimClick = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            scrimClick.callback.AddListener(ev =>
            {
                // Only close when the click target IS the scrim (not a child).
                if (ev is PointerEventData ped && ped.pointerCurrentRaycast.gameObject == rootGo)
                    CloseColumnsConfigPopup();
            });
            scrimTrigger.triggers.Add(scrimClick);

            // Popup body — centred panel with header / scroll / footer.
            var bodyGo = EditorUIHelpers.CreateUI("PopupBody", rootGo.transform);
            var bodyRt = bodyGo.GetComponent<RectTransform>();
            bodyRt.anchorMin        = new Vector2(0.5f, 0.5f);
            bodyRt.anchorMax        = new Vector2(0.5f, 0.5f);
            bodyRt.pivot            = new Vector2(0.5f, 0.5f);
            bodyRt.anchoredPosition = Vector2.zero;
            bodyRt.sizeDelta        = new Vector2(COLPOP_WIDTH, COLPOP_HEIGHT);
            bodyGo.AddComponent<Image>().color = TileEditorTheme.PanelBg;
            var ol            = bodyGo.AddComponent<Outline>();
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(1f, 1f);

            // VLG inside the body so header / scroll / footer stack cleanly.
            var bodyVlg = bodyGo.AddComponent<VerticalLayoutGroup>();
            bodyVlg.spacing                = 0f;
            bodyVlg.padding                = new RectOffset(0, 0, 0, 0);
            bodyVlg.childForceExpandWidth  = true;
            bodyVlg.childForceExpandHeight = false;
            bodyVlg.childControlWidth      = true;
            bodyVlg.childControlHeight     = true;

            BuildColumnsPopupHeader(bodyGo.transform);
            BuildColumnsPopupRows(bodyGo.transform);
            BuildColumnsPopupFooter(bodyGo.transform);

            _columnsPopup = rootGo;
        }

        private void BuildColumnsPopupHeader(Transform parent)
        {
            var hdrGo = EditorUIHelpers.CreateUI("Header", parent);
            hdrGo.AddComponent<LayoutElement>().preferredHeight = COLPOP_HEADER_H;
            var hdrImg = hdrGo.AddComponent<Image>();
            hdrImg.color = TileEditorTheme.HeaderBg;

            var hlg = hdrGo.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 4, 0, 0);
            hlg.childForceExpandHeight = true;
            hlg.childControlHeight     = true;
            hlg.childControlWidth      = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var titleGo = EditorUIHelpers.CreateUI("Title", hdrGo.transform);
            titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var titleTmp           = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text          = "COLUMNS";
            titleTmp.fontSize      = 12f;
            titleTmp.fontStyle     = FontStyles.Bold;
            titleTmp.alignment     = TextAlignmentOptions.MidlineLeft;
            titleTmp.color         = TileEditorTheme.HeaderTitle;
            titleTmp.characterSpacing = 1.5f;

            var closeBtn = UIButton.Make(hdrGo.transform, "X",
                CloseColumnsConfigPopup, height: COLPOP_HEADER_H, fontSize: 12f);
            var closeLE = closeBtn.GetComponent<LayoutElement>();
            closeLE.preferredWidth = 32f;
            closeLE.flexibleWidth  = 0f;
        }

        private void BuildColumnsPopupRows(Transform parent)
        {
            var (scroll, content) = UIFactory.MakeScrollView(parent, "RowsScroll");
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            var scrollLE = scroll.gameObject.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1f;

            // Replace VLG that MakeScrollView creates with one tuned for tight rows.
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.spacing                = 1f;
                vlg.padding                = new RectOffset(6, 6, 4, 4);
                vlg.childForceExpandWidth  = true;
                vlg.childForceExpandHeight = false;
                vlg.childControlWidth      = true;
                vlg.childControlHeight     = true;
            }

            var cols = ItemTableColumns.All;
            _columnTogglesByIndex = new Toggle[cols.Count];

            ItemColumnCategory? prevCategory = null;
            for (int i = 0; i < cols.Count; i++)
            {
                var col = cols[i];
                if (prevCategory == null || col.Category != prevCategory.Value)
                {
                    BuildColumnsPopupSectionLabel(content, col.Category);
                    prevCategory = col.Category;
                }
                _columnTogglesByIndex[i] = BuildColumnsPopupRow(content, col);
            }
        }

        private static void BuildColumnsPopupSectionLabel(Transform parent, ItemColumnCategory cat)
        {
            var go = EditorUIHelpers.CreateUI("Sect_" + cat, parent);
            go.AddComponent<LayoutElement>().preferredHeight = COLPOP_SECT_H;
            var tmp        = go.AddComponent<TextMeshProUGUI>();
            tmp.text       = cat.ToString().ToUpperInvariant();
            tmp.fontSize   = 9f;
            tmp.fontStyle  = FontStyles.Bold | FontStyles.UpperCase;
            tmp.alignment  = TextAlignmentOptions.MidlineLeft;
            tmp.color      = ItemTableColumns.CategoryColor(cat);
            tmp.characterSpacing = 1.5f;
            tmp.margin     = new Vector4(2f, 0f, 0f, 0f);
        }

        private Toggle BuildColumnsPopupRow(Transform parent, ItemTableColumn col)
        {
            var rowGo = EditorUIHelpers.CreateUI("Row_" + col.Header, parent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = COLPOP_ROW_H;
            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.padding                = new RectOffset(2, 2, 0, 0);
            hlg.childForceExpandHeight = true;
            hlg.childControlHeight     = true;
            hlg.childControlWidth      = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            // Toggle (square checkbox)
            var togGo = EditorUIHelpers.CreateUI("Toggle", rowGo.transform);
            var togLE = togGo.AddComponent<LayoutElement>();
            togLE.preferredWidth  = 18f;
            togLE.flexibleWidth   = 0f;
            var togBg     = togGo.AddComponent<Image>();
            togBg.color   = UITheme.BG_SURFACE;
            var toggle    = togGo.AddComponent<Toggle>();
            toggle.targetGraphic = togBg;

            var checkGo = EditorUIHelpers.CreateUI("Check", togGo.transform);
            UIFactory.StretchFill(checkGo);
            var checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.offsetMin = new Vector2(2f, 2f);
            checkRt.offsetMax = new Vector2(-2f, -2f);
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color   = UITheme.ACCENT;
            toggle.graphic   = checkImg;

            toggle.isOn = !_hiddenColumns.Contains(col.Header);
            toggle.onValueChanged.AddListener(on => OnColumnVisibilityToggled(col, on));

            // Category stripe (3 px coloured rect)
            var stripeGo = EditorUIHelpers.CreateUI("Stripe", rowGo.transform);
            var stripeLE = stripeGo.AddComponent<LayoutElement>();
            stripeLE.preferredWidth = COLPOP_STRIPE_W;
            stripeLE.flexibleWidth  = 0f;
            stripeGo.AddComponent<Image>().color = ItemTableColumns.CategoryColor(col.Category);

            // Header label
            var labelGo = EditorUIHelpers.CreateUI("Label", rowGo.transform);
            labelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var labelTmp        = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text       = col.Header;
            labelTmp.fontSize   = 11f;
            labelTmp.alignment  = TextAlignmentOptions.MidlineLeft;
            labelTmp.color      = UITheme.TEXT_PRIMARY;
            labelTmp.enableWordWrapping = false;
            labelTmp.overflowMode       = TextOverflowModes.Ellipsis;

            return toggle;
        }

        private void BuildColumnsPopupFooter(Transform parent)
        {
            var ftrGo = EditorUIHelpers.CreateUI("Footer", parent);
            ftrGo.AddComponent<LayoutElement>().preferredHeight = COLPOP_FOOTER_H;
            ftrGo.AddComponent<Image>().color = TileEditorTheme.HeaderBg;

            var hlg = ftrGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.padding                = new RectOffset(8, 8, 4, 4);
            hlg.childForceExpandHeight = true;
            hlg.childControlHeight     = true;
            hlg.childControlWidth      = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            // Live counter — updated by RefreshColumnsConfigState.
            var counterGo = EditorUIHelpers.CreateUI("Counter", ftrGo.transform);
            counterGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var counterTmp        = counterGo.AddComponent<TextMeshProUGUI>();
            counterTmp.fontSize   = 10f;
            counterTmp.alignment  = TextAlignmentOptions.MidlineLeft;
            counterTmp.color      = UITheme.TEXT_SECONDARY;
            _columnsCountLabel    = counterTmp;

            UIButton.Make(ftrGo.transform, "All",
                () => SetAllColumnsVisible(true), height: 28f, fontSize: 10f)
                .GetComponent<LayoutElement>().preferredWidth = 50f;
            UIButton.Make(ftrGo.transform, "None",
                () => SetAllColumnsVisible(false), height: 28f, fontSize: 10f)
                .GetComponent<LayoutElement>().preferredWidth = 60f;
            UIButton.Make(ftrGo.transform, "Reset",
                ResetColumnsToDefaults, height: 28f, fontSize: 10f)
                .GetComponent<LayoutElement>().preferredWidth = 60f;
        }

        // ── Popup state helpers ───────────────────────────────────────────────

        private void RefreshColumnsConfigState()
        {
            if (_columnTogglesByIndex == null) return;
            var cols = ItemTableColumns.All;
            for (int i = 0; i < cols.Count && i < _columnTogglesByIndex.Length; i++)
            {
                var t = _columnTogglesByIndex[i];
                if (t == null) continue;
                t.SetIsOnWithoutNotify(!_hiddenColumns.Contains(cols[i].Header));
            }
            UpdateColumnsCountLabel();
            RefreshColumnsCountLabel();
        }

        private void UpdateColumnsCountLabel()
        {
            if (_columnsCountLabel == null) return;
            int total   = ItemTableColumns.All.Count;
            int visible = total - _hiddenColumns.Count;
            _columnsCountLabel.text = $"{visible}/{total} visible";
        }

        private void OnColumnVisibilityToggled(ItemTableColumn col, bool visible)
        {
            if (col == null) return;
            if (visible) _hiddenColumns.Remove(col.Header);
            else         _hiddenColumns.Add(col.Header);

            SaveColumnPrefs();
            UpdateColumnsCountLabel();
            RefreshColumnsCountLabel();

            // Rebuild header + rows with the new visibility set.
            BuildTableHeader();
            RefreshTable();
        }

        private void SetAllColumnsVisible(bool visible)
        {
            _hiddenColumns.Clear();
            if (!visible)
            {
                foreach (var c in ItemTableColumns.All) _hiddenColumns.Add(c.Header);
            }
            SaveColumnPrefs();
            BuildTableHeader();
            RefreshTable();
            RefreshColumnsConfigState();
        }

        /// <summary>
        /// Reset to the canonical defaults: every column visible. Same effect
        /// as "All" today; kept as a separate verb so we can introduce
        /// preset bundles (e.g. "Combat columns only") without rewiring.
        /// </summary>
        private void ResetColumnsToDefaults() => SetAllColumnsVisible(true);
    }
}
