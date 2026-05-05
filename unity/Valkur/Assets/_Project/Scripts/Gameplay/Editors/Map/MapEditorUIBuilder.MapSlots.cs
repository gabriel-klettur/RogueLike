using System;
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
            public TMP_InputField  MapsNameInput;

            // Modal dialogs
            public GameObject      MapsDeleteDialog;
            public TextMeshProUGUI MapsDeletePrompt;
            public GameObject      MapsNewDialog;
            public TMP_InputField  MapsNewNameInput;

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
            public Action<string>          OnSaveAs;
            public Action<string>          OnLoad;
            public Action<string>          OnDelete;
            public Action<string, string>  OnRename;
            public Action<string>          OnNew;
            public Func<string[]>          ListSlots;
            public Func<string>            GetActive;
        }

        public class MapSlotsDialogState
        {
            public string SelectedSlot;
            public string DeleteTargetSlot;
        }

        // ── Sizes ────────────────────────────────────────────────────────────────

        private const float MAPS_PANEL_W      = 280f;
        private const float MAPS_PANEL_H      = 470f + PANEL_HDR_H;
        private const float MAPS_LIST_MIN_H   = 200f;
        private const float MAPS_LIST_SCROLLBAR_W = 12f;
        private const float MAPS_DELETE_DIALOG_W  = 430f;
        private const float MAPS_DELETE_DIALOG_H  = 130f + PANEL_HDR_H;
        private const float MAPS_NEW_DIALOG_W     = 430f;
        private const float MAPS_NEW_DIALOG_H     = 200f + PANEL_HDR_H;

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
            BuildSectionLabel(t, "Map name");

            var nameHost = CreateUI("MapsNameHost", t);
            nameHost.AddComponent<LayoutElement>().preferredHeight = 30f;
            refs.MapsNameInput = MakeTmpInput(nameHost, "new_map_name");

            BuildSeparator(t);

            var nameInput = refs.MapsNameInput;
            AddActionBtn(t, "Save As", BTN_H, () =>
            {
                callbacks.OnSaveAs?.Invoke(nameInput != null ? nameInput.text : string.Empty);
            });
            AddActionBtn(t, "Load",    BTN_H, () =>
            {
                callbacks.OnLoad?.Invoke(state.SelectedSlot);
            });
            AddActionBtn(t, "Rename",  BTN_H, () =>
            {
                callbacks.OnRename?.Invoke(state.SelectedSlot,
                    nameInput != null ? nameInput.text : string.Empty);
            });

            // Delete + New flows go through small confirm dialogs.
            BuildMapsDeleteDialog(canvasT, ref refs, callbacks);
            var localDeleteDialog = refs.MapsDeleteDialog;
            var localDeletePrompt = refs.MapsDeletePrompt;
            AddActionBtn(t, "Delete", BTN_H, () =>
            {
                if (string.IsNullOrEmpty(state.SelectedSlot)) return;
                state.DeleteTargetSlot = state.SelectedSlot;
                bool isDefault = string.Equals(state.SelectedSlot,
                    MapEditorMapSlots.DEFAULT_SLOT, StringComparison.OrdinalIgnoreCase);
                if (localDeletePrompt != null)
                    localDeletePrompt.text = isDefault
                        ? "The 'default' map is the implicit baseline and cannot be deleted."
                        : $"Delete map '{state.SelectedSlot}'?";
                if (localDeleteDialog != null)
                    localDeleteDialog.SetActive(true);
            }, danger: true);

            BuildMapsNewDialog(canvasT, ref refs, callbacks);
            var localNewDialog    = refs.MapsNewDialog;
            var localNewNameInput = refs.MapsNewNameInput;
            AddActionBtn(t, "New (clear)", BTN_H, () =>
            {
                if (localNewNameInput != null) localNewNameInput.text = "";
                if (localNewDialog != null) localNewDialog.SetActive(true);
            });

            refs.MapsDropdown.SetActive(false);
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
            promptGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            refs.MapsDeletePrompt = promptGo.AddComponent<TextMeshProUGUI>();
            refs.MapsDeletePrompt.text      = "Delete map?";
            refs.MapsDeletePrompt.fontSize  = 13f;
            refs.MapsDeletePrompt.color     = TEXT_PRIMARY;
            refs.MapsDeletePrompt.alignment = TextAlignmentOptions.Left;

            BuildSeparator(t);

            var localGo    = go;
            var localState = refs.MapsState;
            var btnRow = MakeRow("MapsDelDialogBtns", t, BTN_H);
            AddActionBtn(btnRow.transform, "Delete", BTN_H, () =>
            {
                bool isDefault = localState != null
                    && string.Equals(localState.DeleteTargetSlot,
                        MapEditorMapSlots.DEFAULT_SLOT, StringComparison.OrdinalIgnoreCase);
                if (!isDefault && localState != null
                    && !string.IsNullOrEmpty(localState.DeleteTargetSlot))
                    callbacks.OnDelete?.Invoke(localState.DeleteTargetSlot);
                if (localState != null) localState.DeleteTargetSlot = null;
                localGo.SetActive(false);
            }, danger: true);
            AddActionBtn(btnRow.transform, "Cancel", BTN_H, () => { localGo.SetActive(false); });

            go.SetActive(false);
        }

        private static void BuildMapsNewDialog(Transform canvasT, ref UIRefs refs,
            MapSlotCallbacks callbacks)
        {
            var go = MakeDrop("MapsNewDialog", canvasT,
                PanelDock.TopLeft,
                /*x*/ 80f, /*y*/ PANEL_TOP_OFFSET + 80f,
                MAPS_NEW_DIALOG_W, MAPS_NEW_DIALOG_H,
                "NEW MAP (CLEAR)",
                out var t, out _);
            refs.MapsNewDialog = go;

            var promptGo = CreateUI("Prompt", t);
            promptGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var prompt = promptGo.AddComponent<TextMeshProUGUI>();
            prompt.text      = "Discard current zones and start a blank map?";
            prompt.fontSize  = 12f;
            prompt.color     = TEXT_PRIMARY;
            prompt.alignment = TextAlignmentOptions.Left;
            prompt.enableWordWrapping = true;

            BuildSeparator(t);
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

        // ── Slot list rendering ──────────────────────────────────────────────────

        /// <summary>
        /// Rebuild the saved-maps list. Highlights the active slot in accent
        /// colour. Selecting a row stores the slot in <paramref name="state"/>
        /// and pre-fills the name input via <paramref name="nameInput"/>.
        /// </summary>
        public static void RebuildMapsList(UIRefs refs, string[] slots, string activeSlot,
            Action<string> onRowClicked)
        {
            if (refs.MapsListContent == null) return;

            for (int i = refs.MapsListContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(refs.MapsListContent.GetChild(i).gameObject);

            if (refs.MapsActiveLabel != null)
                refs.MapsActiveLabel.text = string.IsNullOrEmpty(activeSlot)
                    ? "—" : "📁 " + activeSlot;

            if (slots == null || slots.Length == 0)
            {
                var emptyGo = CreateUI("EmptyHint", refs.MapsListContent);
                emptyGo.AddComponent<LayoutElement>().preferredHeight = 22f;
                var t = emptyGo.AddComponent<TextMeshProUGUI>();
                t.text      = "(no saved maps yet)";
                t.fontSize  = 11f;
                t.color     = TEXT_SECONDARY;
                t.alignment = TextAlignmentOptions.Center;
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                string slot = slots[i];
                bool isActive = string.Equals(slot, activeSlot, StringComparison.OrdinalIgnoreCase);
                var btn = AddActionBtn(refs.MapsListContent, slot, 26f, () =>
                {
                    onRowClicked?.Invoke(slot);
                });
                if (isActive)
                {
                    var img = btn.GetComponent<Image>();
                    if (img != null) img.color = new Color(0.30f, 0.25f, 0.06f, 1f);
                    var label = btn.GetComponentInChildren<TextMeshProUGUI>();
                    if (label != null) label.color = ACCENT;
                }
            }
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
            v.spacing                = 3f;
            v.childControlWidth      = true;
            v.childControlHeight     = false;
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
