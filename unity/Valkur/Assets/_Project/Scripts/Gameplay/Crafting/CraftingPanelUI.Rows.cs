using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using Valkur.UIKit;

namespace Valkur.Gameplay.Crafting
{
    public partial class CraftingPanelUI
    {
        /// <summary>
        /// Rebuild the visible rows for the active trade.
        ///
        /// <para>Rebuilt wholesale rather than diffed, and that is a measurement rather than a
        /// shrug: a trade holds a handful of rows of five widgets, against the Controls
        /// editor's 63 rows of up to twelve — the case that genuinely needed realising once and
        /// showing or hiding afterwards. A diff here would be code carrying a bug surface for a
        /// cost nobody can perceive.</para>
        /// </summary>
        private partial void RefreshRows()
        {
            if (_rowsParent == null) return;

            var profession = ActiveProfession;
            _stationInRange = CraftingStation.IsInRangeOfPlayer(_playerGo, profession);

            UpdateTabHighlights();
            UpdateStationLabel(profession);
            UpdateLevelBar(profession);
            ClearRows();

            if (_catalog == null)
            {
                SetStatus("No hay recetario cargado.", UITheme.WARNING);
                return;
            }
            if (profession == null)
            {
                SetStatus("No hay oficios definidos.", UITheme.WARNING);
                return;
            }

            var recipes = _catalog.RecipesFor(profession);
            int drawn = 0;
            for (int i = 0; i < recipes.Count && drawn < MAX_ROWS; i++)
            {
                BuildRow(recipes[i]);
                drawn++;
            }

            if (drawn == 0)
                SetStatus($"No hay recetas de {profession.displayName}.", UITheme.TEXT_MUTED);
        }

        private void UpdateStationLabel(ProfessionDefinition profession)
        {
            if (_stationText == null) return;

            string noun = profession != null && !string.IsNullOrWhiteSpace(profession.stationName)
                ? profession.stationName
                : "estacion";
            _stationText.text = _stationInRange ? noun + " cerca" : "sin " + noun;
            _stationText.color = _stationInRange ? UITheme.SUCCESS : UITheme.TEXT_MUTED;
        }

        /// <summary>
        /// Draw the active trade's level and its progress towards the next.
        ///
        /// <para>The fill is driven by <c>anchorMax.x</c> rather than a filled Image, because a
        /// stretched anchor keeps the bar correct at any panel width without anybody having to
        /// recompute a pixel size when the layout changes.</para>
        /// </summary>
        private void UpdateLevelBar(ProfessionDefinition profession)
        {
            if (_levelText == null || _levelFill == null) return;

            if (profession == null)
            {
                _levelText.text = "";
                _levelFill.rectTransform.anchorMax = new Vector2(0f, 1f);
                return;
            }

            var skill = profession.skill;
            int tenths = skill != null && _skills != null ? _skills.GetTenths(skill.skillKey) : 0;

            _levelText.text = skill == null
                ? $"{profession.displayName} · sin skill"
                : $"{profession.displayName} · {skill.displayName} {SkillDefinition.FormatPercent(tenths)}";

            _levelFill.rectTransform.anchorMax =
                new Vector2(tenths / (float)SkillDefinition.MaxTenths, 1f);

            // The trade's own accent, so the bar says WHICH trade at a glance rather than
            // relying on the label alone. Alpha is kept low: this is a backdrop behind text.
            var accent = profession.accentColor;
            accent.a = 0.30f;
            _levelFill.color = accent;
        }

        /// <summary>
        /// Repaint the tab strip so the open trade is obvious.
        ///
        /// <para>Through <see cref="UIButton.SetTint"/> rather than <c>targetGraphic.color</c>,
        /// which is a HARDENING here rather than a bug fix — and the distinction is worth
        /// keeping straight. A Button's ColorTint transition drives its graphic's CanvasRenderer
        /// to the ColorBlock, and that MULTIPLIES with Graphic.color, so writing the graphic
        /// renders the PRODUCT of the two. It happened to be harmless in this panel because the
        /// local <c>MakeButton</c> never assigns <c>btn.colors</c>, leaving Unity's default
        /// white block and making the product a no-op. It was NOT harmless in the Skills
        /// editor, whose buttons come from <c>UIButton.Make</c>, which does assign one:
        /// measured there, the active row rendered (32,31,29) against a (31,33,42) panel —
        /// invisible — while untouched rows sat at near-black, so the selection read BACKWARDS.
        /// Going through SetTint means this panel stays correct if anyone ever gives its
        /// buttons a ColorBlock.</para>
        /// </summary>
        private void UpdateTabHighlights()
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                if (_tabButtons[i] == null) continue;

                if (i != _activeTab)
                {
                    UIButton.SetTint(_tabButtons[i], UITheme.BTN_NORMAL);
                    continue;
                }

