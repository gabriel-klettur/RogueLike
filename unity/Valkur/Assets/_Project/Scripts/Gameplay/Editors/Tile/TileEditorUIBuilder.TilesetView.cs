using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Partial that builds the "tileset view" controls — zoom slider + hide-duplicates
    /// toggle — that appear in the Tiles panel when the active category came from a
    /// sliced tilesheet (i.e. has a Resources/Tiles/&lt;cat&gt;/_manifest.json). The row is
    /// inserted between the CATEGORIES section and the TILES grid by
    /// <see cref="BuildTilesDropdown"/>; visibility is toggled at runtime by
    /// <see cref="TileEditorUI.PopulateTileGrid"/> based on the category metadata.
    ///
    /// The actual grid rebuild (layout in (r,c) order, zoom-driven cellSize, dedup
    /// filtering) happens in TileEditorUI.Builder.cs — this file only owns the
    /// always-present chrome that drives those callbacks.
    /// </summary>
    public static partial class TileEditorUIBuilder
    {
        public const float TILESET_ZOOM_MIN = 24f;
        public const float TILESET_ZOOM_MAX = 96f;
        public const float TILESET_ZOOM_DEFAULT = 40f;

        /// <summary>
        /// Builds a single horizontal row with: zoom slider on the left + hide-duplicates
        /// toggle button on the right. Called from <see cref="BuildTilesDropdown"/> right
        /// before <c>BuildTilePicker</c>. Initially hidden; <see cref="TileEditorUI"/>
        /// shows it for tilesheet categories.
        /// </summary>
        internal static void BuildTilesetControls(Transform parent, ref UIRefs refs,
            System.Action<float> onZoomChanged,
            System.Action onDedupClicked)
        {
            var row = CreateUI("TilesetControls", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = false;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.padding = new RectOffset(4, 4, 2, 2);

            BuildZoomCell(row.transform, ref refs, onZoomChanged);
            BuildDedupCell(row.transform, ref refs, onDedupClicked);

            refs.TilesetControlsRow = row;
            row.SetActive(false);
        }

        private static void BuildZoomCell(Transform parent, ref UIRefs refs,
            System.Action<float> onZoomChanged)
        {
            var cell = CreateUI("ZoomCell", parent);
            cell.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var h = cell.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            var labelGo = CreateUI("Lbl", cell.transform);
            labelGo.AddComponent<LayoutElement>().preferredWidth = 28f;
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = "ZOOM";
            labelTmp.fontSize = 9f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = TEXT_MUTED;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
            labelTmp.characterSpacing = 1.5f;

            var sliderGo = CreateUI("Slider", cell.transform);
            sliderGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var slider = sliderGo.AddComponent<Slider>();
            slider.minValue = TILESET_ZOOM_MIN;
            slider.maxValue = TILESET_ZOOM_MAX;
            slider.value = TILESET_ZOOM_DEFAULT;
            slider.wholeNumbers = false;

            // Slider visuals: track + fill + handle.
            var bg = CreateUI("BG", sliderGo.transform);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.5f); bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.sizeDelta = new Vector2(0f, 4f);
            bg.AddComponent<Image>().color = SLOT_BG;

            var fillRoot = CreateUI("FillArea", sliderGo.transform);
            var fillRt = fillRoot.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0.5f); fillRt.anchorMax = new Vector2(1f, 0.5f);
            fillRt.sizeDelta = new Vector2(-10f, 4f);
            var fill = CreateUI("Fill", fillRoot.transform);
            var fillRtInner = fill.GetComponent<RectTransform>();
            fillRtInner.anchorMin = Vector2.zero; fillRtInner.anchorMax = Vector2.one;
            fillRtInner.sizeDelta = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = ACCENT_DIM;

            var handleRoot = CreateUI("HandleArea", sliderGo.transform);
            var handleRootRt = handleRoot.GetComponent<RectTransform>();
            handleRootRt.anchorMin = Vector2.zero; handleRootRt.anchorMax = Vector2.one;
            handleRootRt.sizeDelta = new Vector2(-10f, 0f);
            var handle = CreateUI("Handle", handleRoot.transform);
            var handleRt = handle.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(10f, 14f);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = ACCENT;

            slider.fillRect = fillRtInner;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;

            slider.onValueChanged.AddListener(v => onZoomChanged?.Invoke(v));

            var valGo = CreateUI("Val", cell.transform);
            valGo.AddComponent<LayoutElement>().preferredWidth = 32f;
            var valTmp = valGo.AddComponent<TextMeshProUGUI>();
            valTmp.text = $"{(int)TILESET_ZOOM_DEFAULT}";
            valTmp.fontSize = 9f;
            valTmp.color = TEXT_SECONDARY;
            valTmp.alignment = TextAlignmentOptions.MidlineRight;

            refs.TilesetZoomSlider = slider;
            refs.TilesetZoomLabel = valTmp;
        }

        private static void BuildDedupCell(Transform parent, ref UIRefs refs,
            System.Action onDedupClicked)
        {
            var cell = CreateUI("DedupBtn", parent);
            cell.AddComponent<LayoutElement>().preferredWidth = 86f;

            var img = cell.AddComponent<Image>();
            img.color = BTN_NORMAL;
            var btn = cell.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onDedupClicked?.Invoke());

            var labelTmp = AddCenteredText(cell.transform, "HIDE DUPS", 9f, FontStyles.Bold, TEXT_SECONDARY);
            labelTmp.characterSpacing = 1.5f;

            refs.TilesetDedupToggleImg = img;
            refs.TilesetDedupToggleLabel = labelTmp;
        }
    }
}
