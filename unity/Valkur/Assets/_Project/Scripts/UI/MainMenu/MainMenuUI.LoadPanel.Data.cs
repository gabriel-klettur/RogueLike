using UnityEngine;
using Valkur.Core.UI;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Save;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        // ── Data refresh ──────────────────────────────────────────────────────────

        private void RefreshMMLoadPanel()
        {
            // Try to preserve the previously selected save by file path.
            string prevSavePath = null;
            if (_mmLoadRunSel >= 0 && _mmLoadRunSel < _mmLoadRuns.Count)
            {
                var pr = _mmLoadRuns[_mmLoadRunSel];
                if (_mmLoadSaveSel >= 0 && _mmLoadSaveSel < pr.saves.Count)
                    prevSavePath = pr.saves[_mmLoadSaveSel].path;
            }

            _mmLoadRuns     = SaveFileManager.ListSavesByRun();
            _mmLoadRunSel   = 0;
            _mmLoadSaveSel  = 0;
            _mmLoadRunScroll = 0;

            if (!string.IsNullOrEmpty(prevSavePath))
            {
                for (int ri = 0; ri < _mmLoadRuns.Count; ri++)
                {
                    var grp = _mmLoadRuns[ri];
                    for (int si = 0; si < grp.saves.Count; si++)
                    {
                        if (string.Equals(grp.saves[si].path, prevSavePath,
                                          System.StringComparison.OrdinalIgnoreCase))
                        { _mmLoadRunSel = ri; _mmLoadSaveSel = si; break; }
                    }
                }
            }

            EnsureMMLoadScroll();
            SetLoadMode(LoadPanelMode.List);
            UpdateMMLoadVisuals();
        }

        private void UpdateMMLoadVisuals()
        {
            if (_mmRunPills == null) return;

            // ── Left column: run list ──────────────────────────────────────────
            for (int i = 0; i < MM_RUN_ROWS; i++)
            {
                int dataIdx = _mmLoadRunScroll + i;
                bool hasRun  = dataIdx < _mmLoadRuns.Count;
                bool selRun  = dataIdx == _mmLoadRunSel;

                _mmRunPills[i].color = selRun && hasRun ? PillColor  : Color.clear;
                _mmRunBars[i].color  = selRun && hasRun ? AccentGold : Color.clear;
                _mmRunTexts[i].color = selRun && hasRun ? TextSelected : TextNormal;

                if (hasRun)
                {
                    var run = _mmLoadRuns[dataIdx];
                    if (_mmRunFaceImages?[i] != null)
                    {
                        var tex = GetCachedPortraitTexture(run.playerClass);
                        _mmRunFaceImages[i].texture = tex;
                        _mmRunFaceImages[i].uvRect = GetFaceUvRect(run.playerClass);
                        _mmRunFaceImages[i].color = tex != null ? Color.white : Color.clear;
                    }
                    _mmRunTexts[i].text = DescribeRun(run);
                }
                else
                {
                    if (_mmRunFaceImages?[i] != null) _mmRunFaceImages[i].color = Color.clear;
                    _mmRunTexts[i].text = string.Empty;
                }
            }

            // ── Right column: save list ────────────────────────────────────────
            var currentRun = (_mmLoadRunSel >= 0 && _mmLoadRunSel < _mmLoadRuns.Count)
                ? _mmLoadRuns[_mmLoadRunSel] : null;

            for (int i = 0; i < MM_SAVE_ROWS; i++)
            {
                bool hasSave = currentRun != null && i < currentRun.saves.Count;
                bool selSave = i == _mmLoadSaveSel;

                _mmSavePills[i].color = selSave && hasSave ? PillColor  : Color.clear;
                _mmSaveBars[i].color  = selSave && hasSave ? AccentGold : Color.clear;
                _mmSaveTexts[i].color = selSave && hasSave ? TextSelected : TextNormal;

                if (hasSave)
                {
                    var sv = currentRun.saves[i];
                    string name = sv.isAutoSave ? MenuText.LoadAutoSave : sv.fileName;
                    string when = HumanTime(sv.timestamp);
                    _mmSaveTexts[i].text = sv.isCorrupted
                        ? $"<color=#{Hex(Style.Danger)}>{MenuText.LoadCorrupted}</color>  {name}"
                        : $"{name}   <color=#{Hex(Style.TextMuted)}><size=90%>{when}</size></color>";
                }
                else _mmSaveTexts[i].text = "";
            }

            // ── Target label ───────────────────────────────────────────────────
            if (_mmLoadTargetLabel != null)
            {
                if (TryGetSelectedSave(out var tsv))
                {
                    string label = tsv.isAutoSave ? MenuText.LoadAutoSave : tsv.fileName;
                    _mmLoadTargetLabel.text = MenuText.LoadSelected("<b>" + label + "</b>");
                }
                else
                    _mmLoadTargetLabel.text = "";
            }

            // ── Detail panel ───────────────────────────────────────────────────
            if (_mmLoadDetailText != null)
            {
                if (_mmLoadRuns.Count == 0)
                {
                    _mmLoadDetailText.text = MenuText.LoadNoSaves;
                }
                else if (TryGetSelectedSave(out var info))
                {
                    string gold = Hex(Style.Gold);
                    string muted = Hex(Style.TextMuted);
                    if (info.isCorrupted)
                    {
                        _mmLoadDetailText.text =
                            $"<color=#{Hex(Style.Danger)}><b>{MenuText.LoadCorrupted}</b></color>\n\n" +
                            $"<color=#{gold}>{MenuText.LoadSaved}:</color> {HumanTime(info.timestamp)}\n\n" +
                            MenuText.LoadCorruptedDetail + "\n\n" +
                            $"<color=#{muted}><size=85%>{info.fileName}</size></color>";
                    }
                    else
                    {
                        string cls = FormatClassName(info.playerClass);
                        string zone = string.IsNullOrEmpty(info.currentZone) ? "—" : info.currentZone;
                        string hp = info.maxHp > 0 ? $"{info.hp} / {info.maxHp}" : "—";
                        _mmLoadDetailText.text =
                            $"<color=#{gold}>{MenuText.LoadClass}:</color> {cls}\n" +
                            $"<color=#{gold}>{MenuText.LoadZone}:</color> {zone}\n\n" +
                            $"<color=#{gold}>{MenuText.LoadLevel}:</color> {info.level}    " +
                            $"<color=#{gold}>{MenuText.LoadXp}:</color> {info.experience}\n" +
                            $"<color=#{gold}>{MenuText.LoadHp}:</color> {hp}\n\n" +
                            $"<color=#{gold}>{MenuText.LoadSaved}:</color> {HumanTime(info.timestamp)}\n\n" +
                            $"<color=#{muted}><size=85%>{info.fileName}</size></color>";
                    }
                }
                else
                {
                    _mmLoadDetailText.text = MenuText.LoadPickOne;
                }
            }

            UpdateMMLoadHoverBorders();
        }

        private void UpdateMMLoadHoverBorders()
        {
            if (_mmRunHoverBorders != null)
            {
                for (int i = 0; i < MM_RUN_ROWS; i++)
                {
                    var strips = _mmRunHoverBorders[i];
                    if (strips == null) continue;
                    int dataIdx = _mmLoadRunScroll + i;
                    bool isSel = dataIdx == _mmLoadRunSel && dataIdx < _mmLoadRuns.Count;
                    Color c = (i == _mmRunHover && !isSel) ? HoverBorderColor : Color.clear;
                    foreach (var img in strips) if (img != null) img.color = c;
                }
            }

            if (_mmSaveHoverBorders != null)
            {
                var cr = (_mmLoadRunSel >= 0 && _mmLoadRunSel < _mmLoadRuns.Count)
                    ? _mmLoadRuns[_mmLoadRunSel] : null;
                for (int i = 0; i < MM_SAVE_ROWS; i++)
                {
                    var strips = _mmSaveHoverBorders[i];
                    if (strips == null) continue;
                    bool hasSave = cr != null && i < cr.saves.Count;
                    bool isSel = i == _mmLoadSaveSel && hasSave;
                    Color c = (i == _mmSaveHover && !isSel) ? HoverBorderColor : Color.clear;
                    foreach (var img in strips) if (img != null) img.color = c;
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// What a run row SAYS. The shipped panel printed the whole row as <c>Lv.1</c> — three
        /// consecutive rows of a save folder read "Lv.1", "Lv.1", "Lv.1" over the same face, so
        /// the list whose only job is "choose which of your runs" could not tell them apart. The
        /// run's own <c>displayName</c> was already in <c>RunGroupInfo</c> and was never read.
        /// </summary>
        private static string DescribeRun(Valkur.Gameplay.RunGroupInfo run)
        {
            if (run == null) return string.Empty;
            if (run.isLegacy) return "<color=#808080>" + MenuText.LoadLegacyRun + "</color>";

            string name = !string.IsNullOrWhiteSpace(run.displayName)
                ? run.displayName
                : FormatClassName(run.playerClass);
            string when = HumanTime(run.latestTimestamp);
            return name + "\n<color=#808080><size=85%>" + MenuText.LoadLevel + " " + run.maxLevel
                 + "  ·  " + when + "</size></color>";
        }

        /// <summary>
        /// A date a person reads. The panel used to print the save's raw metadata stamp —
        /// <c>2026-09-12T02:16:34</c> — in the list the player chooses from. An unparseable
        /// stamp is shown VERBATIM rather than swallowed: it is still the only thing telling two
        /// saves apart, and hiding it would make them identical.
        /// </summary>
        private static string HumanTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "—";
            if (System.DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                                         System.Globalization.DateTimeStyles.None, out var when))
                return MenuText.FormatTimestamp(when);
            return raw;
        }

        /// <summary>A theme colour as a TMP rich-text hex, so no panel writes a literal.</summary>
        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        private static string FormatClassName(string key)
        {
            if (string.IsNullOrEmpty(key)) return "—";
            return char.ToUpperInvariant(key[0]) + key.Substring(1).ToLowerInvariant();
        }

        // UV rects for each class portrait image (1536×1024 group portraits).
        // Each rect crops the specific character's face from their highlighted portrait.
        // Format: Rect(x_left, y_bottom, width, height) — Unity UV origin = bottom-left.
        // All crops are ~280×280px (square) for distortion-free display in square containers.
        private static readonly System.Collections.Generic.Dictionary<string, Rect> ClassFaceUvRects =
            new System.Collections.Generic.Dictionary<string, Rect>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "barbarian", new Rect(0.000f, 0.552f, 0.182f, 0.273f) },
                { "elven",     new Rect(0.156f, 0.566f, 0.182f, 0.273f) },
                { "mague",     new Rect(0.352f, 0.449f, 0.182f, 0.273f) },
                { "valkyrie",  new Rect(0.592f, 0.576f, 0.182f, 0.273f) },
                { "dwarf",     new Rect(0.801f, 0.547f, 0.182f, 0.273f) },
                // The vampire was MISSING, and the failure was silent: GetFaceUvRect fell back
                // to the whole 0..1 rect, so her thumbnail showed the entire 1536x1024 tavern
                // plate squeezed into 33 x 33 px instead of a face. Her portrait is composed
                // against the empty plate (see ClassPortraitPaths), so she stands where the
                // group's fifth figure would be.
                { "vampire",   new Rect(0.801f, 0.547f, 0.182f, 0.273f) },
            };

        private static Rect GetFaceUvRect(string playerClass)
        {
            if (!string.IsNullOrEmpty(playerClass) &&
                ClassFaceUvRects.TryGetValue(playerClass, out var rect))
                return rect;
            return new Rect(0f, 0f, 1f, 1f);
        }

    }
}
