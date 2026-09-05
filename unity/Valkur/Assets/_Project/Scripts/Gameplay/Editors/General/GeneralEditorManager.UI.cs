using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Core.Editors;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.General
{
    public partial class GeneralEditorManager
    {
        // ── Layout constants (kept here so UI is self-describing) ──────────────
        private const float PANEL_WIDTH       = 280f;
        private const float PANEL_X_OFFSET    = 8f;
        private const float PANEL_Y_OFFSET    = TileEditorUIHelpers.PANEL_TOP_OFFSET;
        internal const float SECTION_HDR_H    = 18f;
        internal const float SECTION_SPACING  = 6f;
        internal const float BUTTON_HEIGHT    = 26f;
        internal const float GRID_SPACING     = 4f;
        internal const int   GRID_COLUMNS     = 3;
        private const float CLOSE_BTN_WIDTH   = 20f;

        // MakeDropPanel's content layout — (8, 8, 6, 6) padding and 4 spacing. The live
        // values are read back from the built group in ApplyDerivedHeight; these are the
        // fallback for computing a height before anything is built.
        internal const float CONTENT_PAD_TOTAL_V = 12f;
        internal const float CONTENT_SPACING     = 4f;

        private Canvas         _canvas;
        private GameObject     _panelRoot;
        private DraggablePanel _drag;
        private GameObject     _firstEntryGo;

        // (button, source entry) so RefreshActiveStates can repaint without rebuilding.
        private readonly List<(Image bg, Button btn, GeneralEditorEntry entry)> _entryButtons =
            new List<(Image, Button, GeneralEditorEntry)>();

        partial void BuildUI()
        {
            if (_uiBuilt) return;
            _uiBuilt = true;

            _canvas = EditorUIHelpers.CreateEditorCanvas("GeneralEditorCanvas", sortOrder: 110);

            _panelRoot = EditorUIHelpers.MakeDropPanel(
                name: "GeneralEditorPanel",
                canvasT: _canvas.transform,
                dock: TileEditorUIHelpers.PanelDock.TopLeft,
                xOff: PANEL_X_OFFSET, yOff: PANEL_Y_OFFSET,
                width: PANEL_WIDTH, height: ComputePanelHeight(_entries),
                title: "General Editor",
                contentOut: out var contentRoot,
                dragOut: out _drag);

            // DraggablePanel furnishes every header with its own close button, and that
            // button hides the PANEL and remembers the choice (PlayerPrefs, then the
            // workspace document). On an editor's sub-panel that is the point. On the
            // launcher it was a soft-lock: the panel went away, the launcher stayed the
            // active editor with the player's input frozen, and the next session opened it
            // invisible — measured: after that click, ESC then ESC left IsActive=true with
            // the panel inactive, and ApplyRememberedVisibility hid it again on boot. The
            // launcher is never "closed" as a panel; it is deactivated as an editor, through
            // the button below, and its panel object stays active for its whole life.
            _drag.ShowCloseButton = false;
            AddCloseButtonToHeader(_panelRoot);

            BuildSection(contentRoot, "EDITORS",     GeneralEditorSection.Editors);
            BuildSection(contentRoot, "DIAGNOSTICS", GeneralEditorSection.Diagnostics);
            BuildSection(contentRoot, "GAME",        GeneralEditorSection.Game);

            ApplyDerivedHeight();
        }

        partial void SetPanelVisible(bool visible)
        {
            if (_canvas == null) return;
            _canvas.gameObject.SetActive(visible);
        }

        partial void RefreshActiveStates()
        {
            for (int i = 0; i < _entryButtons.Count; i++)
            {
                var (bg, btn, entry) = _entryButtons[i];
                if (bg == null || entry?.IsActive == null) continue;
                bool active = false;
                try { active = entry.IsActive(); }
                catch { active = false; }

                var tint = active ? UITheme.ACCENT_BG : UITheme.BTN_NORMAL;
                bg.color = tint;
                // The Button's own transition writes normalColor back onto the graphic
                // whenever the pointer leaves, so an "active" tint set only on the Image
                // survived exactly until the first hover.
                if (btn != null)
                {
                    var c = btn.colors;
                    c.normalColor = tint;
                    btn.colors    = c;
                }
            }
        }

        // ── Keyboard focus ─────────────────────────────────────────────────────
        //
        // Selecting the first entry on open is what makes the grid reachable without a
        // mouse: uGUI's own navigation then moves the selection with the arrows / WASD
        // (gameplay input is suspended while the launcher is up, so those keys are free)
        // and Enter / Space presses it. Selection is dropped on close so a later Enter
        // cannot press a button on a hidden canvas.

        partial void FocusFirstEntry()
        {
            var es = EventSystem.current;
            if (es == null || _firstEntryGo == null) return;
            es.SetSelectedGameObject(_firstEntryGo);
        }

        partial void ReleaseFocus()
        {
            var es = EventSystem.current;
            if (es == null || _canvas == null) return;
            var selected = es.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(_canvas.transform))
                es.SetSelectedGameObject(null);
        }

        // ── Height ─────────────────────────────────────────────────────────────
        //
        // The panel used to be a constant, sized when the registry held eleven editors.
        // At sixteen the content wanted 374 px inside 360: the section headers were
        // squashed from 18 px to 8 and the last row of the Game grid sat below the
        // panel's edge. The height is derived from the registry now, so an entry added
        // to the registry grows the panel instead of clipping it.

        /// <summary>
        /// The panel height that fits every entry: header + separator, the content
        /// padding, three children per section (header, grid, spacer) with the content
        /// spacing between them, and each grid sized from its own row count.
        /// </summary>
        public static float ComputePanelHeight(
            IReadOnlyList<GeneralEditorEntry> entries,
            float contentPadTotalV = CONTENT_PAD_TOTAL_V,
            float contentSpacing   = CONTENT_SPACING)
        {
            int   children = 0;
            float total    = 0f;
            foreach (GeneralEditorSection section in System.Enum.GetValues(typeof(GeneralEditorSection)))
            {
                int count = 0;
                if (entries != null)
                    for (int i = 0; i < entries.Count; i++)
                        if (entries[i].Section == section) count++;

                total    += SECTION_HDR_H + GridHeight(count) + SECTION_SPACING;
                children += 3;
            }
            total += Mathf.Max(0, children - 1) * contentSpacing;
            total += contentPadTotalV;
            total += TileEditorUIHelpers.PANEL_HDR_H + 1f; // header + separator
            return total;
        }

        internal static float GridHeight(int entryCount)
        {
            int rows = (entryCount + GRID_COLUMNS - 1) / GRID_COLUMNS;
            return rows * BUTTON_HEIGHT + Mathf.Max(0, rows - 1) * GRID_SPACING;
        }

        private void ApplyDerivedHeight()
        {
            if (_panelRoot == null) return;

            float padV    = CONTENT_PAD_TOTAL_V;
            float spacing = CONTENT_SPACING;
            var content   = _panelRoot.transform.Find("Content");
            var group     = content != null ? content.GetComponent<VerticalLayoutGroup>() : null;
            if (group != null)
            {
                padV    = group.padding.top + group.padding.bottom;
                spacing = group.spacing;
            }

            var rt = (RectTransform)_panelRoot.transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, ComputePanelHeight(_entries, padV, spacing));
        }

        // ── IProvidesWorkspaceState ────────────────────────────────────────────
        //
        // Panel geometry (where the author dragged the launcher) is captured generically
        // off the DraggablePanel under this root. There is no session state of the
        // launcher's own worth keeping — which button was pressed last is not a workspace.

        public Transform WorkspaceRoot => _canvas != null ? _canvas.transform : null;

        public void CaptureWorkspace(EditorWorkspace workspace) { }

        public void RestoreWorkspace(EditorWorkspace workspace)
        {
            // The restored size is the size the panel HAD; the registry may have grown
            // since. Position is kept, height is re-derived.
            ApplyDerivedHeight();
            if (_panelRoot != null && !_panelRoot.activeSelf) _panelRoot.SetActive(true);
        }

        // ── Panel construction helpers ──────────────────────────────────────────

        private void BuildSection(Transform parent, string title, GeneralEditorSection section)
        {
            // Section header
            var hdrGo = EditorUIHelpers.CreateUI($"Hdr_{section}", parent);
            hdrGo.AddComponent<LayoutElement>().preferredHeight = SECTION_HDR_H;
            var hdrTmp           = hdrGo.AddComponent<TextMeshProUGUI>();
            hdrTmp.text          = title;
            hdrTmp.fontSize      = 10f;
            hdrTmp.fontStyle     = FontStyles.Bold;
            hdrTmp.color         = UITheme.ACCENT;
            hdrTmp.alignment     = TextAlignmentOptions.MidlineLeft;
            hdrTmp.characterSpacing = 1.2f;
            hdrTmp.raycastTarget = false;

            // Grid container for the section's buttons
            var gridGo = EditorUIHelpers.CreateUI($"Grid_{section}", parent);
            var grid   = gridGo.AddComponent<GridLayoutGroup>();
            grid.cellSize        = new Vector2(
                (PANEL_WIDTH - 8 - 8 - (GRID_COLUMNS - 1) * GRID_SPACING) / GRID_COLUMNS,
                BUTTON_HEIGHT);
            grid.spacing         = new Vector2(GRID_SPACING, GRID_SPACING);
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = GRID_COLUMNS;
            grid.childAlignment  = TextAnchor.UpperLeft;

            int count = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.Section != section) continue;
                AddEntryButton(gridGo.transform, entry);
                count++;
            }

            gridGo.AddComponent<LayoutElement>().preferredHeight = GridHeight(count);

            // Trailing spacing so sections don't visually collide
            var spacerGo = EditorUIHelpers.CreateUI($"Spacer_{section}", parent);
            spacerGo.AddComponent<LayoutElement>().preferredHeight = SECTION_SPACING;
        }

        private void AddEntryButton(Transform parent, GeneralEditorEntry entry)
        {
            var bg = EditorUIHelpers.AddActionBtn(
                parent, entry.Label, BUTTON_HEIGHT,
                onClick: () => HandleEntryClicked(entry),
                tmp: out _,
                fontSize: 10f);

            // uGUI's default selectedColor is near-white, so the keyboard-selected entry
            // would light up like a missing texture. Selected reads as hovered.
            var btn = bg.GetComponent<Button>();
            if (btn != null)
            {
                var c = btn.colors;
                c.selectedColor = UITheme.BTN_HOVER;
                btn.colors      = c;
            }

            if (_firstEntryGo == null) _firstEntryGo = bg.gameObject;
            _entryButtons.Add((bg, btn, entry));
        }

        private void HandleEntryClicked(GeneralEditorEntry entry)
        {
            if (entry == null) return;

            // For "ClosesLauncher" actions (Save / Load / Options / Quit), hide
            // the launcher BEFORE invoking — the action may itself open another
            // overlay (pause menu) and we don't want them stacked.
            if (entry.ClosesLauncher) Deactivate();

            try { entry.OnClick?.Invoke(); }
            catch (System.Exception ex)
            { Debug.LogError($"[GeneralEditor] '{entry.Label}' click failed: {ex.Message}"); }

            // After diagnostic toggles the launcher stays open; refresh
            // highlight so the user sees the new on/off state immediately.
            if (_isActive) RefreshActiveStates();
        }

        // ── Header close button ─────────────────────────────────────────────────
        //
        // Styled like the chrome button every other panel carries, but wired to
        // Deactivate: this is the launcher closing as an EDITOR, not a panel hiding.

        private void AddCloseButtonToHeader(GameObject panelRoot)
        {
            // The header GameObject is the first child of the panel root and is
            // built by EditorUIHelpers.MakeDropPanel as a HorizontalLayoutGroup.
            var hdrTr = panelRoot.transform.Find("PanelHeader");
            if (hdrTr == null) return;

            var btnGo = EditorUIHelpers.CreateUI("CloseBtn", hdrTr);
            btnGo.AddComponent<LayoutElement>().preferredWidth = CLOSE_BTN_WIDTH;

            var img = btnGo.AddComponent<Image>();
            img.color = UITheme.DANGER_IDLE;

            var btn = btnGo.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = UITheme.DANGER_IDLE;
            c.highlightedColor = UITheme.DANGER;
            c.pressedColor     = UITheme.DANGER;
            c.selectedColor    = UITheme.DANGER_IDLE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            btn.onClick.AddListener(Deactivate);

            // ASCII "X" (U+0058) ships with LiberationSans SDF; the Unicode
            // multiplication-X (U+2715) does not, which spammed a TMP
            // fallback warning every time a panel was opened.
            var tmp           = UILabel.AddCenteredText(btnGo.transform, "X", 11f, FontStyles.Bold, UITheme.TEXT_PRIMARY);
            tmp.alignment     = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }
    }
}
