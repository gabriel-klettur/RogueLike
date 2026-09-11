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
        internal const float TAB_HEIGHT       = 24f;

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

        // One container and one tab button per section. Sections are built ONCE and shown or
        // hidden after that — the same reason the Controls editor realises its rows per
        // context instead of per keystroke: building a uGUI row costs ~0.3 ms and there are
        // thirty of them here, so rebuilding on every tab click is a visible hitch for a
        // gesture that changes nothing.
        private readonly Dictionary<GeneralEditorSection, GameObject> _sectionRoots =
            new Dictionary<GeneralEditorSection, GameObject>(3);
        private readonly Dictionary<GeneralEditorSection, (Image bg, Button btn, TextMeshProUGUI tmp)> _tabs =
            new Dictionary<GeneralEditorSection, (Image, Button, TextMeshProUGUI)>(3);

        private GeneralEditorSection _activeTab = GeneralEditorSection.Editors;

        /// <summary>Which tab is showing. Read by the tests; the launcher itself never needs it.</summary>
        internal GeneralEditorSection ActiveTab => _activeTab;

        /// <summary>The tab label, kept beside the enum so a section added to one is a compile
        /// error in the other rather than a tab with no name.</summary>
        internal static string TabLabel(GeneralEditorSection section) => section switch
        {
            GeneralEditorSection.Editors => "EDITORES",
            GeneralEditorSection.Tools   => "HERRAMIENTAS",
            GeneralEditorSection.Game    => "JUEGO",
            _                            => section.ToString().ToUpperInvariant(),
        };

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
                width: PANEL_WIDTH, height: ComputePanelHeight(_entries, _activeTab),
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

            BuildTabStrip(contentRoot);
            foreach (GeneralEditorSection section in System.Enum.GetValues(typeof(GeneralEditorSection)))
                BuildSection(contentRoot, section);

            SelectTab(_activeTab);
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
                // Only the open tab is on screen; repainting the other 24 buttons asks every
                // one of their IsActive delegates a question nobody can see the answer to.
                if (entry.Section != _activeTab) continue;
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
        /// The panel height for ONE tab — the one that is open.
        ///
        /// <para>It sized itself to the TALLEST tab first, so the panel never resized. Measured,
        /// that left it <b>60 % empty</b> on two tabs of three: Editors wants 228 px of grid and
        /// Tools and Game want 56 each, so 180 px of the window was void. A panel that is mostly
        /// hole reads as half-built, and it is a worse trade than a resize the author asked for
        /// by clicking a tab.</para>
        ///
        /// <para>Safe to resize because the height is DERIVED and never persisted: the workspace
        /// records where the panel was dragged, not how tall it was, so this cannot accumulate
        /// the way the Controls editor's geometry drifted across three opens.</para>
        ///
        /// <para>Derived from the registry rather than a constant, for the reason the stacked
        /// version was: 360 px was a constant sized when the registry held eleven editors, and at
        /// sixteen the content wanted 374 px inside it — headers squashed from 18 px to 8 and the
        /// last row below the panel's edge.</para>
        /// </summary>
        public static float ComputePanelHeight(
            IReadOnlyList<GeneralEditorEntry> entries,
            GeneralEditorSection section,
            float contentPadTotalV = CONTENT_PAD_TOTAL_V,
            float contentSpacing   = CONTENT_SPACING)
        {
            int count = 0;
            if (entries != null)
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i].Section == section) count++;

            // Content children are the tab strip and the one visible grid: one spacing between.
            // No section header any more — the tab strip already names the open section, and
            // printing "HERRAMIENTAS" again 4 px under the lit "HERRAMIENTAS" tab spent 18 px
            // saying nothing.
            float total = TAB_HEIGHT + contentSpacing + GridHeight(count);
            total += SECTION_SPACING;                       // breathing room under the grid
            total += contentPadTotalV;
            total += TileEditorUIHelpers.PANEL_HDR_H + 1f;  // header + separator
            return total;
        }

        /// <summary>The height of the tallest tab — what the panel must never be shorter than
        /// if it is ever pinned to one size again. Kept for the tests, which assert that no tab
        /// clips, independently of which one happens to be open.</summary>
        public static float TallestTabHeight(IReadOnlyList<GeneralEditorEntry> entries)
        {
            float tallest = 0f;
            foreach (GeneralEditorSection s in System.Enum.GetValues(typeof(GeneralEditorSection)))
            {
                float h = ComputePanelHeight(entries, s);
                if (h > tallest) tallest = h;
            }
            return tallest;
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
            rt.sizeDelta = new Vector2(rt.sizeDelta.x,
                ComputePanelHeight(_entries, _activeTab, padV, spacing));
        }

        // ── IProvidesWorkspaceState ────────────────────────────────────────────
        //
        // Panel geometry (where the author dragged the launcher) is captured generically
        // off the DraggablePanel under this root. There is no session state of the
        // launcher's own worth keeping — which button was pressed last is not a workspace.

        public Transform WorkspaceRoot => _canvas != null ? _canvas.transform : null;

        public void CaptureWorkspace(EditorWorkspace workspace)
        {
            // WHICH TAB, and nothing else. An author spending a session in HERRAMIENTAS
            // reopened onto EDITORES every time, which is a choice the launcher watched them
            // make and then threw away. Which BUTTON was pressed last is still not a
            // workspace — that is a history, not a layout.
            workspace?.SetString("tab", _activeTab.ToString());
        }

        public void RestoreWorkspace(EditorWorkspace workspace)
        {
            if (workspace != null &&
                System.Enum.TryParse(workspace.GetString("tab", ""), out GeneralEditorSection saved) &&
                System.Enum.IsDefined(typeof(GeneralEditorSection), saved))
            {
                // Parse rather than cast: a document written before a section was renamed
                // holds a name this build does not have, and TryParse answers false where a
                // cast would happily produce an out-of-range enum and hide every tab.
                _activeTab = saved;
                if (_uiBuilt) SelectTab(_activeTab);
            }

            // The restored size is the size the panel HAD; the registry may have grown
            // since, and the open tab decides the height. Position is kept, height re-derived.
            ApplyDerivedHeight();
            if (_panelRoot != null && !_panelRoot.activeSelf) _panelRoot.SetActive(true);
        }

        // ── Panel construction helpers ──────────────────────────────────────────

        /// <summary>
        /// The tab strip. Three buttons in a row, each switching which section is on screen.
        ///
        /// <para>A button in a <c>HorizontalLayoutGroup</c> with <c>childControlWidth</c> and
        /// no <c>childForceExpandWidth</c> is laid out at its MINIMUM — which in the Controls
        /// editor collapsed two buttons into a single overprinted character column. Both flags
        /// are set here for that reason.</para>
        /// </summary>
        private void BuildTabStrip(Transform parent)
        {
            var rowGo = EditorUIHelpers.CreateUI("Tabs", parent);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = TAB_HEIGHT;

            // flexibleHeight = 0 IS THE WHOLE FIX, and without it this strip renders at 180 px.
            //
            // uGUI resolves each layout property INDEPENDENTLY, from the highest-priority
            // component that supplies one. The LayoutElement wins the PREFERRED height and
            // leaves flexibleHeight unset (-1), so the value actually used comes from the
            // HorizontalLayoutGroup on this same GameObject, which reports 1 because
            // childForceExpandHeight is on. This row was then the content's only flexible
            // child and absorbed every spare pixel the open tab did not use: measured at
            // 274 - 12 - 78 - 4 = 180, three giant vertical bars with a label floating in the
            // middle of each.
            //
            // Same trap the chat input row records, and the note did not prevent it — only
            // reading the rendered frame did. Any row carrying both components needs this.
            le.flexibleHeight = 0f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = GRID_SPACING;
            hlg.childControlWidth      = true;
            hlg.childForceExpandWidth  = true;
            hlg.childControlHeight     = true;
            hlg.childForceExpandHeight = true;

            foreach (GeneralEditorSection section in System.Enum.GetValues(typeof(GeneralEditorSection)))
            {
                var captured = section;
                var bg = EditorUIHelpers.AddActionBtn(rowGo.transform, TabLabel(section), TAB_HEIGHT,
                    onClick: () => SelectTab(captured), tmp: out var tmp, fontSize: 9f);
                _tabs[section] = (bg, bg.GetComponent<Button>(), tmp);
            }
        }

        /// <summary>Show one section, hide the others, and repaint the strip.</summary>
        internal void SelectTab(GeneralEditorSection section)
        {
            _activeTab = section;

            foreach (var kv in _sectionRoots)
                if (kv.Value != null) kv.Value.SetActive(kv.Key == section);

            foreach (var kv in _tabs)
            {
                var (bg, btn, tmp) = kv.Value;
                bool on = kv.Key == section;
                // Through UIButton.SetTint rather than by writing the Image: a Button on the
                // default ColorTint transition multiplies its ColorBlock into the graphic, so
                // a raw write renders the product and the ACTIVE tab comes out darker than
                // the inactive ones — measured at 3 vs 108 luminance in the Skills editor.
                if (btn != null) UIButton.SetTint(btn, on ? UITheme.ACCENT_BG : UITheme.BTN_NORMAL);
                else if (bg != null) bg.color = on ? UITheme.ACCENT_BG : UITheme.BTN_NORMAL;
                if (tmp != null) tmp.color = on ? UITheme.ACCENT : UITheme.TEXT_SECONDARY;
            }

            // The keyboard focus was on a button that may now be hidden; pressing Enter on a
            // disabled object does nothing, which reads as the launcher having stopped
            // responding. Re-seat it on the first entry of the tab just opened.
            _firstEntryGo = FirstEntryOf(section);
            FocusFirstEntry();

            // The panel holds ONE tab, so switching tabs is a resize.
            ApplyDerivedHeight();
        }

        private GameObject FirstEntryOf(GeneralEditorSection section)
        {
            for (int i = 0; i < _entryButtons.Count; i++)
            {
                var (bg, _, entry) = _entryButtons[i];
                if (bg != null && entry != null && entry.Section == section) return bg.gameObject;
            }
            return null;
        }

        private void BuildSection(Transform parent, GeneralEditorSection section)
        {
            // One container per tab, shown or hidden by SelectTab. It carries its own vertical
            // layout so the header and the grid stack inside it exactly as they used to inside
            // the panel — the tab strip is the only thing that changed above them.
            var sectionGo = EditorUIHelpers.CreateUI($"Section_{section}", parent);
            var sectionVlg = sectionGo.AddComponent<VerticalLayoutGroup>();
            sectionVlg.spacing                = CONTENT_SPACING;
            sectionVlg.childControlWidth      = true;
            sectionVlg.childForceExpandWidth  = true;
            sectionVlg.childControlHeight     = true;
            sectionVlg.childForceExpandHeight = false;
            _sectionRoots[section] = sectionGo;

            parent = sectionGo.transform;

            // NO section header. The lit tab above already says which section this is, and
            // printing the same word again 4 px under it spent 18 px twice over.

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

            // No trailing spacer: sections cannot collide any more because only one is ever on
            // screen. The breathing room under the grid is the SECTION_SPACING that
            // ComputePanelHeight adds once, outside the tab.
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
