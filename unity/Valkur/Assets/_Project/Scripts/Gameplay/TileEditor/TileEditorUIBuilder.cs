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
        }

        public static UIRefs BuildAll(Transform canvasT, TileEditorState state,
            System.Action<TileEditorState.Tool> onToolChanged,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged,
            System.Action<int> onBrushSizeChanged,
            System.Action<string> onDropdownToggle)
        {
            var refs = new UIRefs
            {
                ToolButtonImages = new Dictionary<TileEditorState.Tool, Image>(),
                ToolButtonTexts = new Dictionary<TileEditorState.Tool, TextMeshProUGUI>(),
                LayerRowBgs = new List<Image>(),
                LayerRowLabels = new List<TextMeshProUGUI>(),
                LayerVisIcons = new List<Image>()
            };

            BuildMenuBar(canvasT, state, ref refs, onLayerChanged, onBrushSizeChanged, onDropdownToggle);
            BuildToolsDropdown(canvasT, state, ref refs, onToolChanged, onBrushSizeChanged);
            BuildTilesDropdown(canvasT, ref refs);
            BuildLayersDropdown(canvasT, state, ref refs, onLayerChanged);
            BuildInspectorDropdown(canvasT, state, ref refs);
            BuildLayerIndicator(canvasT, state, ref refs);

            return refs;
        }
    }
}
