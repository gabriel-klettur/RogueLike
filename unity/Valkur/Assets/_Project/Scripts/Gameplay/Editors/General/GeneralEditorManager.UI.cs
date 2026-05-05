using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.General
{
    public partial class GeneralEditorManager
    {
        // ── Layout constants (kept here so UI is self-describing) ──────────────
        private const float PANEL_WIDTH       = 280f;
        private const float PANEL_HEIGHT      = 360f;
        private const float PANEL_X_OFFSET    = 8f;
        private const float PANEL_Y_OFFSET    = TileEditorUIHelpers.PANEL_TOP_OFFSET;
        private const float SECTION_HDR_H     = 18f;
        private const float SECTION_SPACING   = 6f;
        private const float BUTTON_HEIGHT     = 26f;
        private const float CLOSE_BTN_WIDTH   = 22f;
        private const int   GRID_COLUMNS      = 3;

        private Canvas      _canvas;
        private GameObject  _panelRoot;

        // (button background image, source entry) pairs so RefreshActiveStates
        // can repaint without rebuilding the whole panel.
        private readonly List<(Image bg, GeneralEditorEntry entry)> _entryButtons =
            new List<(Image, GeneralEditorEntry)>();

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
                width: PANEL_WIDTH, height: PANEL_HEIGHT,
                title: "General Editor",
                contentOut: out var contentRoot,
                dragOut: out _);

            AddCloseButtonToHeader(_panelRoot);

            BuildSection(contentRoot, "EDITORS",     GeneralEditorSection.Editors);
            BuildSection(contentRoot, "DIAGNOSTICS", GeneralEditorSection.Diagnostics);
            BuildSection(contentRoot, "GAME",        GeneralEditorSection.Game);
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
                var (bg, entry) = _entryButtons[i];
                if (bg == null || entry?.IsActive == null) continue;
                bool active = false;
                try { active = entry.IsActive(); }
                catch { active = false; }
                bg.color = active ? UITheme.ACCENT_BG : UITheme.BTN_NORMAL;
            }
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
                (PANEL_WIDTH - 8 - 8 - (GRID_COLUMNS - 1) * 4f) / GRID_COLUMNS,
                BUTTON_HEIGHT);
            grid.spacing         = new Vector2(4f, 4f);
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = GRID_COLUMNS;
            grid.childAlignment  = TextAnchor.UpperLeft;

            int rows = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.Section != section) continue;
                AddEntryButton(gridGo.transform, entry);
                rows++;
            }

            int rowCount = (rows + GRID_COLUMNS - 1) / GRID_COLUMNS;
            float gridHeight = rowCount * BUTTON_HEIGHT + Mathf.Max(0, rowCount - 1) * 4f;
            gridGo.AddComponent<LayoutElement>().preferredHeight = gridHeight;

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

            _entryButtons.Add((bg, entry));
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

        // ── Header close button (small "✕" appended after the title) ──────────

        private void AddCloseButtonToHeader(GameObject panelRoot)
        {
            // The header GameObject is the first child of the panel root and is
            // built by EditorUIHelpers.MakeDropPanel as a HorizontalLayoutGroup.
            var hdrTr = panelRoot.transform.Find("PanelHeader");
            if (hdrTr == null) return;

            var btnGo = EditorUIHelpers.CreateUI("CloseBtn", hdrTr);
            btnGo.AddComponent<LayoutElement>().preferredWidth = CLOSE_BTN_WIDTH;

            var img = btnGo.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // transparent — header bg shows through

            var btn = btnGo.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = new Color(1f, 1f, 1f, 0f);
            c.highlightedColor = new Color(1f, 1f, 1f, 0.10f);
            c.pressedColor     = new Color(1f, 1f, 1f, 0.18f);
            btn.colors         = c;
            btn.targetGraphic  = img;
            btn.onClick.AddListener(Deactivate);

            var tmp           = UILabel.AddCenteredText(btnGo.transform, "✕", 12f, FontStyles.Bold, UITheme.TEXT_PRIMARY);
            tmp.alignment     = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }
    }
}