                // The active tab takes the trade's own accent so the strip and the level bar
                // agree about which trade is open.
                var accent = i < _tabs.Count && _tabs[i] != null
                    ? _tabs[i].accentColor
                    : (Color)UITheme.BTN_ACTIVE;
                accent.a = 0.55f;
                UIButton.SetTint(_tabButtons[i], accent);
            }
        }

        /// <summary>
        /// Destroy the realised rows.
        ///
        /// <para><c>Object.Destroy</c> is an ERROR in Edit Mode, not a warning — seven
        /// ControlsEditorTests went red on the log line alone with every assertion passing. Any
        /// panel path that tears down UI needs this branch if a test is ever to build it.</para>
        /// </summary>
        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] == null) continue;
                if (Application.isPlaying) Destroy(_rows[i]);
                else DestroyImmediate(_rows[i]);
            }
            _rows.Clear();
        }

        private void BuildRow(RecipeDefinition recipe)
        {
            var availability = CraftingService.Evaluate(
                _playerInventory, recipe, _skills, _stationInRange);

            var row = MakePanel(_rowsParent, "Row_" + recipe.recipeId,
                new Vector2(0f, ROW_H), UITheme.BG_ELEVATED);

            // Both numbers, because a LayoutElement that sets only preferredHeight does NOT
            // stop the row expanding: uGUI resolves each property independently and takes
            // flexibleHeight from whatever supplies one, which for an unset element is the
            // parent group's. That is how the chat's input row ended up 80 px tall against a
            // 32 px preference.
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.minHeight = ROW_H;
            le.flexibleHeight = 0f;

            BuildRowIcon(row.transform, recipe);
            BuildRowText(row.transform, recipe, availability);
            BuildRowButton(row.transform, recipe, availability);

            _rows.Add(row);
        }

        private static void BuildRowIcon(Transform parent, RecipeDefinition recipe)
        {
            var go = UIFactory.CreateUI("Icon", parent);
            var img = go.AddComponent<Image>();
            img.preserveAspect = true;
            img.sprite = recipe.output != null ? recipe.output.icon : null;
            // A row whose art failed to import should still be readable, so an absent sprite
            // draws nothing rather than a white box over the row's own background.
            img.color = img.sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(ROW_H - 12f, ROW_H - 12f);
            rect.anchoredPosition = new Vector2(6f, 0f);
        }

        private void BuildRowText(Transform parent, RecipeDefinition recipe,
            CraftAvailability availability)
        {
            var name = MakeLabel(parent, recipe.displayName, 13, UITheme.TEXT_PRIMARY,
                TextAlignmentOptions.TopLeft);
            var nr = name.GetComponent<RectTransform>();
            nr.anchorMin = new Vector2(0f, 0.5f);
            nr.anchorMax = new Vector2(1f, 1f);
            nr.offsetMin = new Vector2(ROW_H, 0f);
            nr.offsetMax = new Vector2(-116f, -6f);

            var detail = MakeLabel(parent, DescribeRow(recipe, availability), 10.5f,
                RowDetailColor(availability), TextAlignmentOptions.TopLeft);
            var dr = detail.GetComponent<RectTransform>();
            dr.anchorMin = new Vector2(0f, 0f);
            dr.anchorMax = new Vector2(1f, 0.5f);
            dr.offsetMin = new Vector2(ROW_H, 6f);
            dr.offsetMax = new Vector2(-116f, 0f);
        }

        private void BuildRowButton(Transform parent, RecipeDefinition recipe,
            CraftAvailability availability)
        {
            bool can = availability.CanCraft;
            var btn = MakeButton(parent, can ? "Fabricar" : "—", 12,
                can ? UITheme.BTN_NORMAL : UITheme.BG_SURFACE, 96f);
            btn.interactable = can;

            var rect = btn.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(96f, ROW_H - 18f);
            rect.anchoredPosition = new Vector2(-8f, 0f);

            if (can) btn.onClick.AddListener(() => CraftOne(recipe));
        }

        /// <summary>
        /// The second line of a row: the full ingredient list when it can be made, and what is
        /// in the way when it cannot.
        ///
        /// <para>Showing the recipe rather than a bare "listo" when it IS craftable is
        /// deliberate — the player is choosing between several things, and what each one costs
        /// is the thing they are choosing on.</para>
        /// </summary>
        private string DescribeRow(RecipeDefinition recipe, CraftAvailability availability)
        {
            switch (availability.Reason)
            {
                case CraftBlockReason.SkillTooLow:
                    return $"Necesita {availability.RequiredSkill}%";
                case CraftBlockReason.NeedsStation:
                    return "Necesita " + (recipe.profession != null
                        && !string.IsNullOrWhiteSpace(recipe.profession.stationName)
                            ? recipe.profession.stationName
                            : "una estacion") + " cerca";
                case CraftBlockReason.MissingIngredients:
                    return "Falta: " + DescribeShortfalls(availability);
                default:
                    return DescribeIngredients(recipe);
            }
        }

        private static Color RowDetailColor(CraftAvailability availability)
        {
            switch (availability.Reason)
            {
                case CraftBlockReason.None: return UITheme.TEXT_SECONDARY;
                case CraftBlockReason.NeedsStation:
                case CraftBlockReason.SkillTooLow: return UITheme.WARNING;
                default: return UITheme.TEXT_MUTED;
            }
        }

        private static string DescribeIngredients(RecipeDefinition recipe)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < recipe.ingredients.Length; i++)
            {
                var line = recipe.ingredients[i];
                if (!line.IsValid) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(line.quantity).Append("x ").Append(line.item.displayName);
            }
            return sb.ToString();
        }
    }
}
