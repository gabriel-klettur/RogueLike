using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The right half of the SKILLS tab: the selected skill's number, bar and one of four bodies —
    /// what a gathering skill uncovers and where to train it, which recipes a crafting skill opens,
    /// what a physical skill's numbers are worth now and at the next milestone, or why a locked
    /// skill cannot be trained yet.
    ///
    /// <para>Built once per SELECTION and refreshed in place at the panel's slow tick. Rebuilding
    /// on the tick would re-bake every wood icon on the GPU twice a second.</para>
    /// </summary>
    public sealed partial class SkillsHUD
    {
        private const int TierRowTexels = 10;
        private const int NodeRowTexels = 9;
        private const int RecipeRowTexels = 8;
        private const int IconTexels = 8;
        private const int NodeColumnWidth = 116;
        private const int PhysicalRowTexels = 10;
        private const int PhysicalLabelWidth = 56;

        private void BuildDetail(Row row)
        {
            for (int i = _detail.childCount - 1; i >= 0; i--)
                SheetPanelChrome.DestroyUI(_detail.GetChild(i).gameObject);
            _refreshDetail = null;

            var skill = row.Skill;
            int w = (int)_detail.sizeDelta.x;
            int h = (int)_detail.sizeDelta.y;
            var theme = _chrome.Theme;
            Transform d = _detail;

            // ── Header: name, category, the number and its bar ──
            var name = Text(d, "Name", 2, h - 9, w - 90, HudFontFace.Small, HudTextAlign.Left, theme.gold);
            name.SetText(string.IsNullOrEmpty(skill.displayName) ? skill.skillKey : skill.displayName);
            Text(d, "Category", w - 88, h - 9, 86, HudFontFace.Small, HudTextAlign.Right, theme.textDisabled)
                .SetText(CategoryLabel(skill.category));

            var percent = Text(d, "Percent", 2, h - 21, 60, HudFontFace.Large, HudTextAlign.Left, theme.text);
            var sign = Text(d, "Sign", 40, h - 21, 10, HudFontFace.Small, HudTextAlign.Left, theme.textDim);
            sign.SetText("%");

            int barW = w - 4;
            SheetPanelChrome.Tinted("Track", d, _chrome.Art.BarFrameThin, 2, h - 30, barW, 6, theme.stoneLight, Image.Type.Sliced);
            var fill = SheetPanelChrome.Tinted("Fill", d, _chrome.Art.White, 3, h - 29, 1, 4, skill.accentColor);

            var descLines = Wrap(skill.description, w - 4, HudFontFace.Small);
            if (descLines.Count > 0)
                Text(d, "Desc0", 2, h - 40, w - 4, HudFontFace.Small, HudTextAlign.Left, theme.textDim).SetText(descLines[0]);
            if (descLines.Count > 1)
                Text(d, "Desc1", 2, h - 48, w - 4, HudFontFace.Small, HudTextAlign.Left, theme.textDim).SetText(descLines[1]);

            // What the next percent buys — the line that turns a number into a goal.
            var nextLine = Text(d, "Next", 2, h - 57, w - 4, HudFontFace.Small, HudTextAlign.Left, theme.info);

            System.Action refreshBody;
            if (!row.Trainable)
            {
                DetailKind = "locked";
                refreshBody = BuildLocked(d, row, w, h - 60);
            }
            else switch (skill.category)
            {
                case SkillCategory.Gathering when skill.yieldTable != null:
                    DetailKind = "gathering";
                    refreshBody = BuildGathering(d, skill, w, h, nextLine, barW);
                    break;
                case SkillCategory.Physical:
                    DetailKind = "physical";
                    refreshBody = BuildPhysical(d, skill, w, h, nextLine, barW);
                    break;
                default:
                    DetailKind = "crafting";
                    refreshBody = BuildCrafting(d, skill, w, h, nextLine, barW);
                    break;
            }

            _refreshDetail = () =>
            {
                var skills = PlayerSkillsNow();
                int tenths = skills != null ? skills.GetTenths(skill.skillKey) : 0;
                float pct = SkillDefinition.ToPercent(tenths);

                percent.SetText(pct.ToString("0.0", CultureInfo.InvariantCulture));
                var signRt = sign.rectTransform;
                signRt.anchoredPosition = new Vector2(2 + percent.InkWidth + 2, signRt.anchoredPosition.y);
                percent.color = row.Trainable ? theme.text : theme.textDisabled;

                var fillRt = fill.rectTransform;
                fillRt.sizeDelta = new Vector2(Mathf.Max(1, Mathf.RoundToInt(pct / 100f * (barW - 2))), fillRt.sizeDelta.y);
                fill.enabled = tenths > 0;

                refreshBody?.Invoke();
            };
        }

        // ── Locked ───────────────────────────────────────────────────────────

        private System.Action BuildLocked(Transform d, Row row, int w, int top)
        {
            var theme = _chrome.Theme;
            Text(d, "LockedTitle", 2, top, w - 4, HudFontFace.Small, HudTextAlign.Left, theme.warning)
                .SetText("Aún no se puede entrenar");

            string why = row.Skill.category switch
            {
                SkillCategory.Crafting => "Este oficio todavía no tiene recetas.",
                SkillCategory.Physical => "Todavía no se puede entrenar esta habilidad física.",
                _ => "Todavía no hay dónde practicarla en el mundo.",
            };
            var lines = Wrap(why, w - 4, HudFontFace.Small);
            for (int i = 0; i < lines.Count && i < 2; i++)
                Text(d, "Why" + i, 2, top - 10 - i * 8, w - 4, HudFontFace.Small, HudTextAlign.Left, theme.textDim)
                    .SetText(lines[i]);
            return null;
        }

        // ── Gathering ────────────────────────────────────────────────────────

        private System.Action BuildGathering(Transform d, SkillDefinition skill, int w, int h,
            HudPixelText nextLine, int barW)
        {
            var theme = _chrome.Theme;
            var table = skill.yieldTable;

            // Where each wood unlocks, marked on the bar itself.
            foreach (var tier in table.tiers)
            {
                if (tier == null || tier.minSkill <= 0f) continue;
                int x = 3 + Mathf.RoundToInt(tier.minSkill / 100f * (barW - 2));
                SheetPanelChrome.Tinted("Tick_" + tier.key, d, _chrome.Art.White, x, h - 33, 1, 2, TierColour(tier));
            }

            int top = h - 68;
            Text(d, "NodesHeader", 2, top, NodeColumnWidth, HudFontFace.Small, HudTextAlign.Left, theme.gold)
                .SetText("Dónde aprender");

            var nodeNames = new List<HudPixelText>();
            var nodeEase = new List<HudPixelText>();
            for (int i = 0; i < skill.trainingNodes.Count; i++)
            {
                int y = top - 10 - i * NodeRowTexels;
                if (y < 0) break;
                nodeNames.Add(Text(d, "Node" + i, 4, y, 72, HudFontFace.Small, HudTextAlign.Left, theme.textDim));
                nodeEase.Add(Text(d, "Ease" + i, 74, y, NodeColumnWidth - 76, HudFontFace.Small, HudTextAlign.Right, theme.textDim));
            }

            int x0 = NodeColumnWidth + 6;
            int colW = w - x0;
            var tiersHeader = Text(d, "TiersHeader", x0, top, colW, HudFontFace.Small, HudTextAlign.Left, theme.gold);

            int iconPx = IconTexels * Mathf.Max(1, _chrome.PixelScale);
            var tierNames = new List<HudPixelText>();
            var tierStatus = new List<HudPixelText>();
            var tierIcons = new List<RawImage>();
            for (int i = 0; i < table.tiers.Count; i++)
            {
                var tier = table.tiers[i];
                int y = top - 10 - i * TierRowTexels;
                if (y < 0) break;

                var iconRt = HudRect.Make("Icon_" + tier.key, d, x0, y - 1, IconTexels, IconTexels);
                var icon = iconRt.gameObject.AddComponent<RawImage>();
                icon.raycastTarget = false;
                var sprite = tier.items != null && tier.items.Length > 0 && tier.items[0] != null ? tier.items[0].icon : null;
                var baked = HudTextureBaker.Icon(sprite, iconPx);
                icon.texture = baked != null ? (Texture)baked : (sprite != null ? sprite.texture : null);
                tierIcons.Add(icon);

                tierNames.Add(Text(d, "Tier_" + tier.key, x0 + 11, y, colW - 52, HudFontFace.Small, HudTextAlign.Left, theme.text));
                tierStatus.Add(Text(d, "Status_" + tier.key, w - 42, y, 40, HudFontFace.Small, HudTextAlign.Right, theme.textDim));
            }

            return () =>
            {
                var skills = PlayerSkillsNow();
                int tenths = skills != null ? skills.GetTenths(skill.skillKey) : 0;
                float pct = SkillDefinition.ToPercent(tenths);

                GatheringYieldTable.Tier next = null;
                foreach (var tier in table.tiers)
                    if (tier != null && tier.minSkill > pct && (next == null || tier.minSkill < next.minSkill))
                        next = tier;
                nextLine.SetText(next == null
                    ? (pct >= 100f ? "Maestría completa." : "Todas las maderas descubiertas.")
                    : "Siguiente: " + next.displayName + " a " +
                      next.minSkill.ToString("0", CultureInfo.InvariantCulture) + "%" +
                      (string.IsNullOrEmpty(next.whereHint) ? string.Empty : " en " + next.whereHint));

                for (int i = 0; i < nodeNames.Count; i++)
                {
                    var node = skill.trainingNodes[i];
                    if (node == null) { nodeNames[i].SetText(string.Empty); nodeEase[i].SetText(string.Empty); continue; }
                    var ease = skill.Ease(tenths, node.skillDifficulty);
                    nodeNames[i].SetText(string.IsNullOrEmpty(node.nodeDisplayName) ? node.name : node.nodeDisplayName);
                    nodeNames[i].color = ease == SkillEase.Suitable ? theme.text : theme.textDim;
                    nodeEase[i].SetText(ShortEase(ease));
                    nodeEase[i].color = EaseColour(ease);
                }

                int found = 0;
                for (int i = 0; i < tierNames.Count; i++)
                {
                    var tier = table.tiers[i];
                    bool unlocked = pct >= tier.minSkill;
                    if (unlocked) found++;
                    tierNames[i].SetText(tier.displayName);
                    tierNames[i].color = unlocked ? TierColour(tier) : theme.textDisabled;
                    tierIcons[i].color = unlocked ? Color.white : new Color(0.25f, 0.25f, 0.3f, 0.8f);
                    tierStatus[i].SetText(unlocked ? "ok" : tier.minSkill.ToString("0", CultureInfo.InvariantCulture) + "%");
                    tierStatus[i].color = unlocked ? theme.success : theme.textDisabled;
                }
                tiersHeader.SetText($"Maderas {found}/{table.tiers.Count}");
            };
        }

        // ── Crafting ─────────────────────────────────────────────────────────

        private System.Action BuildCrafting(Transform d, SkillDefinition skill, int w, int h,
            HudPixelText nextLine, int barW)
        {
            var theme = _chrome.Theme;
            var recipes = RecipesFor(skill);

            foreach (var r in recipes)
            {
                if (r.requiredSkill <= 0) continue;
                int x = 3 + Mathf.RoundToInt(r.requiredSkill / 100f * (barW - 2));
                SheetPanelChrome.Tinted("Tick_" + r.recipeId, d, _chrome.Art.White, x, h - 33, 1, 2, theme.goldShade);
            }

            int top = h - 68;
            var header = Text(d, "RecipesHeader", 2, top, w - 4, HudFontFace.Small, HudTextAlign.Left, theme.gold);

            int colW = (w - 6) / 2;
            int rowsPerColumn = Mathf.Max(1, (top - 10) / RecipeRowTexels + 1);
            var names = new List<HudPixelText>();
            var reqs = new List<HudPixelText>();
            for (int i = 0; i < recipes.Count && i < rowsPerColumn * 2; i++)
            {
                int column = i / rowsPerColumn;
                int x = 2 + column * (colW + 2);
                int y = top - 10 - (i % rowsPerColumn) * RecipeRowTexels;
                names.Add(Text(d, "Recipe" + i, x, y, colW - 26, HudFontFace.Small, HudTextAlign.Left, theme.text));
                reqs.Add(Text(d, "Req" + i, x + colW - 26, y, 24, HudFontFace.Small, HudTextAlign.Right, theme.textDim));
            }

            return () =>
            {
                var skills = PlayerSkillsNow();
                int tenths = skills != null ? skills.GetTenths(skill.skillKey) : 0;

                int open = 0;
                RecipeDefinition next = null;
                foreach (var r in recipes)
                {
                    if (tenths >= r.requiredSkill * 10) open++;
                    else if (next == null) next = r;
                }

                header.SetText(recipes.Count > names.Count
                    ? $"Recetas {open}/{recipes.Count} (+{recipes.Count - names.Count})"
                    : $"Recetas {open}/{recipes.Count}");
                nextLine.SetText(next == null
                    ? "Todas las recetas disponibles."
                    : $"Siguiente receta: {next.displayName} a {next.requiredSkill}%");

                for (int i = 0; i < names.Count; i++)
                {
                    var r = recipes[i];
                    bool unlocked = tenths >= r.requiredSkill * 10;
                    names[i].SetText(r.displayName);
                    names[i].color = unlocked ? theme.text : theme.textDisabled;
                    reqs[i].SetText(r.requiredSkill + "%");
                    reqs[i].color = unlocked ? theme.success : theme.textDisabled;
                }
            };
        }

        /// <summary>Every well-formed recipe whose trade trains <paramref name="skill"/>, easiest first.</summary>
        private List<RecipeDefinition> RecipesFor(SkillDefinition skill)
        {
            var list = new List<RecipeDefinition>();
            if (_recipes == null) return list;
            foreach (var r in _recipes.Recipes)
                if (r != null && r.IsWellFormed && r.profession.skill == skill) list.Add(r);

            // Stable by requirement, then by name: the order the player unlocks them in.
            list.Sort((a, b) =>
            {
                int c = a.requiredSkill.CompareTo(b.requiredSkill);
                return c != 0 ? c : string.CompareOrdinal(a.displayName, b.displayName);
            });
            return list;
        }

        // ── Physical ─────────────────────────────────────────────────────────

        /// <summary>
        /// A physical skill has no nodes or recipes to browse — it IS the body — so its detail
        /// shows what the number actually buys: the four locomotion numbers
        /// <see cref="LocomotionTuning"/> derives from it, now and at the next 10 % milestone.
        ///
        /// <para>Only <c>athletics</c> has a tuning asset behind it. A hypothetical second
        /// physical skill with no matching tuning falls back to its description alone — a table
        /// of the WRONG skill's numbers is worse than an empty one.</para>
        /// </summary>
        private System.Action BuildPhysical(Transform d, SkillDefinition skill, int w, int h,
            HudPixelText nextLine, int barW)
        {
            var theme = _chrome.Theme;
            bool isAthletics = string.Equals(skill.skillKey, LocomotionTuning.SkillKey,
                System.StringComparison.OrdinalIgnoreCase);

            int top = h - 68;
            if (!isAthletics)
            {
                Text(d, "PhysNoData", 2, top, w - 4, HudFontFace.Small, HudTextAlign.Left, theme.textDim)
                    .SetText("Sin números para esta skill todavía.");
                nextLine.SetText(string.Empty);
                return null;
            }

            var tuning = LocomotionTuning.Active;
            int colW = (w - 4 - PhysicalLabelWidth) / 2;
            int xNow = PhysicalLabelWidth;
            int xNext = PhysicalLabelWidth + colW;

            Text(d, "PhysNowHeader", xNow, top, colW, HudFontFace.Small, HudTextAlign.Left, theme.gold)
                .SetText("AHORA");
            var nextHeader = Text(d, "PhysNextHeader", xNext, top, colW, HudFontFace.Small, HudTextAlign.Left, theme.gold);

            string[] labels = { "Arranque", "Carrera", "Aguante", "Giro" };
            var nowValues = new List<HudPixelText>(labels.Length);
            var nextValues = new List<HudPixelText>(labels.Length);
            for (int i = 0; i < labels.Length; i++)
            {
                int y = top - 10 - i * PhysicalRowTexels;
                Text(d, "PhysLabel" + i, 2, y, PhysicalLabelWidth - 2, HudFontFace.Small, HudTextAlign.Left, theme.textDim)
                    .SetText(labels[i]);
                nowValues.Add(Text(d, "PhysNow" + i, xNow, y, colW - 2, HudFontFace.Small, HudTextAlign.Left, theme.text));
                nextValues.Add(Text(d, "PhysNext" + i, xNext, y, colW - 2, HudFontFace.Small, HudTextAlign.Left, theme.textDim));
            }

            return () =>
            {
                var skills = PlayerSkillsNow();
                int tenths = skills != null ? skills.GetTenths(skill.skillKey) : 0;
                float maxEnergy = PlayerMaxEnergy();

                SetPhysicalRow(nowValues, tuning, tenths / (float)SkillDefinition.MaxTenths, maxEnergy);

                bool atMax = tenths >= SkillDefinition.MaxTenths;
                int nextTenths = Mathf.Min(SkillDefinition.MaxTenths, (tenths / 100 + 1) * 100);

                nextHeader.SetText(atMax ? "MÁXIMO" : $"AL {nextTenths / 10}%");
                if (atMax)
                {
                    foreach (var value in nextValues) value.SetText("—");
                }
                else
                {
                    SetPhysicalRow(nextValues, tuning, nextTenths / (float)SkillDefinition.MaxTenths, maxEnergy);
                }

                nextLine.SetText(atMax
                    ? "Maestría completa: arranque, carrera y aguante al máximo."
                    : $"Al {nextTenths / 10}%: arranque, carrera, aguante y giro mejoran.");
            };
        }

        private static void SetPhysicalRow(List<HudPixelText> values, LocomotionTuning tuning, float skill01, float maxEnergy)
        {
            values[0].SetText(FormatSeconds(tuning.StartSeconds(skill01)));
            values[1].SetText(FormatMultiplier(tuning.RunMultiplier(skill01)));
            values[2].SetText(FormatEndurance(tuning.RunEndurance(skill01, maxEnergy)));
            values[3].SetText(FormatDegrees(tuning.TurnTolerance(skill01)));
        }

        /// <summary>The player's max energy pool, or 100 with no player / no energy pool yet.</summary>
        private static float PlayerMaxEnergy()
        {
            var playerGo = EntityRegistry.Player;
            var controller = playerGo != null ? playerGo.GetComponent<PlayerController>() : null;
            var energy = controller != null ? controller.Energy : null;
            return energy != null ? energy.Max : 100f;
        }

        private static string FormatSeconds(float seconds) =>
            seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

        private static string FormatMultiplier(float multiplier) =>
            "x" + multiplier.ToString("0.00", CultureInfo.InvariantCulture);

        private static string FormatDegrees(float degrees) =>
            Mathf.RoundToInt(degrees).ToString(CultureInfo.InvariantCulture) + "°";

        private static string FormatEndurance(float seconds) =>
            // Words, not the infinity sign: LiberationSans SDF has no U+221E and draws an empty box.
            float.IsInfinity(seconds) ? "sin límite" : Mathf.RoundToInt(seconds).ToString(CultureInfo.InvariantCulture) + " s";

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Greedy word wrap against the pixel font's own measurement.</summary>
        private List<string> Wrap(string text, int widthTexels, HudFontFace face)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            var line = new System.Text.StringBuilder();
            foreach (var word in text.Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && _chrome.Art.Measure(candidate, face) > widthTexels)
                {
                    lines.Add(line.ToString());
                    line.Clear().Append(word);
                }
                else
                {
                    line.Clear().Append(candidate);
                }
            }
            if (line.Length > 0) lines.Add(line.ToString());
            return lines;
        }

        private static string CategoryLabel(SkillCategory category) => category switch
        {
            SkillCategory.Crafting => "Fabricación",
            SkillCategory.Physical => "Físico",
            _ => "Recolección",
        };

        private static string ShortEase(SkillEase ease)
        {
            switch (ease)
            {
                case SkillEase.Trivial:  return "trivial";
                case SkillEase.Easy:     return "fácil";
                case SkillEase.Suitable: return "adecuado";
                case SkillEase.Hard:     return "difícil";
                default:                 return "muy difícil";
            }
        }

        private Color EaseColour(SkillEase ease)
        {
            var theme = _chrome.Theme;
            switch (ease)
            {
                case SkillEase.Suitable: return theme.success;
                case SkillEase.Easy:     return theme.info;
                case SkillEase.Hard:     return theme.warning;
                case SkillEase.VeryHard: return theme.danger;
                default:                 return theme.textDisabled;
            }
        }

        private static Color TierColour(GatheringYieldTable.Tier tier)
        {
            var item = tier.items != null && tier.items.Length > 0 ? tier.items[0] : null;
            return item != null ? RarityPalette.Color(item.rarity) : Color.white;
        }
    }
}
