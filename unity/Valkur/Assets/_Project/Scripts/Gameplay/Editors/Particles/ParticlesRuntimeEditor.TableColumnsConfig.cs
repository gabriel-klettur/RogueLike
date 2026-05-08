using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Particles Editor — column visibility popup for the Table tab.
    ///
    /// Lets the user pick which ParticlePresetDefinition columns are rendered in the table.
    /// Mirrors <c>SpellsRuntimeEditor.TableColumnsConfig.cs</c> exactly, adapted for
    /// <see cref="ParticleTableColumn"/> / <see cref="ParticleColumnCategory"/>.
    ///
    /// Persistence
    /// ───────────
    /// Hidden column headers are stored in PlayerPrefs as CSV under
    /// <see cref="PARTICLE_HIDDEN_COLUMNS_PREFS_KEY"/> (shared with Table.cs).
    ///
    /// UX
    /// ──
    ///  • "Columns ▾" toolbar button (when Table tab is active) opens/closes the popup.
    ///  • Modal popup: translucent scrim + click-on-scrim closes it.
    ///  • Checkbox rows for every column, grouped by category.
    ///  • "All" / "None" / "Reset" buttons in the footer.
    ///  • Button label shows "Columns (N hidden)" when columns are hidden.
    /// </summary>
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private const float PCOLPOP_WIDTH    = 300f;
        private const float PCOLPOP_HEIGHT   = 440f;
        private const float PCOLPOP_HEADER_H =  28f;
        private const float PCOLPOP_FOOTER_H =  36f;
        private const float PCOLPOP_ROW_H    =  22f;
        private const float PCOLPOP_SECT_H   =  18f;
        private const float PCOLPOP_STRIPE_W =   3f;

        private GameObject   _particleColumnsPopup;
        private Toggle[]     _particleColumnTogglesByIndex;
        private TextMeshProUGUI _particleColumnsCountLabel;

        // ── Open / Close ──────────────────────────────────────────────────────

        internal void OpenParticleColumnsConfigPopup()
        {
            if (_particleColumnsPopup == null) BuildParticleColumnsConfigPopup();
            if (_particleColumnsPopup == null) return;
            _particleColumnsPopup.SetActive(true);
            _particleColumnsPopup.transform.SetAsLastSibling();
            RefreshParticleColumnsConfigState();
            UpdateParticleColumnsBtnLabel();
        }

        internal void CloseParticleColumnsConfigPopup()
        {
            if (_particleColumnsPopup != null) _particleColumnsPopup.SetActive(false);
        }

        // ── Button label helper ───────────────────────────────────────────────

        /// <summary>
        /// Refresh the "Columns" / "Columns (N hidden)" label on the toolbar button.
        /// Called after every toggle so the button reflects state in real time.
        /// </summary>
        internal void UpdateParticleColumnsBtnLabel()
        {
            if (_ui.PresetsColumnsCfgLabel == null) return;
            int hidden = _hiddenParticleColumns.Count;
            _ui.PresetsColumnsCfgLabel.text = hidden > 0
                ? $"Columns ({hidden} hidden)"
                : "Columns";
        }

        // ── Popup builder ─────────────────────────────────────────────────────

        private void BuildParticleColumnsConfigPopup()
        {
            if (_canvas == null) return;

            // Full-screen scrim that closes on click-outside.
            var rootGo = EditorUIHelpers.CreateUI("ParticleColumnsConfigPopup", _canvas.transform);
            EditorUIHelpers.StretchFill(rootGo);
            var scrim = rootGo.AddComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, 0.55f);
            scrim.raycastTarget = true;

            var scrimTrigger = rootGo.AddComponent<EventTrigger>();
            var scrimClick   = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            scrimClick.callback.AddListener(ev =>
            {
                if (ev is PointerEventData ped && ped.pointerCurrentRaycast.gameObject == rootGo)
                    CloseParticleColumnsConfigPopup();
            });
            scrimTrigger.triggers.Add(scrimClick);

            // Centred body panel.
            var bodyGo = EditorUIHelpers.CreateUI("PopupBody", rootGo.transform);
            var bodyRt = bodyGo.GetComponent<RectTransform>();
            bodyRt.anchorMin        = new Vector2(0.5f, 0.5f);
            bodyRt.anchorMax        = new Vector2(0.5f, 0.5f);
            bodyRt.pivot            = new Vector2(0.5f, 0.5f);
            bodyRt.anchoredPosition = Vector2.zero;
            bodyRt.sizeDelta        = new Vector2(PCOLPOP_WIDTH, PCOLPOP_HEIGHT);
            bodyGo.AddComponent<Image>().color = TileEditorTheme.PanelBg;
            var ol            = bodyGo.AddComponent<Outline>();
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(1f, 1f);

            var bodyVlg = bodyGo.AddComponent<VerticalLayoutGroup>();
            bodyVlg.spacing                = 0f;
            bodyVlg.padding                = new RectOffset(0, 0, 0, 0);
            bodyVlg.childForceExpandWidth  = true;
            bodyVlg.childForceExpandHeight = false;
            bodyVlg.childControlWidth      = true;
            bodyVlg.childControlHeight     = true;

            BuildParticleColumnsPopupHeader(bodyGo.transform);
            BuildParticleColumnsPopupRows(bodyGo.transform);
            BuildParticleColumnsPopupFooter(bodyGo.transform);

            _particleColumnsPopup = rootGo;
        }

        private void BuildParticleColumnsPopupHeader(Transform parent)
        {
            var hdrGo = EditorUIHelpers.CreateUI("Header", parent);
            hdrGo.AddComponent<LayoutElement>().preferredHeight = PCOLPOP_HEADER_H;
            hdrGo.AddComponent<Image>().color = TileEditorTheme.HeaderBg;

            var hlg = hdrGo.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 4, 0, 0);
            hlg.childForceExpandHeight = true;
            hlg.childControlHeight     = true;
            hlg.childControlWidth      = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var titleGo = EditorUIHelpers.CreateUI("Title", hdrGo.transform);
            titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var titleTmp        = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text       = "COLUMNS";
            titleTmp.fontSize   = 12f;
            titleTmp.fontStyle  = FontStyles.Bold;
            titleTmp.alignment  = TextAlignmentOptions.MidlineLeft;
            titleTmp.color      = TileEditorTheme.HeaderTitle;
            titleTmp.characterSpacing = 1.5f;

            var closeBtn = UIButton.Make(hdrGo.transform, "X",
                CloseParticleColumnsConfigPopup, height: PCOLPOP_HEADER_H, fontSize: 12f);
            closeBtn.GetComponent<LayoutElement>().preferredWidth = 32f;
        }

        private void BuildParticleColumnsPopupRows(Transform parent)
        {
            var (scroll, content) = UIFactory.MakeScrollView(parent, "RowsScroll");
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            scroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

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

            var cols = ParticleTableColumns.All;
            _particleColumnTogglesByIndex = new Toggle[cols.Count];

            ParticleColumnCategory? prevCategory = null;
            for (int i = 0; i < cols.Count; i++)
            {
                var col = cols[i];
                if (prevCategory == null || col.Category != prevCategory.Value)
                {
                    BuildParticleColumnsPopupSectionLabel(content, col.Category);
                    prevCategory = col.Category;
                }
                _particleColumnTogglesByIndex[i] = BuildParticleColumnsPopupRow(content, col);
            }
        }

        private static void BuildParticleColumnsPopupSectionLabel(Transform parent,
            ParticleColumnCategory cat)
        {
            var go = EditorUIHelpers.CreateUI("Sect_" + cat, parent);
            go.AddComponent<LayoutElement>().preferredHeight = PCOLPOP_SECT_H;
            var tmp        = go.AddComponent<TextMeshProUGUI>();
            tmp.text       = cat.ToString().ToUpperInvariant();
            tmp.fontSize   = 9f;
            tmp.fontStyle  = FontStyles.Bold | FontStyles.UpperCase;
            tmp.alignment  = TextAlignmentOptions.MidlineLeft;
            tmp.color      = ParticleTableColumns.CategoryColor(cat);
            tmp.characterSpacing = 1.5f;
            tmp.margin     = new Vector4(2f, 0f, 0f, 0f);
        }

        private Toggle BuildParticleColumnsPopupRow(Transform parent, ParticleTableColumn col)
        {
            var rowGo = EditorUIHelpers.CreateUI("Row_" + col.Header, parent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = PCOLPOP_ROW_H;
            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.padding                = new RectOffset(2, 2, 0, 0);
            hlg.childForceExpandHeight = true;
            hlg.childControlHeight     = true;
            hlg.childControlWidth      = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            // Toggle checkbox
            var togGo = EditorUIHelpers.CreateUI("Toggle", rowGo.transform);
            var togLE = togGo.AddComponent<LayoutElement>();
            togLE.preferredWidth = 18f;
            togLE.flexibleWidth  = 0f;
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

            toggle.isOn = !_hiddenParticleColumns.Contains(col.Header);
            toggle.onValueChanged.AddListener(on => OnParticleColumnVisibilityToggled(col, on));

            // Category stripe (3 px coloured rect)
            var stripeGo = EditorUIHelpers.CreateUI("Stripe", rowGo.transform);
            var stripeLE = stripeGo.AddComponent<LayoutElement>();
            stripeLE.preferredWidth = PCOLPOP_STRIPE_W;
            stripeLE.flexibleWidth  = 0f;
            stripeGo.AddComponent<Image>().color = ParticleTableColumns.CategoryColor(col.Category);

            // Label
            var labelGo = EditorUIHelpers.CreateUI("Label", rowGo.transform);
            labelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var labelTmp             = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text            = col.Header;
            labelTmp.fontSize        = 11f;
            labelTmp.alignment       = TextAlignmentOptions.MidlineLeft;
            labelTmp.color           = UITheme.TEXT_PRIMARY;
            labelTmp.enableWordWrapping = false;
            labelTmp.overflowMode    = TextOverflowModes.Ellipsis;

            return toggle;
        }

        private void BuildParticleColumnsPopupFooter(Transform parent)
        {
            var ftrGo = EditorUIHelpers.CreateUI("Footer", parent);
            ftrGo.AddComponent<LayoutElement>().preferredHeight = PCOLPOP_FOOTER_H;
            ftrGo.AddComponent<Image>().color = TileEditorTheme.HeaderBg;

            var hlg = ftrGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.padding                = new RectOffset(8, 8, 4, 4);
            hlg.childForceExpandHeight = true;
            hlg.childControlHeight     = true;
            hlg.childControlWidth      = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var counterGo = EditorUIHelpers.CreateUI("Counter", ftrGo.transform);
            counterGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var counterTmp        = counterGo.AddComponent<TextMeshProUGUI>();
            counterTmp.fontSize   = 10f;
            counterTmp.alignment  = TextAlignmentOptions.MidlineLeft;
            counterTmp.color      = UITheme.TEXT_SECONDARY;
            _particleColumnsCountLabel = counterTmp;

            UIButton.Make(ftrGo.transform, "All",
                () => SetAllParticleColumnsVisible(true), height: 28f, fontSize: 10f)
                .GetComponent<LayoutElement>().preferredWidth = 50f;
            UIButton.Make(ftrGo.transform, "None",
                () => SetAllParticleColumnsVisible(false), height: 28f, fontSize: 10f)
                .GetComponent<LayoutElement>().preferredWidth = 60f;
            UIButton.Make(ftrGo.transform, "Reset",
                ResetParticleColumnsToDefaults, height: 28f, fontSize: 10f)
                .GetComponent<LayoutElement>().preferredWidth = 60f;
        }

        // ── Popup state helpers ───────────────────────────────────────────────

        private void RefreshParticleColumnsConfigState()
        {
            if (_particleColumnTogglesByIndex == null) return;
            var cols = ParticleTableColumns.All;
            for (int i = 0; i < cols.Count && i < _particleColumnTogglesByIndex.Length; i++)
            {
                var t = _particleColumnTogglesByIndex[i];
                if (t == null) continue;
                t.SetIsOnWithoutNotify(!_hiddenParticleColumns.Contains(cols[i].Header));
            }
            UpdateParticleColumnsCountLabelPopup();
            UpdateParticleColumnsBtnLabel();
        }

        private void UpdateParticleColumnsCountLabelPopup()
        {
            if (_particleColumnsCountLabel == null) return;
            int total   = ParticleTableColumns.All.Count;
            int visible = total - _hiddenParticleColumns.Count;
            _particleColumnsCountLabel.text = $"{visible}/{total} visible";
        }

        private void OnParticleColumnVisibilityToggled(ParticleTableColumn col, bool visible)
        {
            if (col == null) return;
            if (visible) _hiddenParticleColumns.Remove(col.Header);
            else         _hiddenParticleColumns.Add(col.Header);

            SaveParticleColumnPrefs();
            UpdateParticleColumnsCountLabelPopup();
            UpdateParticleColumnsBtnLabel();

            BuildPresetsTableHeader();
            RefreshTable();
        }

        private void SetAllParticleColumnsVisible(bool visible)
        {
            _hiddenParticleColumns.Clear();
            if (!visible)
            {
                foreach (var c in ParticleTableColumns.All)
                    _hiddenParticleColumns.Add(c.Header);
            }
            SaveParticleColumnPrefs();
            BuildPresetsTableHeader();
            RefreshTable();
            RefreshParticleColumnsConfigState();
        }

        private void ResetParticleColumnsToDefaults()
        {
            _hiddenParticleColumns.Clear();
            foreach (var h in ParticleTableColumns.DefaultHidden)
                _hiddenParticleColumns.Add(h);
            SaveParticleColumnPrefs();
            BuildPresetsTableHeader();
            RefreshTable();
            RefreshParticleColumnsConfigState();
        }
    }
}
