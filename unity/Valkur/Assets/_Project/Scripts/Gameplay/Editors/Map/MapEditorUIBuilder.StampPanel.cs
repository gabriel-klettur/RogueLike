using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Stamp panel for the Map Editor — auto-discovers tilesheet manifests
    /// produced by <c>tools/atlas/migrate_tilesheet.py</c>
    /// (<c>Resources/Tiles/&lt;cat&gt;/_manifest.json</c>) and lets the user
    /// paint the entire stamp at the cursor, on a chosen tilemap layer.
    /// Pattern mirrors <see cref="BuildBiomesPanel"/>: floating dropdown panel
    /// activated from a menu-bar button.
    /// </summary>
    public static partial class MapEditorUIBuilder
    {
        public partial struct UIRefs
        {
            // Menu bar entry
            public Image           StampMenuBtnImg;
            public TextMeshProUGUI StampMenuBtnTmp;

            // Panel + dropdown chrome
            public GameObject      StampDropdown;
            public DraggablePanel  StampPanelDrag;

            // Active layer label (driven by the buttons below).
            public TextMeshProUGUI StampLayerLabel;

            // Mutable state — kept on a class so cycle/place button click
            // handlers share the selection (UIRefs is a struct).
            public StampPanelState StampState;
        }

        public class StampPanelState
        {
            public TilemapLayerSetup.TilemapLayer SelectedLayer = TilemapLayerSetup.TilemapLayer.Ground;
            public string SelectedManifestResourcePath; // e.g. "Tiles/castle_pandora/_manifest"
        }

        public struct StampDescriptor
        {
            public string ManifestResourcePath; // for Resources.Load<TextAsset>
            public string CategoryFolder;       // e.g. "castle_pandora"
            public string DisplayLabel;         // e.g. "secret_of_mana_pandora_castle_exterior (24x28)"
        }

        private const float STAMP_PANEL_W = 280f;
        private const float STAMP_PANEL_H = 380f + PANEL_HDR_H;

        /// <summary>
        /// Called by <see cref="BuildAll"/> when the host wires the panel.
        /// </summary>
        private static void BuildStampPanel(Transform canvasT, ref UIRefs refs,
            Func<List<StampDescriptor>> discoverStamps,
            Action<string, TilemapLayerSetup.TilemapLayer> onPlaceStamp,
            Action onCancelStamp)
        {
            // Stack to the right of Biomes (which sits at x = ZONES + ACTIONS).
            float x = PANEL_GAP + 280f /*ZONES_W*/ + PANEL_GAP + 230f /*ACTIONS_W*/ + PANEL_GAP
                      + 280f /*BIOMES_PANEL_W*/ + PANEL_GAP;

            refs.StampDropdown = MakeDrop("StampPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                STAMP_PANEL_W, STAMP_PANEL_H, "STAMP",
                out var t, out refs.StampPanelDrag);

            var state = new StampPanelState();
            refs.StampState = state;

            BuildSectionLabel(t, "Target layer");
            var layerRow = MakeRow("StampLayerRow", t, 30f);
            refs.StampLayerLabel = BuildStampLayerCycle(layerRow.transform, state);

            BuildSeparator(t);

            BuildSectionLabel(t, "Available stamps");

            var listScroll = MakeScrollView("StampList", t, out var listContent, 220f);
            var listLE = listScroll.AddComponent<LayoutElement>();
            listLE.flexibleHeight = 1f;
            listLE.minHeight = 160f;

            // Materialise the buttons now. The list is short (one entry per
            // tilesheet currently in Resources/Tiles) so eager construction is
            // simpler than a refresh-on-open hook.
            var stamps = discoverStamps?.Invoke() ?? new List<StampDescriptor>();
            if (stamps.Count == 0)
            {
                BuildEmptyStampHint(listContent);
            }
            else
            {
                foreach (var s in stamps)
                    BuildStampEntry(listContent, s, state, onPlaceStamp);
            }

            BuildSeparator(t);
            AddActionBtn(t, "Cancel placement", BTN_H, () => onCancelStamp?.Invoke());

            refs.StampDropdown.SetActive(false);
        }

        private static TextMeshProUGUI BuildStampLayerCycle(Transform parent, StampPanelState state)
        {
            // Cycle through TilemapLayer enum values. Cleaner than a 9-button row
            // for a panel whose primary purpose is "paint the stamp".
            var layers = (TilemapLayerSetup.TilemapLayer[])Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer));

            var prevBtn = AddActionBtn(parent, "<", 26f, null);
            prevBtn.GetComponent<LayoutElement>().preferredWidth = 32f;

            var labelGo = CreateUI("StampLayerLbl", parent);
            labelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text       = state.SelectedLayer.ToString();
            label.fontSize   = 12f;
            label.fontStyle  = FontStyles.Bold;
            label.color      = ACCENT;
            label.alignment  = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            var nextBtn = AddActionBtn(parent, ">", 26f, null);
            nextBtn.GetComponent<LayoutElement>().preferredWidth = 32f;

            void Cycle(int delta)
            {
                int idx = Array.IndexOf(layers, state.SelectedLayer);
                if (idx < 0) idx = 0;
                idx = (idx + delta + layers.Length) % layers.Length;
                state.SelectedLayer = layers[idx];
                label.text = state.SelectedLayer.ToString();
            }

            prevBtn.onClick.AddListener(() => Cycle(-1));
            nextBtn.onClick.AddListener(() => Cycle(+1));
            return label;
        }

        private static void BuildStampEntry(RectTransform parent, StampDescriptor desc,
            StampPanelState state, Action<string, TilemapLayerSetup.TilemapLayer> onPlaceStamp)
        {
            var row = MakeRow($"Stamp_{desc.CategoryFolder}", parent, 36f);

            var labelGo = CreateUI("Lbl", row.transform);
            labelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var lbl = labelGo.AddComponent<TextMeshProUGUI>();
            lbl.text       = desc.DisplayLabel;
            lbl.fontSize   = 11f;
            lbl.color      = TEXT_PRIMARY;
            lbl.alignment  = TextAlignmentOptions.MidlineLeft;
            lbl.raycastTarget = false;

            var captured = desc;
            var placeBtn = AddActionBtn(row.transform, "Place", 28f,
                () => onPlaceStamp?.Invoke(captured.ManifestResourcePath, state.SelectedLayer));
            placeBtn.GetComponent<LayoutElement>().preferredWidth = 70f;
        }

        private static void BuildEmptyStampHint(RectTransform parent)
        {
            var hintGo = CreateUI("EmptyStampHint", parent);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 60f;
            var tmp = hintGo.AddComponent<TextMeshProUGUI>();
            tmp.text =
                "No tilesheet manifests found.\n\n" +
                "Slice a tilesheet PNG with\n" +
                "tools/atlas/migrate_tilesheet.py\n" +
                "and refresh Unity to register it.";
            tmp.fontSize           = 10f;
            tmp.color              = TEXT_MUTED;
            tmp.alignment          = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;
        }
    }
}
