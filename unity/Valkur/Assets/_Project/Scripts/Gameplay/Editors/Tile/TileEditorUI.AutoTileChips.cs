using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Populates the Tiles picker with one chip per unique terrain registered in
    /// the project's <see cref="TerrainCatalog"/>. Used when the
    /// <see cref="TileEditorState.Tool.AutoTileRegion"/> tool is active — clicking
    /// a chip stamps that terrain into <c>TileEditorManager.SelectedTerrain</c>
    /// so the next region drag paints with that terrain.
    /// </summary>
    public partial class TileEditorUI
    {
        private void PopulateTerrainChips()
        {
            foreach (var slot in _tileSlots) if (slot != null) Destroy(slot);
            _tileSlots.Clear();
            _selectedSlotIndex = -1;

            var catalog = TerrainCatalogLoader.Load();
            int chipCount = 0;
            if (catalog != null)
            {
                foreach (var terrain in catalog.GetUniqueTerrains())
                {
                    BuildTerrainChip(terrain);
                    chipCount++;
                }
            }

            if (_refs.TileCountText != null)
                _refs.TileCountText.text = chipCount > 0
                    ? $"{chipCount} terrain(s) available"
                    : "No TerrainCatalog (Resources/TerrainCatalog.asset).";
        }

        private void BuildTerrainChip(string terrain)
        {
            var go = TileEditorUIHelpers.CreateUI($"Terrain_{terrain}", _refs.TileGridContent);
            var img = go.AddComponent<Image>();
            img.color = SLOT_BG;
            var btn = go.AddComponent<Button>();
            var bc = btn.colors;
            bc.normalColor = SLOT_BG;
            bc.highlightedColor = SLOT_HOVER;
            bc.pressedColor = SLOT_SELECTED;
            btn.colors = bc;
            btn.targetGraphic = img;

            var cap = terrain;
            btn.onClick.AddListener(() =>
            {
                if (TileEditorManager.HasInstance)
                    TileEditorManager.Instance.SelectTerrain(cap);
            });

            // Compact label centred in the cell — terrain IDs are short ("grass", "sand").
            var lblGo = TileEditorUIHelpers.CreateUI("Lbl", go.transform);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = lblRt.offsetMax = Vector2.zero;
            var tmp = lblGo.AddComponent<TextMeshProUGUI>();
            tmp.text = terrain;
            tmp.fontSize = 11f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = TEXT_PRIMARY;
            tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;

            _tileSlots.Add(go);
        }
    }
}
