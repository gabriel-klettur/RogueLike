using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World;
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
            public TextMeshProUGUI ToolsMenuBtnTmp;
            public TextMeshProUGUI TilesMenuBtnTmp;
            public TextMeshProUGUI LayersMenuBtnTmp;
            public TextMeshProUGUI InspectorMenuBtnTmp;
            public TextMeshProUGUI CollidersMenuBtnTmp;
            public TextMeshProUGUI SizeMenuBtnTmp;

            // Colliders panel — visualize toggle, draw toggle, erase toggle, status hint
            public Image ShowCollidersToggleImg;
            public TextMeshProUGUI ShowCollidersToggleLabel;
            public Image DrawCollidersToggleImg;
            public TextMeshProUGUI DrawCollidersToggleLabel;
            public Image EraseCollidersToggleImg;
            public TextMeshProUGUI EraseCollidersToggleLabel;
            public TextMeshProUGUI CollidersHintText;

            // Size panel — preset buttons (1x1 .. 5x5)
            public List<Image> BrushSizePresetImgs;
            public List<TextMeshProUGUI> BrushSizePresetLabels;

            // Save button + dirty indicator (in Tools panel)
            public Image SaveButtonImg;
            public TextMeshProUGUI SaveButtonLabel;
            public TextMeshProUGUI DirtyIndicatorText;

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

            // UX / Theme panel
            public GameObject       UxDropdown;
            public DraggablePanel   UxPanelDrag;
            public Image            UxMenuBtnImg;
            public TextMeshProUGUI  UxMenuBtnTmp;
        }

        public static UIRefs BuildAll(Transform canvasT, TileEditorState state,
            System.Action<TileEditorState.Tool> onToolChanged,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged,
            System.Action<int> onBrushSizeChanged,
            System.Action<string> onDropdownToggle,
            System.Action onUndo = null,
            System.Action onRedo = null,
            System.Action onSave = null,
            System.Action onShowColliders = null,
            System.Action onDrawColliders = null,
            System.Action onEraseColliders = null,
            System.Action onPerfToggle = null)
        {
            var refs = new UIRefs
            {
                ToolButtonImages = new Dictionary<TileEditorState.Tool, Image>(),
                ToolButtonTexts = new Dictionary<TileEditorState.Tool, TextMeshProUGUI>(),
                LayerRowBgs = new List<Image>(),
                LayerRowLabels = new List<TextMeshProUGUI>(),
                LayerVisIcons = new List<Image>(),
                BrushSizePresetImgs = new List<Image>(),
                BrushSizePresetLabels = new List<TextMeshProUGUI>()
            };

            BuildMenuBar(canvasT, state, ref refs, onBrushSizeChanged, onDropdownToggle, onPerfToggle);
            BuildToolsDropdown(canvasT, state, ref refs, onToolChanged, onBrushSizeChanged, onUndo, onRedo, onSave);
            BuildTilesDropdown(canvasT, ref refs);
            BuildLayersDropdown(canvasT, state, ref refs, onLayerChanged);
            BuildInspectorDropdown(canvasT, state, ref refs);
            BuildCollidersDropdown(canvasT, state, ref refs,
                onShowColliders, onDrawColliders, onEraseColliders);
            BuildSizeDropdown(canvasT, state, ref refs, onBrushSizeChanged);
            BuildUxDropdown(canvasT, ref refs);
            BuildLayerIndicator(canvasT, state, ref refs, onLayerChanged);

            return refs;
        }
    }
}
