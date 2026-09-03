using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data.Biomes;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Biome-generator panel for the Map Editor — picks a <see cref="BiomeKind"/>
    /// (or random per zone), chooses the target scope (all zones / selected
    /// only) and a seed, then runs the manager's biome generator.
    /// </summary>
    public static partial class MapEditorUIBuilder
    {
        public partial struct UIRefs
        {
            // Menu bar
            public Image           BiomesMenuBtnImg;
            public TextMeshProUGUI BiomesMenuBtnTmp;

            // Floating panel
            public GameObject      BiomesDropdown;
            public DraggablePanel  BiomesPanelDrag;

            // Mode toggles + seed
            public Toggle          BiomeRandomToggle;
            public Toggle          BiomeSelectedZoneOnlyToggle;
            public TMP_InputField  BiomeSeedInput;

            // Mutable state — needs to be a class so click handlers share it
            // (UIRefs is a struct, so capturing struct fields by value would
            // give every button its own private copy of "selected biome").
            public BiomeDialogState BiomeState;
        }

        public class BiomeDialogState
        {
            public BiomeKind SelectedBiome = BiomeKind.Forest;
            public BiomeKind[] Kinds;
            public Outline[] Outlines;
            public TextMeshProUGUI SelectedLabel;
        }

        private const float BIOMES_PANEL_W = 280f;
        private const float BIOMES_PANEL_H = 470f + PANEL_HDR_H;

        private static void BuildBiomesPanel(Transform canvasT, ref UIRefs refs,
            Action<BiomeDialogResult> onConfirmGenerate)
        {
            float x = PANEL_GAP + 280f /*ZONES_W*/ + PANEL_GAP + 230f /*ACTIONS_W*/ + PANEL_GAP;

            refs.BiomesDropdown = MakeDrop("BiomesPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                BIOMES_PANEL_W, BIOMES_PANEL_H, "BIOMES",
                out var t, out refs.BiomesPanelDrag);

            var state = new BiomeDialogState();
            refs.BiomeState = state;

            BuildSectionLabel(t, "Biome");

            var pickGo = CreateUI("SelectedBiome", t);
            pickGo.AddComponent<LayoutElement>().preferredHeight = 18f;
            state.SelectedLabel = pickGo.AddComponent<TextMeshProUGUI>();
            state.SelectedLabel.text       = "Forest";
            state.SelectedLabel.fontSize   = 12f;
            state.SelectedLabel.fontStyle  = FontStyles.Bold;
            state.SelectedLabel.color      = ACCENT;
            state.SelectedLabel.alignment  = TextAlignmentOptions.Left;

            BuildBiomeButtonGrid(t, state);

            BuildSeparator(t);
            BuildSectionLabel(t, "Mode");

            refs.BiomeRandomToggle           = BuildLabeledToggle(t, "Random per zone");
            refs.BiomeSelectedZoneOnlyToggle = BuildLabeledToggle(t, "Selected zone only");

            BuildSeparator(t);
            BuildSectionLabel(t, "Seed");

            var seedHost = CreateUI("SeedHost", t);
            seedHost.AddComponent<LayoutElement>().preferredHeight = 30f;
            refs.BiomeSeedInput = MakeTmpInput(seedHost, "12345");
            refs.BiomeSeedInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            refs.BiomeSeedInput.text = "12345";

            BuildSeparator(t);

            // Capture references that the Generate handler needs. None of these
            // are ref-only struct fields, so plain locals are safe.
            var randomToggle   = refs.BiomeRandomToggle;
            var selOnlyToggle  = refs.BiomeSelectedZoneOnlyToggle;
            var seedInput      = refs.BiomeSeedInput;

            AddActionBtn(t, "Generate", BTN_H, () =>
            {
                int seed = 0;
                if (seedInput != null && !int.TryParse(seedInput.text, out seed))
                    seed = 0;
                onConfirmGenerate?.Invoke(new BiomeDialogResult
                {
                    biome            = state.SelectedBiome,
                    randomPerZone    = randomToggle != null    && randomToggle.isOn,
                    selectedZoneOnly = selOnlyToggle != null   && selOnlyToggle.isOn,
                    seed             = seed,
                });
            });

            refs.BiomesDropdown.SetActive(false);
        }

        private static void BuildBiomeButtonGrid(Transform parent, BiomeDialogState state)
        {
            var biomes = (BiomeKind[])Enum.GetValues(typeof(BiomeKind));
            state.Kinds    = new BiomeKind[biomes.Length];
            state.Outlines = new Outline[biomes.Length];

            for (int i = 0; i < biomes.Length; i++)
            {
                int captureIndex = i;
                var kind         = biomes[i];
                state.Kinds[i]   = kind;

                var btn = AddActionBtn(parent, kind.ToString(), 26f, null);
                var ol  = btn.gameObject.AddComponent<Outline>();
                ol.effectColor    = Color.clear;
                ol.effectDistance = new Vector2(2f, 2f);
                state.Outlines[i] = ol;

                btn.onClick.AddListener(() =>
                {
                    state.SelectedBiome = state.Kinds[captureIndex];
                    if (state.SelectedLabel != null)
                        state.SelectedLabel.text = state.SelectedBiome.ToString();
                    HighlightBiomeButton(state.Outlines, captureIndex);
                });
            }

            int forestIdx = Array.IndexOf(state.Kinds, BiomeKind.Forest);
            if (forestIdx < 0) forestIdx = 0;
            HighlightBiomeButton(state.Outlines, forestIdx);
        }

        private static void HighlightBiomeButton(Outline[] outlines, int index)
        {
            if (outlines == null) return;
            for (int i = 0; i < outlines.Length; i++)
            {
                if (outlines[i] == null) continue;
                outlines[i].effectColor = (i == index)
                    ? new Color(1f, 0.85f, 0.0f, 1f)
                    : new Color(0f, 0f,    0f,   0f);
            }
        }

        private static Toggle BuildLabeledToggle(Transform parent, string label)
        {
            var row   = MakeRow($"ToggleRow_{label}", parent, 28f);
            var lblGo = CreateUI("Lbl", row.transform);
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text      = label;
            lbl.fontSize  = 11f;
            lbl.color     = TEXT_PRIMARY;
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            return MakeToggle(row.transform);
        }
    }
}
