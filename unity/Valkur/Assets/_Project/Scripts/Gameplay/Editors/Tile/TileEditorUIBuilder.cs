using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Builds all UI panels for the tile editor: slim menu bar, dropdown panels, bottom indicator.
    /// Layout: thin 30px menu bar at top + togglable dropdown panels (Tools, Tiles, Layers, Inspector).
    /// </summary>
    public static partial class TileEditorUIBuilder
    {
        /// <summary>
        /// Holds references to all UI elements created during BuildUI, passed back to TileEditorUI.
        /// </summary>
        public struct UIRefs
        {
            // Menu bar
            public GameObject MenuBar;

            // Dropdown panels
            public GameObject ToolsDropdown;
            public GameObject TilesDropdown;
            public GameObject LayersDropdown;
            public GameObject InspectorDropdown;
            public GameObject CollidersDropdown;
            public GameObject SizeDropdown;
            public GameObject ViewDropdown;
            public GameObject SelectModesDropdown;

            // Tool buttons (inside ToolsDropdown)
            public Dictionary<TileEditorState.Tool, Image> ToolButtonImages;
            public Dictionary<TileEditorState.Tool, TextMeshProUGUI> ToolButtonTexts;

            // Menu bar controls
            public TextMeshProUGUI LayerLabel;
            public TextMeshProUGUI BrushSizeLabel;
            public TextMeshProUGUI StatusText;

            // Tile picker (inside TilesDropdown)
            public Image SelectedTilePreviewImg;
            public TextMeshProUGUI SelectedTileNameText;
            public Transform CategoryTabsContent;
            public Transform TileGridContent;
            public ScrollRect TileScrollRect;
            public TextMeshProUGUI TileCountText;

            // Inspector (inside InspectorDropdown)
            public Image ViewHoveredImg;
            public TextMeshProUGUI ViewHoveredLabel;
            public Image ViewSelectedImg;
            public TextMeshProUGUI ViewSelectedLabel;
            public Image ViewChoiceImg;
            public TextMeshProUGUI ViewChoiceLabel;
            public TextMeshProUGUI ViewLayerHoveredText;
            public TextMeshProUGUI ViewLayerSelectedText;

            // Layers (inside LayersDropdown)
            public List<Image> LayerRowBgs;
            public List<TextMeshProUGUI> LayerRowLabels;
            public List<Image> LayerVisIcons;

            // Bottom indicator
            public GameObject LayerIndicatorPanel;
            public TextMeshProUGUI LayerIndicator;

            // Menu bar button images (for active highlight when dropdown is open)
            public Image ToolsMenuBtnImg;
            public Image TilesMenuBtnImg;
            public Image LayersMenuBtnImg;
            public Image InspectorMenuBtnImg;
            public Image CollidersMenuBtnImg;
            public Image SizeMenuBtnImg;
            public Image ViewMenuBtnImg;
            public TextMeshProUGUI ToolsMenuBtnTmp;
            public TextMeshProUGUI TilesMenuBtnTmp;
            public TextMeshProUGUI LayersMenuBtnTmp;
            public TextMeshProUGUI InspectorMenuBtnTmp;
            public TextMeshProUGUI CollidersMenuBtnTmp;
            public TextMeshProUGUI SizeMenuBtnTmp;
            public TextMeshProUGUI ViewMenuBtnTmp;

            // Colliders panel — visualize toggle, draw toggle, erase toggle.
            public Image ShowCollidersToggleImg;
            public TextMeshProUGUI ShowCollidersToggleLabel;
            public Image DrawCollidersToggleImg;
            public TextMeshProUGUI DrawCollidersToggleLabel;
            public Image EraseCollidersToggleImg;
            public TextMeshProUGUI EraseCollidersToggleLabel;

            // Size panel — slider (1..25, integer steps).
            public Slider BrushSizeSlider;

            // (Save button + dirty indicator removed — auto-save covers this.)

            // Perf Probe toggle button (menu bar far-right)
            public Image PerfProbeMenuBtnImg;
            public TextMeshProUGUI PerfProbeMenuBtnTmp;

            // DraggablePanel components — wired by TileEditorUI.Builder for close callbacks
            public DraggablePanel ToolsPanelDrag;
            public DraggablePanel TilesPanelDrag;
            public DraggablePanel LayersPanelDrag;
            public DraggablePanel InspectorPanelDrag;
            public DraggablePanel CollidersPanelDrag;
            public DraggablePanel SizePanelDrag;
            public DraggablePanel ViewPanelDrag;
            public DraggablePanel SelectModesPanelDrag;

            // SelectModes panel — three radio rows + clipboard action buttons.
            public Image ModeSingleToggleImg;
            public TextMeshProUGUI ModeSingleToggleLabel;
            public Image ModeRectToggleImg;
            public TextMeshProUGUI ModeRectToggleLabel;
            public Image ModeMultiToggleImg;
            public TextMeshProUGUI ModeMultiToggleLabel;
            public Button CopyButton;
            public Image  CopyButtonImg;
            public Button CutButton;
            public Image  CutButtonImg;
            public Button PasteButton;
            public Image  PasteButtonImg;
            public Button ClearSelectionButton;
            public Image  ClearSelectionButtonImg;

            // View panel — three toggle rows mirroring the Colliders panel UI/UX.
            public Image ShowGridLinesToggleImg;
            public TextMeshProUGUI ShowGridLinesToggleLabel;
            public Image ShowZoneGridToggleImg;
            public TextMeshProUGUI ShowZoneGridToggleLabel;
            public Image ViewShowCollidersToggleImg;
            public TextMeshProUGUI ViewShowCollidersToggleLabel;

            // UX / Theme panel
            public GameObject       UxDropdown;
            public DraggablePanel   UxPanelDrag;
            public Image            UxMenuBtnImg;
            public TextMeshProUGUI  UxMenuBtnTmp;

            // Panels visibility toggle button (menu bar, left of UX)
            public Image            PanelsToggleBtnImg;
            public TextMeshProUGUI  PanelsToggleBtnTmp;

            // Tileset Configurator wizard — button inside Tiles panel that opens the
            // Blob16 slot-mapping wizard for the currently-selected category.
            public Button            ConfigureTilesetBtn;
            public TextMeshProUGUI   ConfigureTilesetBtnLabel;

            // Tileset View controls — only visible when the active category has a
            // `_manifest.json` (i.e. came from a sliced tilesheet via
            // tools/atlas/migrate_tilesheet.py). Lets the user zoom in/out and
            // hide duplicate cells while keeping the original sheet layout.
            public GameObject        TilesetControlsRow;
            public Slider            TilesetZoomSlider;
            public TextMeshProUGUI   TilesetZoomLabel;
            public Image             TilesetDedupToggleImg;
            public TextMeshProUGUI   TilesetDedupToggleLabel;

            // Top row of the Tiles panel (SELECTED + RULESET on the left,
            // CATEGORIES on the right). Tracked so ApplyTilesPanelResizePolicy
            // can mark it as horizontally flexible — letting the CATEGORIES
            // list reflow into more columns when the panel widens.
            public GameObject        TilesTopRow;
        }

        public static UIRefs BuildAll(Transform canvasT, TileEditorState state,
            System.Action<TileEditorState.Tool> onToolChanged,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged,
            System.Action<int> onBrushSizeChanged,
            System.Action<string> onDropdownToggle,
            System.Action onUndo = null,
            System.Action onRedo = null,
            System.Action onShowColliders = null,
            System.Action onDrawColliders = null,
            System.Action onEraseColliders = null,
            System.Action onPerfToggle = null,
            System.Action onAllPanelsToggle = null,
            System.Action onShowGridLines = null,
            System.Action onShowZoneGrid = null,
            System.Action<TileEditorState.SelectMode> onSelectModeChanged = null,
            System.Action onCopyClicked = null,
            System.Action onCutClicked = null,
            System.Action onPasteClicked = null,
            System.Action onClearSelectionClicked = null)
        {
            var refs = new UIRefs
            {
                ToolButtonImages = new Dictionary<TileEditorState.Tool, Image>(),
                ToolButtonTexts = new Dictionary<TileEditorState.Tool, TextMeshProUGUI>(),
                LayerRowBgs = new List<Image>(),
                LayerRowLabels = new List<TextMeshProUGUI>(),
                LayerVisIcons = new List<Image>()
            };

            BuildMenuBar(canvasT, state, ref refs, onBrushSizeChanged, onDropdownToggle, onPerfToggle, onAllPanelsToggle);
            BuildToolsDropdown(canvasT, state, ref refs, onToolChanged, onBrushSizeChanged, onUndo, onRedo);
            BuildTilesDropdown(canvasT, ref refs);
            BuildLayersDropdown(canvasT, state, ref refs, onLayerChanged);
            BuildInspectorDropdown(canvasT, state, ref refs);
            BuildCollidersDropdown(canvasT, state, ref refs,
                onShowColliders, onDrawColliders, onEraseColliders);
            BuildSizeDropdown(canvasT, state, ref refs, onBrushSizeChanged);
            BuildViewDropdown(canvasT, state, ref refs,
                onShowGridLines, onShowZoneGrid, onShowColliders);
            BuildSelectModesDropdown(canvasT, state, ref refs,
                onSelectModeChanged, onCopyClicked, onCutClicked, onPasteClicked, onClearSelectionClicked);
            BuildUxDropdown(canvasT, ref refs);
            BuildLayerIndicator(canvasT, state, ref refs, onLayerChanged);

            return refs;
        }
    }
}
