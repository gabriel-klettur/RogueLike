using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TilesetConfiguratorPanel
    {
        private const float PANEL_W = 1100f;
        private const float PANEL_H = 700f;
        private const float HEADER_H = 32f;
        private const float FOOTER_H = 38f;
        private const float SLOT_CELL_H = 64f;
        private const float SPRITE_CELL_SIZE = 56f;
        private const int SPRITE_GRID_COLS = 6;

        // =====================================================================
        // ONE-TIME UI BUILD
        // =====================================================================

        private void EnsureBuilt()
        {
            if (_root != null) return;

            // Root = full-screen backdrop that catches clicks behind the modal.
            _root = CreateUI("TilesetConfigurator", transform);
            var rrt = _root.GetComponent<RectTransform>();
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;
            var backdrop = _root.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.55f);
            backdrop.raycastTarget = true;

            // Modal card — anchored to center, fixed size, vertical layout.
            var card = CreateUI("Card", _root.transform);
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PANEL_W, PANEL_H);
            rt.anchoredPosition = Vector2.zero;
            var bg = card.AddComponent<Image>();
            bg.color = BG_PANEL;
            var outline = card.AddComponent<Outline>();
            outline.effectColor = ACCENT;
            outline.effectDistance = new Vector2(1.5f, 1.5f);

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 6);
            vlg.spacing = 6f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            BuildHeader(card.transform);
            BuildBody(card.transform);
            BuildFooter(card.transform);
        }

        private void BuildHeader(Transform parent)
        {
            var row = CreateUI("Header", parent);
            row.AddComponent<LayoutElement>().preferredHeight = HEADER_H;
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(8, 0, 0, 0);
            hl.spacing = 8f;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            hl.childControlWidth = true;
            hl.childControlHeight = true;

            var titleGo = CreateUI("Title", row.transform);
            titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _titleText = titleGo.AddComponent<TextMeshProUGUI>();
            _titleText.fontSize = 16f;
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.alignment = TextAlignmentOptions.Left;
            _titleText.color = ACCENT;
            _titleText.text = "Configure Tileset";

            var closeGo = CreateUI("Close", row.transform);
            closeGo.AddComponent<LayoutElement>().preferredWidth = HEADER_H;
            MakeBtn(closeGo, "X", Close, 14f);
        }

        private void BuildBody(Transform parent)
        {
            var row = CreateUI("Body", parent);
            row.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8f;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            hl.childControlWidth = true;
            hl.childControlHeight = true;

            // Left pane: 16 slots
            var slotsPane = CreateUI("SlotsPane", row.transform);
            slotsPane.AddComponent<LayoutElement>().preferredWidth = 460f;
            BuildSlotsScroll(slotsPane.transform);

            // Right pane: sprite grid
            var spritesPane = CreateUI("SpritesPane", row.transform);
            spritesPane.AddComponent<LayoutElement>().flexibleWidth = 1f;
            BuildSpritesScroll(spritesPane.transform);
        }

        private void BuildSlotsScroll(Transform parent)
        {
            BuildSectionHeader(parent, "BLOB16 SLOTS", 11f);
            var (content, _) = BuildScrollPane(parent.gameObject, "SlotsScroll");
            _slotsContent = content;

            var vl = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 4f;
            vl.padding = new RectOffset(6, 6, 6, 6);
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
        }

        private void BuildSpritesScroll(Transform parent)
        {
            BuildSectionHeader(parent, "FOLDER SPRITES", 11f);
            var (content, _) = BuildScrollPane(parent.gameObject, "SpritesScroll");
            _spritesContent = content;

            var gl = content.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(SPRITE_CELL_SIZE, SPRITE_CELL_SIZE + 18f);
            gl.spacing = new Vector2(4f, 4f);
            gl.padding = new RectOffset(6, 6, 6, 6);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = SPRITE_GRID_COLS;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
        }

        private static (RectTransform content, ScrollRect sr) BuildScrollPane(GameObject parent, string name)
        {
            var scrollGo = CreateUI(name, parent.transform);
            scrollGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var bg = scrollGo.AddComponent<Image>();
            bg.color = BG_SURFACE;
            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 24f;
            sr.movementType = ScrollRect.MovementType.Clamped;

            var vp = CreateUI("VP", scrollGo.transform);
            var vpRt = vp.GetComponent<RectTransform>();
            vpRt.anchorMin = new Vector2(0f, 0f);
            vpRt.anchorMax = new Vector2(1f, 1f);
            vpRt.pivot = new Vector2(0f, 1f);
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = Vector2.zero;
            vp.AddComponent<RectMask2D>();

            var contentGo = CreateUI("Content", vp.transform);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);
            contentRt.sizeDelta = Vector2.zero;

            sr.content = contentRt;
            sr.viewport = vpRt;
            return (contentRt, sr);
        }

        private void BuildFooter(Transform parent)
        {
            var row = CreateUI("Footer", parent);
            row.AddComponent<LayoutElement>().preferredHeight = FOOTER_H;
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8f;
            hl.padding = new RectOffset(6, 6, 4, 4);
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            hl.childControlWidth = true;
            hl.childControlHeight = true;

            var statusGo = CreateUI("Status", row.transform);
            statusGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _statusText = statusGo.AddComponent<TextMeshProUGUI>();
            _statusText.fontSize = 11f;
            _statusText.alignment = TextAlignmentOptions.MidlineLeft;
            _statusText.color = TEXT_SECONDARY;
            _statusText.text = "";

            var saveGo = CreateUI("Save", row.transform);
            var saveLe = saveGo.AddComponent<LayoutElement>();
            saveLe.preferredWidth = 110f;
            MakeBtn(saveGo, "SAVE", Save, 13f);
        }
    }
}
