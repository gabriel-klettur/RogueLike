using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Builds all UI panels for the tile editor: left toolbar/picker, right sidebar, bottom indicator.
    /// Extracted from TileEditorUI to isolate construction from runtime state management.
    /// </summary>
    public static partial class TileEditorUIBuilder
    {
        /// <summary>
        /// Holds references to all UI elements created during BuildUI, passed back to TileEditorUI.
        /// </summary>
        public struct UIRefs
        {
            // Left panel
            public GameObject LeftPanel;
            public Dictionary<TileEditorState.Tool, Image> ToolButtonImages;
            public Dictionary<TileEditorState.Tool, TextMeshProUGUI> ToolButtonTexts;
            public TextMeshProUGUI LayerLabel;
            public TextMeshProUGUI BrushSizeLabel;
            public Image SelectedTilePreviewImg;
            public TextMeshProUGUI SelectedTileNameText;
            public Transform CategoryTabsContent;
            public Transform TileGridContent;
            public ScrollRect TileScrollRect;
            public TextMeshProUGUI TileCountText;
            public TextMeshProUGUI StatusText;

            // Right: View panel
            public GameObject ViewPanel;
            public Image ViewHoveredImg;
            public TextMeshProUGUI ViewHoveredLabel;
            public Image ViewSelectedImg;
            public TextMeshProUGUI ViewSelectedLabel;
            public Image ViewChoiceImg;
            public TextMeshProUGUI ViewChoiceLabel;
            public TextMeshProUGUI ViewLayerHoveredText;
            public TextMeshProUGUI ViewLayerSelectedText;

            // Right: Layers panel
            public GameObject LayersPanel;
            public List<Image> LayerRowBgs;
            public List<TextMeshProUGUI> LayerRowLabels;
            public List<Image> LayerVisIcons;

            // Bottom
            public GameObject LayerIndicatorPanel;
            public TextMeshProUGUI LayerIndicator;
        }

        public static UIRefs BuildAll(Transform canvasT, TileEditorState state,
            System.Action<TileEditorState.Tool> onToolChanged,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged,
            System.Action<int> onBrushSizeChanged)
        {
            var refs = new UIRefs
            {
                ToolButtonImages = new Dictionary<TileEditorState.Tool, Image>(),
                ToolButtonTexts = new Dictionary<TileEditorState.Tool, TextMeshProUGUI>(),
                LayerRowBgs = new List<Image>(),
                LayerRowLabels = new List<TextMeshProUGUI>(),
                LayerVisIcons = new List<Image>()
            };

            BuildLeftPanel(canvasT, state, ref refs, onToolChanged, onLayerChanged, onBrushSizeChanged);
            BuildRightSidebar(canvasT, state, ref refs, onLayerChanged);
            BuildLayerIndicator(canvasT, state, ref refs);

            return refs;
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  LEFT PANEL
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    }
}
