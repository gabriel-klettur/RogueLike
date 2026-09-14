using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// The class selector: the screen that decides what the player does for the next several
    /// hours.
    ///
    /// <para><b>What it was.</b> Six flat grey rectangles headed by the raw internal key in
    /// lowercase — <c>barbarian</c>, <c>mague</c>, <c>drwaft</c>'s neighbour <c>dwarf</c> — over
    /// six unlabelled abbreviations (<c>HP ATK ARM SPD MANA ENG</c>) whose values are 1 and 2
    /// for attack and 0 and 5 for armour. No description, no art on the card, and a selection
    /// marker whose COLOUR CHANGED PER CLASS, so the barbarian's selected card wore a 2 px red
    /// border that reads as a validation error and no two rows ever agreed on what "chosen"
    /// looks like.</para>
    ///
    /// <para><b>What changed.</b> Real names from <c>PlayerClassPreset.DisplayName</c> (which
    /// existed and was never read), one written line per class, bars instead of bare integers,
    /// and ONE selection colour for all six. The class's own colour survives as a thin accent on
    /// the card's top edge — it is useful as identity and useless as a state.</para>
    ///
    /// <para><b>The group portrait has five figures and there are six classes.</b> That is the
    /// painting's limit, recorded in <c>ClassPortraitPaths</c>: the five are one re-lit scene and
    /// the vampire is composed against the empty plate. The header shows whichever portrait the
    /// selected class has, so the mismatch is invisible instead of being a card with nobody in
    /// the picture.</para>
    /// </summary>
    public partial class MainMenuUI
    {
        /// <summary>Per-class identity, used as an ACCENT and never as the selection state.</summary>
        [SelfHealingStatic("Immutable table built once from literals. Nothing writes to it after the static initialiser, it holds no Unity object and no subscription, so it cannot carry a destroyed reference or a session decision across Play.")]
        private static readonly Dictionary<string, Color> ClassAccent =
            new Dictionary<string, Color>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "barbarian", new Color(0.86f, 0.28f, 0.24f) },
                { "elven",     new Color(0.32f, 0.78f, 0.42f) },
                { "mague",     new Color(0.62f, 0.48f, 0.95f) },
                { "valkyrie",  new Color(0.98f, 0.52f, 0.72f) },
                { "dwarf",     new Color(0.38f, 0.58f, 0.98f) },
                { "vampire",   new Color(0.84f, 0.22f, 0.36f) },
            };

        private static readonly Dictionary<string, string> ClassPortraitPaths =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "barbarian", "UI/CharacterSelection/character_selection_barbrian" },
                { "elven",     "UI/CharacterSelection/character_selection_elve" },
                { "mague",     "UI/CharacterSelection/character_selection_mague" },
                { "valkyrie",  "UI/CharacterSelection/character_selection_valkyrie" },
                { "dwarf",     "UI/CharacterSelection/character_selection_drwaft" },
                // TEMPORARY. The five above are one painted tavern GROUP, re-lit per class — a
                // sixth character cannot join that scene without it being repainted. This one is
                // composed from the shipped empty plate (taberna.png) with the vampire's own idle
                // frame standing in it, by tools/atlas/wave11/build_vampire_portrait.py. Delete
                // both when the group plate is repainted with six figures.
                { "vampire",   "UI/CharacterSelection/character_selection_vampire" },
            };

        /// <summary>What a bar is drawn against. Per stat, so "fast" and "tough" are comparable.</summary>
        private struct StatBar
        {
            public string Label;
            public float Value;
            public float Max;
        }

        private readonly Dictionary<string, Sprite> _portraitSpriteCache = new Dictionary<string, Sprite>();

        /// <summary>
        /// The subset of that cache this menu BUILT, and therefore the only entries it may free.
        /// Everything else in it is a shipped asset shared with the rest of the game.
        /// </summary>
        private readonly HashSet<Sprite> _ownedPortraitSprites = new HashSet<Sprite>();
        private readonly List<Image> _classCardAccents = new List<Image>();
        private readonly List<Frontend.BevelFrameGraphic> _classCardFrames = new List<Frontend.BevelFrameGraphic>();
        private MenuFxLayer _classMotes;

        /// <summary>The height of a card's name band, which the frame draws as its header.</summary>
        private const float ClassCardHeader = 38f;
        private readonly List<Button> _classButtons = new List<Button>();
        private Image _classHeaderPortrait;
        private TextMeshProUGUI _classDescription;
        private TextMeshProUGUI _classChosenName;

        private void BuildClassSelectorPanel(Transform canvasTransform)
        {
            var style = Style;

            _classSelectionPanel = MenuUIKit.Rect("ClassSelectionOverlay", canvasTransform).gameObject;
            StretchFull(_classSelectionPanel);
            var overlay = _classSelectionPanel.transform;

            // 1. The tavern, cropped to cover, with the same downward bias the carousel uses so
            //    the figures' heads are not the part that gets cut.
            var tavernContainer = MenuUIKit.Stretch("TavernBgContainer", overlay);
            tavernContainer.gameObject.AddComponent<RectMask2D>();

            var tavernRt = MenuUIKit.Rect("TavernBg", tavernContainer);
            tavernRt.anchorMin = tavernRt.anchorMax = new Vector2(0.5f, 0.5f);
            tavernRt.pivot = new Vector2(0.5f, 0.5f);
            var tavernImg = tavernRt.gameObject.AddComponent<Image>();
            tavernImg.raycastTarget = false;
            var tavern = LoadSprite("UI/CharacterSelection/taberna");
            if (tavern != null)
            {
                tavernImg.sprite = tavern;
                tavernImg.color = Color.white;
                var arf = tavernRt.gameObject.AddComponent<AspectRatioFitter>();
                arf.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                var tex = tavern.texture;
                arf.aspectRatio = tex != null ? tex.width / Mathf.Max(1f, tex.height) : 1.5f;
            }
            else tavernImg.color = Color.black;

            var dim = MenuUIKit.Stretch("Dim", overlay);
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.46f);
            dimImg.raycastTarget = false;

            // 2. Header portrait, above the cards.
            var headerRt = MenuUIKit.Rect("HeaderPortrait", overlay);
            headerRt.anchorMin = new Vector2(0.12f, 0.42f);
            headerRt.anchorMax = new Vector2(0.88f, 0.97f);
            headerRt.offsetMin = Vector2.zero;
            headerRt.offsetMax = Vector2.zero;
            _classHeaderPortrait = headerRt.gameObject.AddComponent<Image>();
            _classHeaderPortrait.preserveAspect = true;
            _classHeaderPortrait.raycastTarget = false;
            _classHeaderPortrait.color = Color.clear;

            var titleRt = MenuUIKit.Rect("Title", overlay);
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -18f);
            titleRt.sizeDelta = new Vector2(900f, 40f);
            MenuTypography.Label(titleRt.gameObject, style, MenuText.ClassTitle,
                                 style.titleFontSize, style.Gold, TextAlignmentOptions.Center, bold: true);

            // 3. The chosen class's name and its sentence, between the portrait and the cards —
            //    the one place the eye already is when it moves from the picture to the choice.
            var nameRt = MenuUIKit.Rect("ChosenName", overlay);
            nameRt.anchorMin = new Vector2(0.5f, 0.40f);
            nameRt.anchorMax = new Vector2(0.5f, 0.40f);
            nameRt.pivot = new Vector2(0.5f, 0f);
            nameRt.sizeDelta = new Vector2(960f, 34f);
            _classChosenName = MenuTypography.Label(nameRt.gameObject, style, string.Empty,
                                                    style.titleFontSize, style.Gold,
                                                    TextAlignmentOptions.Center, bold: true);

            var descRt = MenuUIKit.Rect("Description", overlay);
            descRt.anchorMin = new Vector2(0.5f, 0.34f);
            descRt.anchorMax = new Vector2(0.5f, 0.34f);
            descRt.pivot = new Vector2(0.5f, 0f);
            descRt.sizeDelta = new Vector2(980f, 44f);
            _classDescription = MenuTypography.Label(descRt.gameObject, style, string.Empty,
                                                     style.rowFontSize - 4f, style.TextPrimary,
                                                     TextAlignmentOptions.Top);
            _classDescription.enableWordWrapping = true;

            // 4. The cards.
            var row = MenuUIKit.Rect("CardsRow", overlay);
            row.anchorMin = new Vector2(0.02f, 0.05f);
            row.anchorMax = new Vector2(0.98f, 0.32f);
            row.offsetMin = Vector2.zero;
            row.offsetMax = Vector2.zero;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleCenter;

            _classButtons.Clear();
            _classKeys.Clear();
            _classCardAccents.Clear();
            _classCardFrames.Clear();

            var presets = PlayerClassCatalog.AllPresets;
            for (int i = 0; i < presets.Count; i++)
                BuildClassCard(row, presets[i], i);

            var hintRt = MenuUIKit.Rect("SelectorHint", overlay);
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 0f);
            hintRt.pivot = new Vector2(0.5f, 0f);
            hintRt.anchoredPosition = new Vector2(0f, 8f);
            hintRt.sizeDelta = new Vector2(1000f, 24f);
            MenuTypography.Label(hintRt.gameObject, style, MenuText.ClassHint,
                                 style.hintFontSize, style.TextMuted, TextAlignmentOptions.Center);

            // Over the cards, so a chosen card's sparks fly in front of it rather than behind the
            // tavern. The shell's own layer sits under every screen.
            _classMotes = MenuFxLayer.Create(overlay, _art, 96, Frontend.FrontendKit.Get(style).Additive);
            _classMotes.name = "ClassMotes";
            _classMotes.UseSoftMotes = true;

            _classSelectionPanel.SetActive(false);
            _selectedClassIndex = FindSelectedClassIndex();
            UpdateClassSelectionUI();
        }

        private void BuildClassCard(Transform row, PlayerClassCatalog.PlayerClassPreset preset, int index)
        {
            var style = Style;
            string key = preset.PlayerKey;

            // The panel housing at card size: a bevelled frame whose header band holds the name,
            // with a gem where its rule meets each side. UpdateClassSelectionUI lights the chosen.
            var card = Frontend.BevelFrameGraphic.Create(row, $"Class_{key}");
            card.Thickness = 4f;
            card.ShadowScale = 0.8f;
            card.Brackets = false;
            card.Tint = style.Gold;
            card.HeaderHeight = ClassCardHeader - card.Thickness;
            card.raycastTarget = true;
            card.gameObject.AddComponent<LayoutElement>();
            _classCardFrames.Add(card);
            // FOUR lists are index-parallel and all four are filled HERE, in the one method that
            // makes a card. This one was cleared in the builder and never added to, which is not
            // a cosmetic slip: every reader of it opens with
            // `if (_selectedClassIndex >= _classKeys.Count) return;`, so an empty list made the
            // header portrait, the chosen-class name, the accent AND
            // `PlayerSelectionState.SetSelectedPlayer` all unreachable — the screen drew six
            // cards, moved its highlight, and could not record which class the player picked.
            // Nothing failed; the run simply started as whatever class was stored last.
            _classKeys.Add(key);

            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = card;
            btn.transition = Selectable.Transition.None;
            int captured = index;
            btn.onClick.AddListener(() => OnClassCardClicked(captured));
            MenuUIKit.OnHover(card.gameObject, _ => SetSelectedClassIndex(captured));
            _classButtons.Add(btn);

            // The class's own colour, as a 3 px bar along the top. Identity, not state.
            var accent = MenuUIKit.Sprite("Accent", card.transform, _art.White,
                                          ClassAccent.TryGetValue(key, out var c) ? c : style.Gold);
            var art = (RectTransform)accent.transform;
            art.anchorMin = new Vector2(0f, 1f);
            art.anchorMax = new Vector2(1f, 1f);
            art.pivot = new Vector2(0.5f, 1f);
            art.offsetMin = new Vector2(4f, -7f);
            art.offsetMax = new Vector2(-4f, -4f);
            _classCardAccents.Add(accent);

            var nameRt = MenuUIKit.Rect("Name", card.transform);
            nameRt.anchorMin = new Vector2(0f, 1f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.pivot = new Vector2(0.5f, 1f);
            nameRt.anchoredPosition = new Vector2(0f, -12f);
            nameRt.sizeDelta = new Vector2(-16f, 30f);
            MenuTypography.Label(nameRt.gameObject, style,
                                 string.IsNullOrEmpty(preset.DisplayName) ? key : preset.DisplayName,
                                 style.rowFontSize, style.TextPrimary, TextAlignmentOptions.Center,
                                 bold: true);

            // Bars, each against the largest value any class has for that stat, so the six cards
            // are comparable at a glance. Six bare integers were not: "ATK 2" against "ATK 1"
            // tells a player nothing about how much harder that is.
            var bars = StatBarsFor(preset);
            float y = -48f;
            foreach (var bar in bars)
            {
                BuildStatBar(card.transform, bar, y);
                y -= 22f;
            }
        }

        private static List<StatBar> StatBarsFor(PlayerClassCatalog.PlayerClassPreset p)
        {
            // The maxima are read off the catalogue rather than written down, so a retune of any
            // class rescales every bar instead of leaving one card's bar past the end of its
            // track.
            float maxHp = 1f, maxMana = 1f, maxSta = 1f, maxSpd = 1f, maxAtk = 1f, maxArm = 1f;
            foreach (var q in PlayerClassCatalog.AllPresets)
            {
                maxHp = Mathf.Max(maxHp, q.MaxStrength);
                maxMana = Mathf.Max(maxMana, q.MaxIntelligence);
                maxSta = Mathf.Max(maxSta, q.MaxDexterity);
                maxSpd = Mathf.Max(maxSpd, q.BasicSpeed);
                maxAtk = Mathf.Max(maxAtk, q.BasicAttack);
                maxArm = Mathf.Max(maxArm, q.BasicArmor);
            }
            return new List<StatBar>
            {
                new StatBar { Label = MenuText.ClassHealth, Value = p.MaxStrength, Max = maxHp },
                new StatBar { Label = MenuText.ClassAttack, Value = p.BasicAttack, Max = maxAtk },
                new StatBar { Label = MenuText.ClassArmour, Value = p.BasicArmor, Max = maxArm },
                new StatBar { Label = MenuText.ClassSpeed, Value = p.BasicSpeed, Max = maxSpd },
                new StatBar { Label = MenuText.ClassMana, Value = p.MaxIntelligence, Max = maxMana },
                new StatBar { Label = MenuText.ClassStamina, Value = p.MaxDexterity, Max = maxSta },
            };
        }

        private void BuildStatBar(Transform card, StatBar bar, float y)
        {
            var style = Style;
            var rt = MenuUIKit.Rect("Stat_" + bar.Label, card);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(-20f, 18f);

            var labelRt = MenuUIKit.Rect("L", rt);
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(0.52f, 1f);
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            MenuTypography.Label(labelRt.gameObject, style, bar.Label, style.detailFontSize,
                                 style.TextDim);

            // The loading bar at stat size: a thin bevelled housing and a molten fill. Static —
            // a stat does not move, so nothing here flows, glows or emits.
            var trackRt = MenuUIKit.Rect("T", rt);
            trackRt.anchorMin = new Vector2(0.54f, 0.5f);
            trackRt.anchorMax = new Vector2(1f, 0.5f);
            trackRt.pivot = new Vector2(0.5f, 0.5f);
            trackRt.sizeDelta = new Vector2(0f, 10f);
            trackRt.anchoredPosition = Vector2.zero;
            var frame = Frontend.BevelFrameGraphic.Create(trackRt, "Bg");
            frame.Thickness = 2f;
            frame.ShadowScale = 0f;
            frame.Brackets = false;

            var fill = Frontend.FrontendFillGraphic.Create(trackRt, "Fill");
            fill.Profile = Frontend.FrontendFillProfile.Bar;
            fill.Tint = style.Gold;
            fill.Amount = Mathf.Clamp01(bar.Value / Mathf.Max(0.0001f, bar.Max));
            fill.rectTransform.offsetMin = new Vector2(2f, 2f);
            fill.rectTransform.offsetMax = new Vector2(-2f, -2f);
        }

        private Sprite GetCachedPortraitSprite(string playerKey)
        {
            if (_portraitSpriteCache.TryGetValue(playerKey, out var cached)) return cached;
            if (!ClassPortraitPaths.TryGetValue(playerKey, out var path)) return null;
            var sprite = LoadSprite(path, out bool ownedByUs);
            _portraitSpriteCache[playerKey] = sprite;
            // Recorded HERE, at the one moment it is known. A shipped asset and a sprite built
            // from a loose texture are indistinguishable afterwards without AssetDatabase.
            if (ownedByUs && sprite != null) _ownedPortraitSprites.Add(sprite);
            return sprite;
        }

        /// <summary>The texture behind a class's portrait, for the load panel's face thumbnails.</summary>
        private Texture2D GetCachedPortraitTexture(string playerKey)
        {
            if (string.IsNullOrEmpty(playerKey)) return null;
            var sprite = GetCachedPortraitSprite(playerKey);
            return sprite != null ? sprite.texture : null;
        }
    }
}
