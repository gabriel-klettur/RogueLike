using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    public partial class HUDManager : SingletonMonoBehaviour<HUDManager>
    {
        // ── Portrait (left side of the unified player HUD) ───────────────
        // Captures the player's first SpriteRenderer.sprite at HUD-build time
        // so the picture stays still (would otherwise flicker through animation
        // frames). A small "Lvl N" badge sits in the top-right corner.
        private const float PortraitFramePadding = 4f;
        private const float PortraitLevelBadgeWidth  = 48f;
        private const float PortraitLevelBadgeHeight = 18f;

        private void CreatePortrait(Transform parent, Health playerHealth)
        {
            var portrait = CreateUIObject("Portrait", parent);

            var le = portrait.AddComponent<LayoutElement>();
            le.preferredWidth  = HudPortraitSize;
            le.preferredHeight = HudPortraitSize;
            le.flexibleWidth   = 0f;
            le.flexibleHeight  = 0f;

            // Outer frame.
            var frame = portrait.AddComponent<Image>();
            frame.color = new Color(0.08f, 0.08f, 0.10f, 0.95f);

            // Inner picture area.
            var picGo = CreateUIObject("Picture", portrait.transform);
            var picRt = picGo.GetComponent<RectTransform>();
            picRt.anchorMin = Vector2.zero;
            picRt.anchorMax = Vector2.one;
            picRt.offsetMin = new Vector2(PortraitFramePadding, PortraitFramePadding);
            picRt.offsetMax = new Vector2(-PortraitFramePadding, -PortraitFramePadding);

            var pic = picGo.AddComponent<Image>();
            pic.preserveAspect = true;
            pic.color = Color.white;
            pic.sprite = ResolvePlayerPortraitSprite(playerHealth);
            // No sprite available → fall back to a soft-colored placeholder so
            // the slot still reads as "where the character face goes".
            if (pic.sprite == null)
                pic.color = new Color(0.30f, 0.34f, 0.42f, 1f);

            // Level badge — top-right of the portrait, white text on a dark
            // pill so the number stays readable over any portrait sprite.
            var badgeGo = CreateUIObject("LevelBadge", portrait.transform);
            var badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(1f, 1f);
            badgeRt.anchorMax = new Vector2(1f, 1f);
            badgeRt.pivot     = new Vector2(1f, 1f);
            badgeRt.sizeDelta = new Vector2(PortraitLevelBadgeWidth, PortraitLevelBadgeHeight);
            badgeRt.anchoredPosition = new Vector2(-2f, -2f);
            var badgeBg = badgeGo.AddComponent<Image>();
            badgeBg.color = new Color(0.05f, 0.06f, 0.10f, 0.85f);
            badgeBg.raycastTarget = false;

            var badgeTextGo = CreateUIObject("Text", badgeGo.transform);
            var badgeTextRt = badgeTextGo.GetComponent<RectTransform>();
            badgeTextRt.anchorMin = Vector2.zero;
            badgeTextRt.anchorMax = Vector2.one;
            badgeTextRt.offsetMin = Vector2.zero;
            badgeTextRt.offsetMax = Vector2.zero;
            var badgeText = badgeTextGo.AddComponent<TextMeshProUGUI>();
            badgeText.fontSize = 12f;
            badgeText.fontStyle = FontStyles.Bold;
            badgeText.alignment = TextAlignmentOptions.Center;
            badgeText.color = Color.white;
            badgeText.raycastTarget = false;
            badgeText.text = "Lvl 1";

            var levelDriver = badgeGo.AddComponent<LevelLabelHUD>();
            levelDriver.Bind(playerHealth, badgeText);
        }

        private static Sprite ResolvePlayerPortraitSprite(Health playerHealth)
        {
            if (playerHealth == null) return null;
            var sr = playerHealth.GetComponentInChildren<SpriteRenderer>();
            return sr != null ? sr.sprite : null;
        }

        // ── Overlay bar (HP / MP) ────────────────────────────────────────
        // A thin horizontal bar with the value text centered on top. Replaces
        // the older "label-on-the-left + bar" row layout for the unified HUD.
        private GameObject CreateOverlayBar(Transform parent, string name, float height,
            Color fillColor, out Image fill, out Image bg, out TextMeshProUGUI text)
        {
            var row = CreateUIObject(name, parent);
            var le  = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth   = 1f;

            // Background.
            bg = row.AddComponent<Image>();
            bg.sprite = GetWhitePixelSprite();
            bg.type   = Image.Type.Sliced;
            bg.color  = new Color(0.12f, 0.12f, 0.14f, 0.85f);
            bg.raycastTarget = false;

            // Fill (anchored stretch so it fills the row when fillAmount = 1).
            var fillGo = CreateUIObject("Fill", row.transform);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            fill = fillGo.AddComponent<Image>();
            fill.sprite = GetWhitePixelSprite();
            fill.type   = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.color  = fillColor;
            fill.raycastTarget = false;

            // Value text (centered overlay) lives on its own GameObject — TMP
            // and Image must not share a GameObject (NRE in MaskableGraphic).
            var textGo = CreateUIObject("Text", row.transform);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 13f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 1f, 1f, 0.95f);
            text.raycastTarget = false;
            text.text = "0/0";

            return row;
        }

        // ── Ability row (3 slots) ────────────────────────────────────────
        // Compact action-bar variant inline in the player HUD. Distinct from
        // the full WoW-style bar (Valkur.Gameplay.UI.SpellBarHUD) which is
        // hidden by default and toggled from the HUD tray.
        private const int AbilityRowSlotCount = 3;
        private const float AbilityRowSlotSize = 36f;
        private const float AbilityRowSlotGap  = 4f;
        private static readonly string[] AbilityRowKeyLabels = { "1", "2", "3" };

        private void CreateAbilityRow(Transform parent, GameObject playerGo)
        {
            var row = CreateUIObject("AbilityRow", parent);
            var le  = row.AddComponent<LayoutElement>();
            le.preferredHeight = HudAbilityRowHeight;

            // Invisible hit area covering the whole row (slots AND the gaps
            // between them) so the double-click that opens the character sheet
            // lands anywhere on the row, not just on a slot.
            var hit = row.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            row.AddComponent<AbilityRowDoubleClick>();

            var hLayout = row.AddComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(0, 0, 0, 0);
            hLayout.spacing = AbilityRowSlotGap;
            hLayout.childForceExpandWidth  = false;
            hLayout.childForceExpandHeight = false;
            hLayout.childControlWidth      = true;
            hLayout.childControlHeight     = true;
            hLayout.childAlignment         = TextAnchor.MiddleLeft;

            var slotIcons    = new Image[AbilityRowSlotCount];
            var slotRings    = new CooldownRing[AbilityRowSlotCount];

            for (int i = 0; i < AbilityRowSlotCount; i++)
            {
                BuildAbilitySlot(row.transform, i, out slotIcons[i], out slotRings[i]);
            }

            var driver = row.AddComponent<PlayerAbilityRowHUD>();
            driver.Bind(playerGo, slotIcons, slotRings);
        }

        private void BuildAbilitySlot(Transform parent, int index, out Image icon, out CooldownRing ring)
        {
            var slot = CreateUIObject($"Slot_{index}", parent);
            var slotLe = slot.AddComponent<LayoutElement>();
            slotLe.preferredWidth  = AbilityRowSlotSize;
            slotLe.preferredHeight = AbilityRowSlotSize;

            var bg = slot.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.10f, 0.13f, 0.95f);

            // Spell icon.
            var iconGo = CreateUIObject("Icon", slot.transform);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = new Vector2(2f, 2f);
            iconRt.offsetMax = new Vector2(-2f, -2f);
            icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.color = new Color(0.35f, 0.35f, 0.40f, 0.5f); // empty look
            icon.raycastTarget = false;

            // Cooldown ring on top.
            ring = CooldownRing.AddToParent(slot.transform);

            // Hotkey label.
            var keyGo = CreateUIObject("Key", slot.transform);
            var keyRt = keyGo.GetComponent<RectTransform>();
            keyRt.anchorMin = new Vector2(0f, 0f);
            keyRt.anchorMax = new Vector2(0f, 0f);
            keyRt.pivot     = new Vector2(0f, 0f);
            keyRt.sizeDelta = new Vector2(14f, 14f);
            keyRt.anchoredPosition = new Vector2(2f, 2f);
            var keyText = keyGo.AddComponent<TextMeshProUGUI>();
            keyText.fontSize = 10f;
            keyText.fontStyle = FontStyles.Bold;
            keyText.color = new Color(0.85f, 0.85f, 0.95f, 0.85f);
            keyText.alignment = TextAlignmentOptions.BottomLeft;
            keyText.raycastTarget = false;
            keyText.text = AbilityRowKeyLabels[index];
        }
    }
}
