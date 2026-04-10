using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        // ── Per-class selection border colors (Python: class_border_colors) ──
        private static readonly Dictionary<string, Color> ClassBorderColors =
            new Dictionary<string, Color>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "barbarian", new Color(220f / 255f, 50f / 255f, 50f / 255f) },
                { "elven",     new Color(50f / 255f, 200f / 255f, 90f / 255f) },
                { "mague",     new Color(255f / 255f, 220f / 255f, 90f / 255f) },
                { "valkyrie",  new Color(255f / 255f, 105f / 255f, 180f / 255f) },
                { "dwarf",     new Color(70f / 255f, 120f / 255f, 255f / 255f) },
            };

        // Per-class portrait image Resource paths
        private static readonly Dictionary<string, string> ClassPortraitPaths =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "barbarian", "UI/CharacterSelection/character_selection_barbrian" },
                { "elven",     "UI/CharacterSelection/character_selection_elve" },
                { "mague",     "UI/CharacterSelection/character_selection_mague" },
                { "valkyrie",  "UI/CharacterSelection/character_selection_valkyrie" },
                { "dwarf",     "UI/CharacterSelection/character_selection_drwaft" },
            };

        // UI colors matching Python ClassSelectorManager
        private static readonly Color CellBorderUnselected = new Color(95f / 255f, 95f / 255f, 95f / 255f);
        private static readonly Color CellBackground       = new Color(62f / 255f, 62f / 255f, 62f / 255f);
        private static readonly Color CellTitleColor       = new Color(240f / 255f, 240f / 255f, 240f / 255f);
        private static readonly Color CellStatsColor       = new Color(200f / 255f, 200f / 255f, 200f / 255f);

        private void BuildClassSelectorPanel(Transform canvasTransform)
        {
            // ── Full-screen overlay container ────────────────────────────────
            _classSelectionPanel = CreateUIObject("ClassSelectionOverlay", canvasTransform);
            StretchFull(_classSelectionPanel);

            // 1. Tavern background (Python: scale_mode="cover")
            var tavernContainer = CreateUIObject("TavernBgContainer", _classSelectionPanel.transform);
            StretchFull(tavernContainer);
            tavernContainer.AddComponent<RectMask2D>();

            var tavernGo = CreateUIObject("TavernBg", tavernContainer.transform);
            StretchFull(tavernGo);
            var tavernImg = tavernGo.AddComponent<Image>();
            tavernImg.preserveAspect = true;
            tavernImg.raycastTarget = false;
            var tavernTex = Resources.Load<Texture2D>("UI/CharacterSelection/taberna");
            if (tavernTex != null)
            {
                tavernImg.sprite = MakeSprite(tavernTex);
                tavernImg.color = Color.white;
                // EnvelopeParent = CSS "object-fit: cover"
                var arf = tavernGo.AddComponent<AspectRatioFitter>();
                arf.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                arf.aspectRatio = (float)tavernTex.width / tavernTex.height;
            }

            // 2. Semi-transparent dark overlay (Python: (0,0,0,128))
            var dimGo = CreateUIObject("DimOverlay", _classSelectionPanel.transform);
            StretchFull(dimGo);
            var dimImg = dimGo.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 128f / 255f);
            dimImg.raycastTarget = false;

            // 3. Header portrait area (contain-scaled, changes per selected class)
            var headerGo = CreateUIObject("HeaderPortrait", _classSelectionPanel.transform);
            var headerRect = headerGo.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0.1f, 0.30f);
            headerRect.anchorMax = new Vector2(0.9f, 0.98f);
            headerRect.sizeDelta = Vector2.zero;
            headerRect.anchoredPosition = Vector2.zero;
            _classHeaderPortrait = headerGo.AddComponent<Image>();
            _classHeaderPortrait.preserveAspect = true;
            _classHeaderPortrait.raycastTarget = false;
            _classHeaderPortrait.color = Color.clear; // updated by UpdateClassSelectionUI

            // 4. Cards container (bottom portion of screen)
            var cardsContainerGo = CreateUIObject("CardsContainer", _classSelectionPanel.transform);
            var containerRect = cardsContainerGo.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.02f, 0.03f);
            containerRect.anchorMax = new Vector2(0.98f, 0.30f);
            containerRect.sizeDelta = Vector2.zero;
            containerRect.anchoredPosition = Vector2.zero;

            // Panel drop shadow (Python: (0,0,0,100) offset Y+6)
            var shadowGo = CreateUIObject("PanelShadow", cardsContainerGo.transform);
            var shadowRect = shadowGo.GetComponent<RectTransform>();
            shadowRect.anchorMin = Vector2.zero;
            shadowRect.anchorMax = Vector2.one;
            shadowRect.sizeDelta = Vector2.zero;
            shadowRect.anchoredPosition = new Vector2(0f, -6f);
            var shadowImg = shadowGo.AddComponent<Image>();
            shadowImg.color = new Color(0f, 0f, 0f, 100f / 255f);
            shadowImg.raycastTarget = false;

            // Panel background (Python: (44,44,44,235))
            var panelBgGo = CreateUIObject("PanelBg", cardsContainerGo.transform);
            StretchFull(panelBgGo);
            var panelBgImg = panelBgGo.AddComponent<Image>();
            panelBgImg.color = new Color(44f / 255f, 44f / 255f, 44f / 255f, 235f / 255f);
            panelBgImg.raycastTarget = false;

            // Cards row with horizontal layout (Python: columns=5, cell_h_margin=16)
            var rowGo = CreateUIObject("CardsRow", cardsContainerGo.transform);
            var rowRect = rowGo.GetComponent<RectTransform>();
            rowRect.anchorMin = Vector2.zero;
            rowRect.anchorMax = Vector2.one;
            rowRect.offsetMin = new Vector2(16f, 16f);
            rowRect.offsetMax = new Vector2(-16f, -16f);
            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 16f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;

            // ── Build class cards ────────────────────────────────────────────
            _classButtons.Clear();
            _classMarkerTexts.Clear();
            _classKeys.Clear();
            _classCardBorderImages.Clear();
            _classCardBgRects.Clear();

            var presets = PlayerClassCatalog.AllPresets;
            for (int i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                var key = preset.PlayerKey;

                // Card root = border image (gap between this and inner bg = border width)
                var cardGo = CreateUIObject($"Class_{key}", rowGo.transform);
                cardGo.AddComponent<LayoutElement>();
                var borderImg = cardGo.AddComponent<Image>();
                borderImg.color = CellBorderUnselected;
                _classCardBorderImages.Add(borderImg);

                // Click + hover
                var btn = cardGo.AddComponent<Button>();
                btn.targetGraphic = borderImg;
                btn.transition = Selectable.Transition.None;
                int captured = i;
                btn.onClick.AddListener(() => OnClassCardClicked(captured));

                var trigger = cardGo.AddComponent<EventTrigger>();
                var hoverEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                hoverEntry.callback.AddListener(_ => SetSelectedClassIndex(captured));
                trigger.triggers.Add(hoverEntry);

                // Card inner background (Python: (62,62,62), border_radius=10)
                var bgGo = CreateUIObject("CardBg", cardGo.transform);
                var bgRect = bgGo.GetComponent<RectTransform>();
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                bgRect.offsetMin = new Vector2(2f, 2f);   // 2px border when unselected
                bgRect.offsetMax = new Vector2(-2f, -2f);
                var bgImg = bgGo.AddComponent<Image>();
                bgImg.color = CellBackground;
                bgImg.raycastTarget = false;
                _classCardBgRects.Add(bgRect);

                // Class name (Python: font_size=36, top-left, bold, lowercase)
                var nameGo = CreateUIObject("Name", bgGo.transform);
                var nameRect = nameGo.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0f, 1f);
                nameRect.anchorMax = new Vector2(1f, 1f);
                nameRect.pivot = new Vector2(0f, 1f);
                nameRect.anchoredPosition = new Vector2(10f, -8f);
                nameRect.sizeDelta = new Vector2(-20f, 36f);
                var nameTMP = nameGo.AddComponent<TextMeshProUGUI>();
                nameTMP.text = key;
                nameTMP.fontSize = 28f;
                nameTMP.alignment = TextAlignmentOptions.TopLeft;
                nameTMP.color = CellTitleColor;
                nameTMP.fontStyle = FontStyles.Bold;
                nameTMP.raycastTarget = false;

                // Stats block (Python: small_font=20, lines: HP,ATK,ARM,SPD,MANA,ENG)
                var statsGo = CreateUIObject("Stats", bgGo.transform);
                var statsRect = statsGo.GetComponent<RectTransform>();
                statsRect.anchorMin = Vector2.zero;
                statsRect.anchorMax = Vector2.one;
                statsRect.offsetMin = new Vector2(10f, 8f);
                statsRect.offsetMax = new Vector2(-10f, -48f);
                var statsTMP = statsGo.AddComponent<TextMeshProUGUI>();
                statsTMP.text = FormatClassStats(preset);
                statsTMP.fontSize = 18f;
                statsTMP.alignment = TextAlignmentOptions.TopLeft;
                statsTMP.color = CellStatsColor;
                statsTMP.raycastTarget = false;

                _classButtons.Add(btn);
                _classKeys.Add(key);
            }

            // Hint text at bottom
            var hintGo = CreateUIObject("SelectorHint", _classSelectionPanel.transform);
            var hintR = hintGo.GetComponent<RectTransform>();
            hintR.anchorMin = new Vector2(0.5f, 0f);
            hintR.anchorMax = new Vector2(0.5f, 0f);
            hintR.pivot = new Vector2(0.5f, 0f);
            hintR.anchoredPosition = new Vector2(0f, 4f);
            hintR.sizeDelta = new Vector2(900f, 24f);
            var hintTMP = hintGo.AddComponent<TextMeshProUGUI>();
            hintTMP.text = "Click selecciona y empieza  |  A/D \u2190 \u2192 elegir  |  Enter confirmar  |  Esc volver";
            hintTMP.fontSize = 16f;
            hintTMP.alignment = TextAlignmentOptions.Center;
            hintTMP.color = VersionCol;
            hintTMP.raycastTarget = false;

            _classSelectionPanel.SetActive(false);
            _selectedClassIndex = FindSelectedClassIndex();
            UpdateClassSelectionUI();
        }

        private static string FormatClassStats(PlayerClassCatalog.PlayerClassPreset preset)
        {
            return $"HP: {preset.MaxStrength}\n" +
                   $"ATK: {preset.BasicAttack}\n" +
                   $"ARM: {preset.BasicArmor}\n" +
                   $"SPD: {preset.BasicSpeed:0.#}\n" +
                   $"MANA: {preset.MaxIntelligence}\n" +
                   $"ENG: {preset.MaxDexterity}";
        }

        private Sprite GetCachedPortraitSprite(string playerKey)
        {
            if (_portraitSpriteCache.TryGetValue(playerKey, out var cached))
                return cached;
            if (!ClassPortraitPaths.TryGetValue(playerKey, out var path))
                return null;
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null) return null;
            var sprite = MakeSprite(tex);
            _portraitSpriteCache[playerKey] = sprite;
            return sprite;
        }
    }
}
