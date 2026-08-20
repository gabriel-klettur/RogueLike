using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Particles Editor — Table view (second tab of the Presets panel).
    ///
    /// Architecture mirrors SpellsRuntimeEditor.Table.cs exactly:
    ///   - _presetsHeaderScroll  — horizontal-only, holds the sticky header strip.
    ///   - _presetsTableScroll   — horizontal + vertical, holds all data rows.
    ///   Both share horizontal scroll position via <see cref="OnPresetsTableScrolled"/>.
    ///
    /// Columns are driven by <see cref="ParticleTableColumns.All"/>. Adding a field
    /// = adding one entry in the registry (row builder is generic).
    ///
    /// Inline edits mutate the ParticlePresetDefinition ScriptableObject directly.
    /// NOTE: There is currently no undo/redo path for table edits to preset fields —
    /// this matches the existing OnLoopsToggled pattern (sets dirty, refreshes, no undo).
    /// A future improvement can route through ExecutePersistedEdit if needed.
    /// </summary>
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Table layout constants ────────────────────────────────────────────

        private const float PTBL_ROW_H           = 26f;
        private const float PTBL_HEADER_H        = 24f;
        private const float PTBL_CELL_PAD_H      =  4f;
        private const float PTBL_SB_W            = 12f;
        private const float PTBL_CATEGORY_BAND_H =  3f;

        // Hidden columns — persisted in PlayerPrefs.
        // Prefer string key constant so PlayerPrefs entry is stable across renames.
        private const string PARTICLE_HIDDEN_COLUMNS_PREFS_KEY = "ParticlesEditor.HiddenColumns";

        private readonly HashSet<string> _hiddenParticleColumns
            = new HashSet<string>(System.StringComparer.Ordinal);

        private bool IsParticleColumnVisible(ParticleTableColumn col)
            => col != null && !_hiddenParticleColumns.Contains(col.Header);

        // ScrollRect references set by SetTableScrollRects (called from BuildUI).
        private ScrollRect     _presetsHeaderScroll;
        private ScrollRect     _presetsTableScroll;
        private RectTransform  _presetsTableBodyContent;
        private RectTransform  _presetsTableHeaderContent;

        private readonly List<GameObject> _particleTableRows = new List<GameObject>();

        // Hover state for header tooltip restore.
        private string _particleStatusBeforeHeaderHover;
        private bool   _particleHoveringHeader;

        // ── Wiring — called from BuildUI ─────────────────────────────────────

        internal void SetPresetsTableScrollRects(ScrollRect headerScroll, ScrollRect bodyScroll,
            RectTransform headerContent, RectTransform bodyContent)
        {
            _presetsHeaderScroll       = headerScroll;
            _presetsTableScroll        = bodyScroll;
            _presetsTableHeaderContent = headerContent;
            _presetsTableBodyContent   = bodyContent;

            if (_presetsTableScroll != null)
                _presetsTableScroll.onValueChanged.AddListener(OnPresetsTableScrolled);

            LoadParticleColumnPrefs();
            BuildPresetsTableHeader();
        }

        // ── Column prefs ──────────────────────────────────────────────────────

        internal void LoadParticleColumnPrefs()
        {
            _hiddenParticleColumns.Clear();
            string blob = PlayerPrefs.GetString(PARTICLE_HIDDEN_COLUMNS_PREFS_KEY, null);
            if (blob == null)
            {
                foreach (var h in ParticleTableColumns.DefaultHidden)
                    _hiddenParticleColumns.Add(h);
                return;
            }
            if (string.IsNullOrEmpty(blob)) return;
            foreach (var h in blob.Split(','))
                if (!string.IsNullOrWhiteSpace(h)) _hiddenParticleColumns.Add(h.Trim());
        }

        internal void SaveParticleColumnPrefs()
        {
            PlayerPrefs.SetString(PARTICLE_HIDDEN_COLUMNS_PREFS_KEY,
                string.Join(",", _hiddenParticleColumns));
            PlayerPrefs.Save();
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        private void RefreshTable()
        {
            if (_presetsTableBodyContent == null) return;

            for (int i = _presetsTableBodyContent.childCount - 1; i >= 0; i--)
            {
                var child = _presetsTableBodyContent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            _particleTableRows.Clear();

            if (_catalog == null) return;

            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            var visible = new List<ParticlePresetDefinition>();
            foreach (var preset in _catalog.Presets)
            {
                if (preset == null) continue;
                // Same gate as the Grid — a tab that hid a preset in one view and showed it
                // in the other would make the tabs untrustworthy.
                if (!MatchesCategoryFilter(preset)) continue;
                if (filter.Length > 0)
                {
                    string pid = (preset.id ?? "").ToLowerInvariant();
                    string nm  = (preset.displayName ?? "").ToLowerInvariant();
                    if (!pid.Contains(filter) && !nm.Contains(filter)) continue;
                }
                visible.Add(preset);
            }

            for (int i = 0; i < visible.Count; i++)
            {
                var row = BuildParticleTableRow(visible[i], i);
                _particleTableRows.Add(row);
            }

            float totalW = ComputeParticleTableTotalWidth();
            var bodySize = _presetsTableBodyContent.sizeDelta;
            _presetsTableBodyContent.sizeDelta = new Vector2(totalW, bodySize.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_presetsTableBodyContent);
        }

        // ── Header ────────────────────────────────────────────────────────────

        private void BuildPresetsTableHeader()
        {
            if (_presetsTableHeaderContent == null) return;

            for (int i = _presetsTableHeaderContent.childCount - 1; i >= 0; i--)
            {
                var ch = _presetsTableHeaderContent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(ch);
                else DestroyImmediate(ch);
            }

            float totalW = ComputeParticleTableTotalWidth();
            _presetsTableHeaderContent.sizeDelta = new Vector2(totalW, PTBL_HEADER_H);

            var cols = ParticleTableColumns.All;
            float xCursor = 0f;
            int placed = 0;
            for (int c = 0; c < cols.Count; c++)
            {
                var col = cols[c];
                if (!IsParticleColumnVisible(col)) continue;

                var cellGo = UIFactory.CreateUI("Hdr_" + col.Header, _presetsTableHeaderContent);
                var cellRt = cellGo.GetComponent<RectTransform>();
                cellRt.anchorMin        = new Vector2(0f, 0f);
                cellRt.anchorMax        = new Vector2(0f, 1f);
                cellRt.pivot            = new Vector2(0f, 0.5f);
                cellRt.anchoredPosition = new Vector2(xCursor, 0f);
                cellRt.sizeDelta        = new Vector2(col.Width, 0f);

                cellGo.AddComponent<Image>().color = TileEditorTheme.HeaderBg;

                if (placed > 0)
                {
                    var div   = UIFactory.CreateUI("Div", cellGo.transform);
                    var divRt = div.GetComponent<RectTransform>();
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
                bandRt.sizeDelta        = new Vector2(0f, PTBL_CATEGORY_BAND_H);
                bandGo.AddComponent<Image>().color = ParticleTableColumns.CategoryColor(col.Category);

                var tmp = UILabel.AddCenteredText(cellGo.transform,
                    col.Header, 9f, FontStyles.Bold, TileEditorTheme.HeaderTitle);
                tmp.enableWordWrapping = false;
                tmp.overflowMode       = TextOverflowModes.Truncate;
                tmp.margin             = new Vector4(PTBL_CELL_PAD_H, PTBL_CATEGORY_BAND_H,
                                                     PTBL_CELL_PAD_H, 0f);

                AttachParticleHeaderHover(cellGo, col);

                xCursor += col.Width;
                placed++;
            }
        }

        // ── Header hover tooltip ──────────────────────────────────────────────

        private void AttachParticleHeaderHover(GameObject cellGo, ParticleTableColumn col)
        {
            if (cellGo == null) return;
            var tip = !string.IsNullOrEmpty(col.Tooltip)
                ? $"<b>{col.Header}</b> ({col.Category}) — {col.Tooltip}"
                : $"<b>{col.Header}</b> ({col.Category})";

            var trigger = cellGo.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (!_particleHoveringHeader && _ui.StatusText != null)
                    _particleStatusBeforeHeaderHover = _ui.StatusText.text;
                _particleHoveringHeader = true;
                SetStatus(tip);
            });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                _particleHoveringHeader = false;
                if (_particleStatusBeforeHeaderHover != null)
                {
                    SetStatus(_particleStatusBeforeHeaderHover);
                    _particleStatusBeforeHeaderHover = null;
                }
            });
            trigger.triggers.Add(exit);
        }

        // ── Row builder ───────────────────────────────────────────────────────

        private static readonly Color PTBL_ZEBRA_A  = new Color(0.10f, 0.11f, 0.14f, 0.90f);
        private static readonly Color PTBL_ZEBRA_B  = new Color(0.12f, 0.13f, 0.17f, 0.90f);
        private static readonly Color PTBL_SELECTED = new Color(0.22f, 0.35f, 0.55f, 0.95f);

        private GameObject BuildParticleTableRow(ParticlePresetDefinition def, int rowIndex)
        {
            float totalW = ComputeParticleTableTotalWidth();
            string pid   = def.id ?? "";

            var rowGo = UIFactory.CreateUI("Row_" + pid, _presetsTableBodyContent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = PTBL_ROW_H;

            var rowBg  = rowGo.AddComponent<Image>();
            rowBg.color = (pid == _selectedPresetId)
                ? PTBL_SELECTED
                : rowIndex % 2 == 0 ? PTBL_ZEBRA_A : PTBL_ZEBRA_B;

            var capturedPid = pid;
            var btn = rowGo.AddComponent<Button>();
            btn.targetGraphic = rowBg;
            var bc = btn.colors;
            bc.normalColor      = rowBg.color;
            bc.highlightedColor = rowBg.color + new Color(0.05f, 0.05f, 0.08f, 0f);
            bc.pressedColor     = PTBL_SELECTED;
            bc.selectedColor    = PTBL_SELECTED;
            bc.fadeDuration     = 0.08f;
            btn.colors          = bc;
            btn.onClick.AddListener(() => SelectPreset(capturedPid));

            var cols = ParticleTableColumns.All;
            float xCursor = 0f;
            for (int c = 0; c < cols.Count; c++)
            {
                var col = cols[c];
                if (!IsParticleColumnVisible(col)) continue;

                var cellGo = UIFactory.CreateUI("Cell_" + col.Header, rowGo.transform);
                var cellRt = cellGo.GetComponent<RectTransform>();
                cellRt.anchorMin        = new Vector2(0f, 0f);
                cellRt.anchorMax        = new Vector2(0f, 1f);
                cellRt.pivot            = new Vector2(0f, 0.5f);
                cellRt.anchoredPosition = new Vector2(xCursor, 0f);
                cellRt.sizeDelta        = new Vector2(col.Width, 0f);

                BuildParticleCell(cellGo.transform, col, def);
                xCursor += col.Width;
            }

            rowGo.GetComponent<RectTransform>().sizeDelta = new Vector2(totalW, PTBL_ROW_H);
            return rowGo;
        }

        // ── Cell builder ──────────────────────────────────────────────────────

        private void BuildParticleCell(Transform cellT, ParticleTableColumn col,
            ParticlePresetDefinition def)
        {
            switch (col.EditorKind)
            {
                case ParticleTableEditorKind.Text:
                case ParticleTableEditorKind.Int:
                case ParticleTableEditorKind.Float:
                    BuildParticleTextCell(cellT, col, def);
                    break;
                case ParticleTableEditorKind.Toggle:
                    BuildParticleToggleCell(cellT, col, def);
                    break;
            }
        }

        private void BuildParticleTextCell(Transform cellT, ParticleTableColumn col,
            ParticlePresetDefinition def)
        {
            if (col.SetString == null)
            {
                var tmp = UILabel.AddCenteredText(cellT,
                    col.GetString(def), 10f, FontStyles.Normal, UITheme.TEXT_PRIMARY);
                tmp.enableWordWrapping = false;
                tmp.overflowMode       = TextOverflowModes.Truncate;
                tmp.margin             = new Vector4(PTBL_CELL_PAD_H, 0f, PTBL_CELL_PAD_H, 0f);
                return;
            }

            var contentType = col.EditorKind == ParticleTableEditorKind.Int
                ? TMP_InputField.ContentType.IntegerNumber
                : col.EditorKind == ParticleTableEditorKind.Float
                    ? TMP_InputField.ContentType.DecimalNumber
                    : TMP_InputField.ContentType.Standard;

            var input = UIInputField.AddCommit(cellT,
                col.GetString(def),
                v => OnParticleCellEndEdit(col, def, v),
                PTBL_ROW_H, 10f);
            input.contentType = contentType;
            var inputRt = input.GetComponent<RectTransform>();
            inputRt.anchorMin = Vector2.zero;
            inputRt.anchorMax = Vector2.one;
            inputRt.sizeDelta = Vector2.zero;
        }

        private void BuildParticleToggleCell(Transform cellT, ParticleTableColumn col,
            ParticlePresetDefinition def)
        {
            var holderGo = UIFactory.CreateUI("ToggleHolder", cellT);
            var holderRt = holderGo.GetComponent<RectTransform>();
            holderRt.anchorMin = Vector2.zero;
            holderRt.anchorMax = Vector2.one;
            holderRt.sizeDelta = Vector2.zero;

            var tGo  = UIFactory.CreateUI("Toggle", holderGo.transform);
            var tRt  = tGo.GetComponent<RectTransform>();
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

            var checkGo  = UIFactory.CreateUI("Check", tGo.transform);
            UIFactory.StretchFill(checkGo);
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color   = UITheme.ACCENT;
            toggle.graphic   = checkImg;

            bool.TryParse(col.GetString(def), out bool current);
            toggle.isOn = current;

            if (col.SetString != null)
                toggle.onValueChanged.AddListener(v => OnParticleCellEndEdit(col, def, v.ToString()));
        }

        // ── Commit (with undo/redo) ───────────────────────────────────────────

        /// <summary>
        /// Commits a cell edit to the preset ScriptableObject and pushes an undo
        /// entry so the user can Ctrl-Z a fat-fingered value.
        ///
        /// For text/float/int fields this is called by the TMP_InputField onEndEdit
        /// callback — once per commit, not on every keystroke.
        /// For toggle fields it is called by onValueChanged — one entry per flip.
        ///
        /// The undo lambda calls RefreshTable() to rebuild widgets from the restored
        /// ScriptableObject value, avoiding stale widget state without causing double
        /// entries.
        /// </summary>
        private void OnParticleCellEndEdit(ParticleTableColumn col,
            ParticlePresetDefinition def, string newValue)
        {
            if (col.SetString == null || def == null) return;
            string oldValue = col.GetString(def);

            // Skip no-op commits (same value).
            if (string.Equals(oldValue, newValue, System.StringComparison.Ordinal)) return;

            string label = $"Edit {def.id ?? "?"}.{col.Header}";
            ExecutePresetEdit(label,
                () =>
                {
                    col.SetString(def, newValue);
                    MarkParticlePresetDirty(def);
                    RefreshPicker();
                    RefreshTable();
                },
                () =>
                {
                    col.SetString(def, oldValue);
                    MarkParticlePresetDirty(def);
                    RefreshPicker();
                    RefreshTable();
                });
        }

        /// <summary>
        /// Pushes a preset-asset edit onto the UndoStack.
        /// Unlike <see cref="ExecutePersistedEdit"/>, this does NOT touch the
        /// instances JSON — preset ScriptableObjects are saved by Unity's AssetDatabase,
        /// not by the particles_instances.json persistence layer.
        /// </summary>
        private void ExecutePresetEdit(string label, System.Action doAction, System.Action undoAction)
        {
            _undo.Do(label,
                () => doAction?.Invoke(),
                () => undoAction?.Invoke());
            RefreshUndoRedoLabels();
        }

        private static void MarkParticlePresetDirty(ParticlePresetDefinition def)
        {
            if (def == null) return;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(def);
#endif
        }

        // ── Scroll sync ───────────────────────────────────────────────────────

        /// <summary>
        /// Mirror the body content horizontal position onto the header content
        /// using absolute pixel offset (pixel-mirror is exact; normalized drifts by
        /// the scrollbar gutter width).
        /// </summary>
        private void OnPresetsTableScrolled(Vector2 _)
        {
            if (_presetsTableHeaderContent == null || _presetsTableBodyContent == null) return;
            var hdr = _presetsTableHeaderContent.anchoredPosition;
            hdr.x = _presetsTableBodyContent.anchoredPosition.x;
            _presetsTableHeaderContent.anchoredPosition = hdr;
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private float ComputeParticleTableTotalWidth()
        {
            float w = 0f;
            var cols = ParticleTableColumns.All;
            for (int i = 0; i < cols.Count; i++)
            {
                if (IsParticleColumnVisible(cols[i])) w += cols[i].Width;
            }
            return w;
        }
    }
}
