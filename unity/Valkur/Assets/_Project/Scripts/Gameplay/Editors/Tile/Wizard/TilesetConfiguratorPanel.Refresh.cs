using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TilesetConfiguratorPanel
    {
        private readonly List<GameObject> _slotCells = new List<GameObject>();
        private readonly List<GameObject> _spriteCells = new List<GameObject>();

        // =====================================================================
        // SLOT LIST (left pane)
        // =====================================================================

        private void RefreshSlots()
        {
            foreach (var cell in _slotCells) if (cell != null) Destroy(cell);
            _slotCells.Clear();
            _slotPreviews.Clear();

            for (int i = 0; i < 16; i++)
                _slotCells.Add(BuildSlotCell((Blob16Slot)i));
        }

        private GameObject BuildSlotCell(Blob16Slot slot)
        {
            var cell = CreateUI($"Slot_{slot}", _slotsContent);
            cell.AddComponent<LayoutElement>().preferredHeight = SLOT_CELL_H;

            var bg = cell.AddComponent<Image>();
            bg.color = SLOT_BG;
            cell.AddComponent<TilesetSlotDropTarget>().Bind(this, slot);

            var hl = cell.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8f;
            hl.padding = new RectOffset(6, 6, 6, 6);
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            hl.childControlWidth = true;
            hl.childControlHeight = true;

            // Bitmask diagram (a tiny 32×32 visual showing which neighbors are connected)
            BuildSlotDiagram(cell.transform, slot);

            // Slot name + bitmask string
            var labelGo = CreateUI("Label", cell.transform);
            labelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var vl = labelGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 0f;
            vl.childForceExpandHeight = false;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childControlWidth = true;

            var nameGo = CreateUI("Name", labelGo.transform);
            nameGo.AddComponent<LayoutElement>().preferredHeight = 18f;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text = slot.ToString();
            nameTmp.fontSize = 12f;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.color = TEXT_PRIMARY;

            var maskGo = CreateUI("Mask", labelGo.transform);
            maskGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var maskTmp = maskGo.AddComponent<TextMeshProUGUI>();
            maskTmp.text = $"mask 0b{System.Convert.ToString((byte)slot, 2).PadLeft(4, '0')}  (NESW)";
            maskTmp.fontSize = 9f;
            maskTmp.color = TEXT_MUTED;

            // Current sprite preview
            var previewGo = CreateUI("Preview", cell.transform);
            previewGo.AddComponent<LayoutElement>().preferredWidth = SLOT_CELL_H - 12f;
            var preview = previewGo.AddComponent<Image>();
            preview.color = new Color(1f, 1f, 1f, 0.05f);
            preview.preserveAspect = true;
            preview.raycastTarget = false;
            _slotPreviews[slot] = preview;
            if (_assignments.TryGetValue(slot, out var s) && s != null)
            {
                preview.sprite = s;
                preview.color = Color.white;
            }

            // Clear button
            var clearGo = CreateUI("Clear", cell.transform);
            clearGo.AddComponent<LayoutElement>().preferredWidth = 28f;
            MakeBtn(clearGo, "X", () => ClearSlot(slot), 11f);

            return cell;
        }

        /// <summary>
        /// Renders a 32×32 mini-icon showing which cardinal directions are
        /// connected for this slot. Same-terrain neighbors render as filled dots,
        /// other-terrain neighbors as faint dots.
        /// </summary>
        private static void BuildSlotDiagram(Transform parent, Blob16Slot slot)
        {
            var go = CreateUI("Diag", parent);
            go.AddComponent<LayoutElement>().preferredWidth = 36f;
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.25f);

            byte mask = (byte)slot;
            AddDot(go.transform, new Vector2(0.5f, 0.5f), Color.white);                                    // center
            AddDot(go.transform, new Vector2(0.5f, 0.85f), DotColor((mask & 1) != 0));                     // N
            AddDot(go.transform, new Vector2(0.85f, 0.5f), DotColor((mask & 2) != 0));                     // E
            AddDot(go.transform, new Vector2(0.5f, 0.15f), DotColor((mask & 4) != 0));                     // S
            AddDot(go.transform, new Vector2(0.15f, 0.5f), DotColor((mask & 8) != 0));                     // W
        }

        private static Color DotColor(bool connected)
        {
            return connected
                ? new Color(0.30f, 0.90f, 0.45f, 1f)
                : new Color(0.50f, 0.50f, 0.55f, 0.4f);
        }

        private static void AddDot(Transform parent, Vector2 anchor, Color color)
        {
            var go = CreateUI("Dot", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(7f, 7f);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        // =====================================================================
        // SPRITE LIST (right pane)
        // =====================================================================

        private void RefreshSpriteList()
        {
            foreach (var cell in _spriteCells) if (cell != null) Destroy(cell);
            _spriteCells.Clear();

            for (int i = 0; i < _allSprites.Count; i++)
                _spriteCells.Add(BuildSpriteCell(_allSprites[i]));
        }

        private GameObject BuildSpriteCell(Sprite sprite)
        {
            var cell = CreateUI($"S_{sprite.name}", _spritesContent);
            var bg = cell.AddComponent<Image>();
            bg.color = SLOT_BG;
            cell.AddComponent<TilesetSpriteDragger>().Bind(this, sprite);

            var vl = cell.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 1f;
            vl.padding = new RectOffset(2, 2, 2, 2);
            vl.childForceExpandWidth = true;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandHeight = false;

            var imgGo = CreateUI("Img", cell.transform);
            imgGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var img = imgGo.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            bool isHidden = _hidden.Contains(sprite);
            img.color = isHidden ? new Color(1f, 1f, 1f, 0.30f) : Color.white;
            img.raycastTarget = false;

            // Footer row inside the sprite cell: hide toggle + assigned-marker text.
            var footRow = CreateUI("Foot", cell.transform);
            footRow.AddComponent<LayoutElement>().preferredHeight = 16f;
            var fhl = footRow.AddComponent<HorizontalLayoutGroup>();
            fhl.spacing = 2f;
            fhl.childForceExpandWidth = false;
            fhl.childControlWidth = true;
            fhl.childControlHeight = true;
            fhl.childForceExpandHeight = true;

            var hideGo = CreateUI("Hide", footRow.transform);
            hideGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            MakeBtn(hideGo, isHidden ? "shown" : "hide", () => ToggleSpriteHidden(sprite), 9f);

            // Assigned-to-slot indicator (shows which slot, if any).
            var slot = FindAssignedSlot(sprite);
            if (slot.HasValue)
            {
                var tagGo = CreateUI("Tag", cell.transform);
                tagGo.AddComponent<LayoutElement>().preferredHeight = 11f;
                var tag = tagGo.AddComponent<TextMeshProUGUI>();
                tag.text = $"= {slot.Value}";
                tag.fontSize = 8f;
                tag.alignment = TextAlignmentOptions.Center;
                tag.color = ACCENT;
            }
            return cell;
        }

        private Blob16Slot? FindAssignedSlot(Sprite sprite)
        {
            foreach (var kv in _assignments)
                if (kv.Value == sprite) return kv.Key;
            return null;
        }
    }
}
