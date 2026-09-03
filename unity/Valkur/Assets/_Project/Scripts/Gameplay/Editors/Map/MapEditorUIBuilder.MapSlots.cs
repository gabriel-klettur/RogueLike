using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Maps explorer panel for the Map Editor — the user picks which "universe"
    /// (set of zones) is active. Save As / Load / Rename / Delete / New on the
    /// per-slot JSON snapshots managed by <see cref="MapEditorMapSlots"/>.
    /// Built as a partial to keep the original UIBuilder file readable.
    /// </summary>
    public static partial class MapEditorUIBuilder
    {
        public partial struct UIRefs
        {
            // Menu bar
            public Image           MapsMenuBtnImg;
            public TextMeshProUGUI MapsMenuBtnTmp;

            // Floating panel
            public GameObject      MapsDropdown;
            public DraggablePanel  MapsPanelDrag;

            // Panel widgets
            public TextMeshProUGUI MapsActiveLabel;
            public ScrollRect      MapsListScroll;
            public RectTransform   MapsListContent;

            // Modal dialogs
            public GameObject      MapsDeleteDialog;
            public TextMeshProUGUI MapsDeletePrompt;
            public TextMeshProUGUI MapsDeleteConfirmBtnLabel;
            public GameObject      MapsNewDialog;
            public TMP_InputField  MapsNewNameInput;
            public GameObject      MapsRenameDialog;
            public TextMeshProUGUI MapsRenamePrompt;
            public TMP_InputField  MapsRenameNameInput;

            // Loading overlay (covers the canvas while a slot load runs).
            public GameObject      MapsLoadingOverlay;
            public TextMeshProUGUI MapsLoadingLabel;
            // Reusable progress-bar widget — same chrome as the boot loading
            // screen. Updated each frame from MapEditorUI.Update while the
            // overlay is visible; SetTargetProgress / SetStatus drive it.
            public Valkur.UIKit.LoadingBarWidget MapsLoadingBar;
            // Background image swapped by the teleport-art rotation on every
            // ShowMapsLoadingOverlay call.
            public Image           MapsLoadingBgImage;
            public AspectRatioFitter MapsLoadingBgFitter;

            // Mutable state shared across click handlers
            public MapSlotsDialogState MapsState;
        }

        /// <summary>
        /// Callbacks the UI raises into <see cref="MapEditorManager"/> for slot
        /// operations. Bundled so the <see cref="BuildAll"/> signature stays
        /// readable as the editor grows.
        /// </summary>
        public struct MapSlotCallbacks
        {
            public Action<string>          OnLoad;
            public Action<string>          OnDelete;
            public Action<string, string>  OnRename;
            public Action<string>          OnNew;
            public Func<string[]>          ListSlots;
            public Func<string>            GetActive;
            // Backup hooks — exposed from the same panel because they operate
            // on the same "active slot" concept. OnOpenBackupBrowser spawns
            // the existing MapBackupBrowserUI; OnCreateBackupNow fires a
            // manual snapshot of the active slot and returns a status string
            // for the editor status bar.
            public Action                  OnOpenBackupBrowser;
            public Func<string>            OnCreateBackupNow;
        }

        public class MapSlotsDialogState
        {
            public string SelectedSlot;
            public string DeleteTargetSlot;
            public string RenameTargetSlot;
            // Two-stage delete: 1 = "Delete '<x>'?", 2 = "Permanent — confirm again".
            // The Delete button inside the dialog reads this flag to decide
            // whether the click escalates the prompt or actually fires OnDelete.
            public int    DeleteConfirmStage;
        }

        // ── Sizes ────────────────────────────────────────────────────────────────

        private const float MAPS_PANEL_W            = 280f;
        private const float MAPS_PANEL_H            = 380f + PANEL_HDR_H;
        private const float MAPS_LIST_MIN_H         = 200f;
        private const float MAPS_LIST_SCROLLBAR_W   = 12f;
        private const float MAPS_DELETE_DIALOG_W    = 430f;
        private const float MAPS_DELETE_DIALOG_H    = 170f + PANEL_HDR_H;
        private const float MAPS_NEW_DIALOG_W       = 430f;
        private const float MAPS_NEW_DIALOG_H       = 150f + PANEL_HDR_H;
        private const float MAPS_RENAME_DIALOG_W    = 430f;
        private const float MAPS_RENAME_DIALOG_H    = 180f + PANEL_HDR_H;

        // ── Build entry ──────────────────────────────────────────────────────────

        private static void BuildMapsPanel(Transform canvasT, ref UIRefs refs,
            MapSlotCallbacks callbacks)
        {
            float x = PANEL_GAP + 280f /*ZONES_W*/ + PANEL_GAP + 230f /*ACTIONS_W*/ + PANEL_GAP +
                      BIOMES_PANEL_W + PANEL_GAP;

            refs.MapsDropdown = MakeDrop("MapsPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                MAPS_PANEL_W, MAPS_PANEL_H, "MAPS",
                out var t, out refs.MapsPanelDrag);

            var state = new MapSlotsDialogState();
            refs.MapsState = state;

            BuildSectionLabel(t, "Active map");

            var activeGo = CreateUI("ActiveMap", t);
            activeGo.AddComponent<LayoutElement>().preferredHeight = 18f;
            refs.MapsActiveLabel = activeGo.AddComponent<TextMeshProUGUI>();
            refs.MapsActiveLabel.text       = "—";
            refs.MapsActiveLabel.fontSize   = 12f;
            refs.MapsActiveLabel.fontStyle  = FontStyles.Bold;
            refs.MapsActiveLabel.color      = ACCENT;
            refs.MapsActiveLabel.alignment  = TextAlignmentOptions.Left;

            BuildSeparator(t);
            BuildSectionLabel(t, "Saved maps");

            var listScroll = MakeMapsScrollView(t, out var content);
            var listLE = listScroll.AddComponent<LayoutElement>();
            listLE.flexibleHeight = 1f;
            listLE.minHeight      = MAPS_LIST_MIN_H;
            refs.MapsListScroll  = listScroll.GetComponent<ScrollRect>();
            refs.MapsListContent = content;

            BuildSeparator(t);

            // Per-row action buttons (Rename / Load / Delete) live on each
            // row in the saved-maps list — see RebuildMapsList. The only
            // panel-level action that remains is "New map".
            BuildMapsRenameDialog(canvasT, ref refs, callbacks);
            BuildMapsDeleteDialog(canvasT, ref refs, callbacks);
            BuildMapsNewDialog(canvasT, ref refs, callbacks);
            BuildMapsLoadingOverlay(canvasT, ref refs);

            var localNewDialog    = refs.MapsNewDialog;
            var localNewNameInput = refs.MapsNewNameInput;
            AddActionBtn(t, "New map", BTN_H, () =>
            {
                if (localNewNameInput != null) localNewNameInput.text = "";
                if (localNewDialog != null) localNewDialog.SetActive(true);
            });

            // Backup access from inside F11 so the user never has to leave
            // the Map Editor to snapshot or browse. Two buttons mirror the
            // two most common operations: snapshot the current state and
            // browse / restore prior snapshots.
            var localOnSnapshot = callbacks.OnCreateBackupNow;
            AddActionBtn(t, "Snapshot now", BTN_H, () => localOnSnapshot?.Invoke());

            var localOnOpenBrowser = callbacks.OnOpenBackupBrowser;
            AddActionBtn(t, "Backups…", BTN_H, () => localOnOpenBrowser?.Invoke());

            refs.MapsDropdown.SetActive(false);
        }

        // ── Per-row dialog openers ───────────────────────────────────────────────
        // Used both by RebuildMapsList (per-row Rename/Delete buttons) and by
        // any future external trigger.

        public static void OpenRenameDialogForSlot(UIRefs refs, string slot)
        {
            if (string.IsNullOrEmpty(slot) || refs.MapsRenameDialog == null) return;
            if (refs.MapsState != null) refs.MapsState.RenameTargetSlot = slot;
            bool isDefault = string.Equals(slot,
                MapEditorMapSlots.DEFAULT_SLOT, StringComparison.OrdinalIgnoreCase);
            if (refs.MapsRenamePrompt != null)
                refs.MapsRenamePrompt.text = isDefault
                    ? "The 'default' map is the implicit baseline and cannot be renamed."
                    : $"Rename map '{slot}' to:";
            if (refs.MapsRenameNameInput != null)
                refs.MapsRenameNameInput.text = isDefault ? string.Empty : slot;
            refs.MapsRenameDialog.SetActive(true);
        }

        public static void OpenDeleteDialogForSlot(UIRefs refs, string slot)
        {
            if (string.IsNullOrEmpty(slot) || refs.MapsDeleteDialog == null) return;
            if (refs.MapsState != null)
            {
                refs.MapsState.DeleteTargetSlot   = slot;
                refs.MapsState.DeleteConfirmStage = 1;
            }
            bool isDefault = string.Equals(slot,
                MapEditorMapSlots.DEFAULT_SLOT, StringComparison.OrdinalIgnoreCase);
            if (refs.MapsDeletePrompt != null)
                refs.MapsDeletePrompt.text = isDefault
                    ? "The 'default' map is the implicit baseline and cannot be deleted."
                    : $"Delete map '{slot}'?";
            if (refs.MapsDeleteConfirmBtnLabel != null)
                refs.MapsDeleteConfirmBtnLabel.text = "Delete";
            refs.MapsDeleteDialog.SetActive(true);
        }

        // ── Dialogs ──────────────────────────────────────────────────────────────

        private static void BuildMapsDeleteDialog(Transform canvasT, ref UIRefs refs,
            MapSlotCallbacks callbacks)
        {
            var go = MakeDrop("MapsDeleteDialog", canvasT,
                PanelDock.TopLeft,
                /*x*/ 80f, /*y*/ PANEL_TOP_OFFSET + 80f,
                MAPS_DELETE_DIALOG_W, MAPS_DELETE_DIALOG_H,
                "CONFIRM DELETE MAP",
                out var t, out _);
            refs.MapsDeleteDialog = go;

            var ol = go.GetComponent<Outline>();
            if (ol != null) ol.effectColor = new Color(1f, 0.32f, 0.36f, 0.6f);

            var promptGo = CreateUI("Prompt", t);
            promptGo.AddComponent<LayoutElement>().preferredHeight = 36f;
            refs.MapsDeletePrompt = promptGo.AddComponent<TextMeshProUGUI>();
            refs.MapsDeletePrompt.text      = "Delete map?";
            refs.MapsDeletePrompt.fontSize  = 13f;
            refs.MapsDeletePrompt.color     = TEXT_PRIMARY;
            refs.MapsDeletePrompt.alignment = TextAlignmentOptions.Left;
            refs.MapsDeletePrompt.enableWordWrapping = true;

            BuildSeparator(t);

            var localGo    = go;
            var localState = refs.MapsState;
            var btnRow = MakeRow("MapsDelDialogBtns", t, BTN_H);

            // Two-stage delete: stage 1 → "Delete" prompts the permanent
            // confirmation, stage 2 → "Confirm Delete" actually fires the
            // OnDelete callback. Lets the user back out of an accidental
            // first click without losing the dialog state.
            var deleteBtn = AddActionBtn(btnRow.transform, "Delete", BTN_H, null, danger: true);
            refs.MapsDeleteConfirmBtnLabel = deleteBtn.GetComponentInChildren<TextMeshProUGUI>();
            var localPrompt    = refs.MapsDeletePrompt;
            var localBtnLabel  = refs.MapsDeleteConfirmBtnLabel;
            deleteBtn.onClick.AddListener(() =>
            {
                if (localState == null || string.IsNullOrEmpty(localState.DeleteTargetSlot))
                    return;
                bool isDefault = string.Equals(localState.DeleteTargetSlot,
                    MapEditorMapSlots.DEFAULT_SLOT, StringComparison.OrdinalIgnoreCase);
                if (isDefault) return; // dialog already shows the protection notice

                if (localState.DeleteConfirmStage <= 1)
                {
                    // Escalate to stage 2 — make it abundantly clear that this
                    // is permanent before firing OnDelete.
                    localState.DeleteConfirmStage = 2;
                    if (localPrompt != null)
                        localPrompt.text =
                            $"This will permanently remove '{localState.DeleteTargetSlot}'. " +
                            "Click 'Confirm Delete' again to proceed.";
                    if (localBtnLabel != null) localBtnLabel.text = "Confirm Delete";
                    return;
                }

                // Stage 2 — fire delete and close.
                callbacks.OnDelete?.Invoke(localState.DeleteTargetSlot);
                localState.DeleteTargetSlot   = null;
                localState.DeleteConfirmStage = 0;
                localGo.SetActive(false);
            });

            AddActionBtn(btnRow.transform, "Cancel", BTN_H, () =>
            {
                if (localState != null)
                {
                    localState.DeleteTargetSlot   = null;
                    localState.DeleteConfirmStage = 0;
                }
                localGo.SetActive(false);
            });

            go.SetActive(false);
        }

        private static void BuildMapsNewDialog(Transform canvasT, ref UIRefs refs,
            MapSlotCallbacks callbacks)
        {
            var go = MakeDrop("MapsNewDialog", canvasT,
                PanelDock.TopLeft,
                /*x*/ 80f, /*y*/ PANEL_TOP_OFFSET + 80f,
                MAPS_NEW_DIALOG_W, MAPS_NEW_DIALOG_H,
                "NEW MAP",
                out var t, out _);
            refs.MapsNewDialog = go;

            BuildSectionLabel(t, "New map name");

            var nameHost = CreateUI("MapsNewNameHost", t);
            nameHost.AddComponent<LayoutElement>().preferredHeight = 30f;
            refs.MapsNewNameInput = MakeTmpInput(nameHost, "untitled");

            BuildSeparator(t);

            var localGo        = go;
            var localNewInput  = refs.MapsNewNameInput;
            var btnRow = MakeRow("MapsNewDialogBtns", t, BTN_H);
            AddActionBtn(btnRow.transform, "Create", BTN_H, () =>
            {
                callbacks.OnNew?.Invoke(localNewInput != null ? localNewInput.text : string.Empty);
                localGo.SetActive(false);
            });
            AddActionBtn(btnRow.transform, "Cancel", BTN_H, () => { localGo.SetActive(false); });

            go.SetActive(false);
        }

        private static void BuildMapsRenameDialog(Transform canvasT, ref UIRefs refs,
            MapSlotCallbacks callbacks)
        {
            var go = MakeDrop("MapsRenameDialog", canvasT,
                PanelDock.TopLeft,
                /*x*/ 80f, /*y*/ PANEL_TOP_OFFSET + 80f,
                MAPS_RENAME_DIALOG_W, MAPS_RENAME_DIALOG_H,
                "RENAME MAP",
                out var t, out _);
            refs.MapsRenameDialog = go;

            var promptGo = CreateUI("Prompt", t);
            promptGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            refs.MapsRenamePrompt = promptGo.AddComponent<TextMeshProUGUI>();
            refs.MapsRenamePrompt.text      = "Rename map to:";
            refs.MapsRenamePrompt.fontSize  = 13f;
            refs.MapsRenamePrompt.color     = TEXT_PRIMARY;
            refs.MapsRenamePrompt.alignment = TextAlignmentOptions.Left;
            refs.MapsRenamePrompt.enableWordWrapping = true;

            BuildSeparator(t);

            var nameHost = CreateUI("MapsRenameNameHost", t);
            nameHost.AddComponent<LayoutElement>().preferredHeight = 30f;
            refs.MapsRenameNameInput = MakeTmpInput(nameHost, "renamed_map");

            BuildSeparator(t);

            var localGo        = go;
            var localState     = refs.MapsState;
            var localRenameIn  = refs.MapsRenameNameInput;
            var btnRow = MakeRow("MapsRenameDialogBtns", t, BTN_H);
            AddActionBtn(btnRow.transform, "Confirm", BTN_H, () =>
            {
                if (localState != null && !string.IsNullOrEmpty(localState.RenameTargetSlot))
                {
                    string newName = localRenameIn != null ? localRenameIn.text : string.Empty;
                    bool isDefault = string.Equals(localState.RenameTargetSlot,
                        MapEditorMapSlots.DEFAULT_SLOT, StringComparison.OrdinalIgnoreCase);
                    if (!isDefault)
                        callbacks.OnRename?.Invoke(localState.RenameTargetSlot, newName);
                }
                if (localState != null) localState.RenameTargetSlot = null;
                localGo.SetActive(false);
            });
            AddActionBtn(btnRow.transform, "Cancel", BTN_H, () =>
            {
                if (localState != null) localState.RenameTargetSlot = null;
                localGo.SetActive(false);
            });

            go.SetActive(false);
        }

        // ── Slot list rendering ──────────────────────────────────────────────────

        // Sizes shared by all rows in the saved-maps list.
        private const float MAPS_ROW_H              = 24f;
        private const float MAPS_ROW_RENAME_W       = 56f;
        private const float MAPS_ROW_LOAD_W         = 44f;
        private const float MAPS_ROW_DELETE_W       = 28f;
        private const float MAPS_ROW_OUTLINE_PX     = 2.5f;
        private static readonly Color SELECTED_OUTLINE_COLOR =
            new Color(1f, 0.85f, 0f, 1f); // bright thick yellow

        /// <summary>
        /// Rebuild the saved-maps list. Each row now contains the slot name
        /// (clickable for selection, double-click loads) plus inline action
        /// buttons Rename / Load / Delete. The currently-selected row carries
        /// a thick yellow outline; the active slot additionally renders with
        /// the canonical BTN_ACTIVE background + ACCENT text colour.
        /// </summary>
        public static void RebuildMapsList(UIRefs refs, string[] slots, string activeSlot,
            Action<string> onRowSelected,
            Action<string> onRowLoad)
        {
            if (refs.MapsListContent == null) return;

            for (int i = refs.MapsListContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(refs.MapsListContent.GetChild(i).gameObject);

            if (refs.MapsActiveLabel != null)
                refs.MapsActiveLabel.text = string.IsNullOrEmpty(activeSlot)
                    ? "—" : activeSlot;

            if (slots == null || slots.Length == 0)
            {
                var emptyGo = CreateUI("EmptyHint", refs.MapsListContent);
                emptyGo.AddComponent<LayoutElement>().preferredHeight = 22f;
                var t = emptyGo.AddComponent<TextMeshProUGUI>();
                t.text      = "(no map slots)";
                t.fontSize  = 11f;
                t.color     = TEXT_SECONDARY;
                t.alignment = TextAlignmentOptions.Center;
                return;
            }

            const float DOUBLE_CLICK_SEC = 0.4f;
            float lastClickTime = -10f;
            string lastClickSlot = null;

            // Outlines tracked per-row so the selection visual updates on the
            // fly without triggering a full list rebuild.
            var rowOutlines = new List<(string slot, Outline outline)>(slots.Length);

            void RefreshSelectionVisuals(string selectedSlot)
            {
                foreach (var (s, o) in rowOutlines)
                {
                    if (o == null) continue;
                    bool isSel = string.Equals(s, selectedSlot, StringComparison.OrdinalIgnoreCase);
                    o.effectColor = isSel
                        ? SELECTED_OUTLINE_COLOR
                        : Color.clear;
                    o.effectDistance = isSel
                        ? new Vector2(MAPS_ROW_OUTLINE_PX, MAPS_ROW_OUTLINE_PX)
                        : Vector2.zero;
                }
            }

            UIRefs refsCapture = refs;
            for (int i = 0; i < slots.Length; i++)
            {
                string slot   = slots[i];
                bool isActive = string.Equals(slot, activeSlot, StringComparison.OrdinalIgnoreCase);
                bool isDefault = string.Equals(slot, MapEditorMapSlots.DEFAULT_SLOT,
                    StringComparison.OrdinalIgnoreCase);

                var rowGo = CreateUI($"Row_{slot}", refs.MapsListContent);
                rowGo.AddComponent<LayoutElement>().preferredHeight = MAPS_ROW_H;
                var hg = rowGo.AddComponent<HorizontalLayoutGroup>();
                hg.padding = new RectOffset(0, 0, 0, 0);
                hg.spacing = 2f;
                hg.childControlWidth      = true;
                hg.childControlHeight     = true;
                hg.childForceExpandWidth  = false;
                hg.childForceExpandHeight = true;

                // ── Name button (selection target + double-click → Load) ─────
                var nameBtn = AddActionBtn(rowGo.transform,
                    isActive ? slot + "  [active]" : slot, MAPS_ROW_H, null);
                var nameLE = nameBtn.GetComponent<LayoutElement>();
                if (nameLE == null) nameLE = nameBtn.gameObject.AddComponent<LayoutElement>();
                nameLE.flexibleWidth  = 1f;
                nameLE.preferredWidth = -1f;
                if (isActive)
                {
                    var img = nameBtn.GetComponent<Image>();
                    if (img != null) img.color = BTN_ACTIVE;
                    var label = nameBtn.GetComponentInChildren<TextMeshProUGUI>();
                    if (label != null) label.color = ACCENT;
                }

                var outline = nameBtn.gameObject.AddComponent<Outline>();
                outline.effectColor    = Color.clear;
                outline.effectDistance = Vector2.zero;
                rowOutlines.Add((slot, outline));

                string capturedSlot = slot;
                nameBtn.onClick.AddListener(() =>
                {
                    float now = Time.unscaledTime;
                    bool isDouble = (now - lastClickTime) <= DOUBLE_CLICK_SEC
                        && string.Equals(lastClickSlot, capturedSlot, StringComparison.OrdinalIgnoreCase);
                    lastClickTime = now;
                    lastClickSlot = capturedSlot;

                    onRowSelected?.Invoke(capturedSlot);
                    RefreshSelectionVisuals(capturedSlot);
                    if (isDouble) onRowLoad?.Invoke(capturedSlot);
                });

                // ── Rename ───────────────────────────────────────────────────
                var renameBtn = AddActionBtn(rowGo.transform, "Rename", MAPS_ROW_H, null);
                FixRowBtnWidth(renameBtn, MAPS_ROW_RENAME_W);
                if (isDefault) DimButton(renameBtn);
                renameBtn.onClick.AddListener(() =>
                {
                    onRowSelected?.Invoke(capturedSlot);
                    RefreshSelectionVisuals(capturedSlot);
                    OpenRenameDialogForSlot(refsCapture, capturedSlot);
                });

                // ── Load ─────────────────────────────────────────────────────
                var loadBtn = AddActionBtn(rowGo.transform, "Load", MAPS_ROW_H, null);
                FixRowBtnWidth(loadBtn, MAPS_ROW_LOAD_W);
                loadBtn.onClick.AddListener(() =>
                {
                    onRowSelected?.Invoke(capturedSlot);
                    RefreshSelectionVisuals(capturedSlot);
                    onRowLoad?.Invoke(capturedSlot);
                });

                // ── Delete ───────────────────────────────────────────────────
                var deleteBtn = AddActionBtn(rowGo.transform, "X", MAPS_ROW_H, null, danger: true);
                FixRowBtnWidth(deleteBtn, MAPS_ROW_DELETE_W);
                if (isDefault) DimButton(deleteBtn);
                deleteBtn.onClick.AddListener(() =>
                {
                    onRowSelected?.Invoke(capturedSlot);
                    RefreshSelectionVisuals(capturedSlot);
                    OpenDeleteDialogForSlot(refsCapture, capturedSlot);
                });
            }

            // Apply the initial selection outline (preserves the previously
            // selected slot across rebuilds; otherwise the active slot is the
            // sensible default).
            string initialSelected = refs.MapsState?.SelectedSlot;
            if (string.IsNullOrEmpty(initialSelected)) initialSelected = activeSlot;
            RefreshSelectionVisuals(initialSelected);
        }

        private static void FixRowBtnWidth(Button btn, float width)
        {
            var le = btn.GetComponent<LayoutElement>();
            if (le == null) le = btn.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth  = 0f;
            le.preferredWidth = width;
            le.minWidth       = width;
        }

        private static void DimButton(Button btn)
        {
            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                var c = img.color; c.a = 0.45f; img.color = c;
            }
            btn.interactable = false;
        }

        // ── Loading overlay ──────────────────────────────────────────────────────

        private static void BuildMapsLoadingOverlay(Transform canvasT, ref UIRefs refs)
        {
            var go = CreateUI("MapsLoadingOverlay", canvasT);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Click-blocker layer so the user can't fire input while a slot
            // load runs. Black so the corners stay solid even when the
            // letterboxed teleport art doesn't reach the screen edge.
            var blocker = go.AddComponent<Image>();
            blocker.color = Color.black;
            blocker.raycastTarget = true;

            // ── Teleport-art background ───────────────────────────────────
            // Outer container clips with RectMask2D; inner Image uses
            // AspectRatioFitter.EnvelopeParent so the source aspect is
            // preserved and the canvas is fully covered (any overflow is
            // cropped, never stretched). The actual sprite is assigned per
            // ShowMapsLoadingOverlay via TeleportMapBackgroundProvider so
            // each slot transition rotates through the authored art.
            var bgContainer = CreateUI("Bg_Container", go.transform);
            var bgContRt    = bgContainer.GetComponent<RectTransform>();
            bgContRt.anchorMin = Vector2.zero; bgContRt.anchorMax = Vector2.one;
            bgContRt.offsetMin = Vector2.zero; bgContRt.offsetMax = Vector2.zero;
            bgContainer.AddComponent<RectMask2D>();

            var bgGo  = CreateUI("Bg_Image", bgContainer.transform);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.preserveAspect = true;
            bgImg.raycastTarget  = false;
            var bgRt  = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin        = new Vector2(0.5f, 0.5f);
            bgRt.anchorMax        = new Vector2(0.5f, 0.5f);
            bgRt.pivot            = new Vector2(0.5f, 0.5f);
            bgRt.anchoredPosition = Vector2.zero;
            var fitter = bgGo.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            // Default 16:9 so the layout doesn't NaN before the first sprite
            // is assigned; ApplyMapsLoadingBackground overwrites this with
            // the actual sprite's aspect on every Show call.
            fitter.aspectRatio = 16f / 9f;
            refs.MapsLoadingBgImage  = bgImg;
            refs.MapsLoadingBgFitter = fitter;

            // Map name banner (above the bar). Bold + accent colour so the
            // user always sees which slot is loading.
            var labelGo = CreateUI("Label", go.transform);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0.5f, 0.5f);
            labelRt.anchorMax = new Vector2(0.5f, 0.5f);
            labelRt.pivot     = new Vector2(0.5f, 0.5f);
            labelRt.anchoredPosition = new Vector2(0f, 80f);
            labelRt.sizeDelta = new Vector2(640f, 48f);

            refs.MapsLoadingLabel = labelGo.AddComponent<TextMeshProUGUI>();
            refs.MapsLoadingLabel.text      = "Loading map…";
            refs.MapsLoadingLabel.fontSize  = 26f;
            refs.MapsLoadingLabel.fontStyle = FontStyles.Bold;
            refs.MapsLoadingLabel.color     = ACCENT;
            refs.MapsLoadingLabel.alignment = TextAlignmentOptions.Center;

            // Progress bar — reuses the boot loading screen's chrome via
            // the shared UIKit widget so both surfaces look identical.
            refs.MapsLoadingBar = Valkur.UIKit.LoadingBarWidget.Mount(
                go.transform, anchoredPos: new Vector2(0f, -10f), barWidth: 720f);

            refs.MapsLoadingOverlay = go;
            go.transform.SetAsLastSibling();
            go.SetActive(false);
        }

        // ── Scroll view ──────────────────────────────────────────────────────────

        private static GameObject MakeMapsScrollView(Transform parent, out RectTransform content)
        {
            var root = CreateUI("MapsListScroll", parent);
            var rootImg = root.AddComponent<Image>();
            rootImg.color = new Color(0.06f, 0.06f, 0.08f, 1f);

            var viewport = CreateUI("Viewport", root.transform);
            var vpRt     = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = new Vector2(-MAPS_LIST_SCROLLBAR_W, 0f);
            viewport.AddComponent<RectMask2D>();

            var contentGo = CreateUI("Content", viewport.transform);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin        = new Vector2(0f, 1f);
            contentRt.anchorMax        = new Vector2(1f, 1f);
            contentRt.pivot            = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta        = Vector2.zero;
            content = contentRt;

            var v = contentGo.AddComponent<VerticalLayoutGroup>();
            v.padding                = new RectOffset(4, 4, 4, 4);
            v.spacing                = 2f;
            v.childControlWidth      = true;
            // childControlHeight MUST be true so the LayoutElement.preferredHeight
            // we set on each row (24f in RebuildMapsList) is actually applied —
            // otherwise rows fall back to their default rect height (~60px) and
            // the list looks oversized vs. the rest of the editor chrome.
            v.childControlHeight     = true;
            v.childForceExpandWidth  = true;
            v.childForceExpandHeight = false;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var scroll = root.AddComponent<ScrollRect>();
            scroll.viewport          = vpRt;
            scroll.content           = contentRt;
            scroll.horizontal        = false;
            scroll.vertical          = true;
            scroll.scrollSensitivity = 24f;
            scroll.movementType      = ScrollRect.MovementType.Clamped;

            return root;
        }
    }
}
