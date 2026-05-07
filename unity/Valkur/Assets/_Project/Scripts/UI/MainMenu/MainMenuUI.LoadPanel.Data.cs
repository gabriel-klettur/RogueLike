using UnityEngine;
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
                    if (run.isLegacy)
                    {
                        if (_mmRunFaceImages?[i] != null) _mmRunFaceImages[i].color = Color.clear;
                        _mmRunTexts[i].text = "<color=#808080>Legacy</color>";
                    }
                    else
                    {
                        if (_mmRunFaceImages?[i] != null)
                        {
                            var tex = GetCachedPortraitTexture(run.playerClass);
                            _mmRunFaceImages[i].texture = tex;
                            _mmRunFaceImages[i].uvRect  = GetFaceUvRect(run.playerClass);
                            _mmRunFaceImages[i].color   = tex != null ? Color.white : Color.clear;
                        }
                        _mmRunTexts[i].text = $"<color=#808080>Lv.{run.maxLevel}</color>";
                    }
                }
                else
                {
                    if (_mmRunFaceImages?[i] != null) _mmRunFaceImages[i].color = Color.clear;
                    _mmRunTexts[i].text = "";
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
                    string display = sv.isAutoSave
                        ? $"<b><color=#FFC800>{Valkur.Gameplay.Save.SaveFileManager.AUTOSAVE_DISPLAY}</color></b>"
                        : sv.fileName;
                    _mmSaveTexts[i].text = sv.isCorrupted
                        ? $"<color=#FF6666>[Corrupted]</color> {display}"
                        : $"{display}  <color=#808080><size=12>{sv.timestamp}</size></color>";
                }
                else _mmSaveTexts[i].text = "";
            }

            // ── Target label ───────────────────────────────────────────────────
            if (_mmLoadTargetLabel != null)
            {
                if (TryGetSelectedSave(out var tsv))
                {
                    string label = tsv.isAutoSave ? Valkur.Gameplay.Save.SaveFileManager.AUTOSAVE_DISPLAY : tsv.fileName;
                    _mmLoadTargetLabel.text = $"Will operate on: <b>{label}</b>";
                }
                else
                    _mmLoadTargetLabel.text = "";
            }

            // ── Detail panel ───────────────────────────────────────────────────
            if (_mmLoadDetailText != null)
            {
                if (_mmLoadRuns.Count == 0)
                {
                    _mmLoadDetailText.text = "No saved games.";
                }
                else if (TryGetSelectedSave(out var info))
                {
                    if (info.isCorrupted)
                    {
                        _mmLoadDetailText.text =
                            "<color=#FF6666><b>Corrupted save</b></color>\n\n" +
                            $"<color=#FFC800>File:</color> {info.fileName}\n\n" +
                            "This save cannot be loaded.\n" +
                            "You can delete it with <b>Del</b>.";
                    }
                    else
                    {
                        string cls  = FormatClassName(info.playerClass);
                        string zone = string.IsNullOrEmpty(info.currentZone) ? "—" : info.currentZone;
                        string hp   = info.maxHp > 0 ? $"{info.hp}/{info.maxHp}" : "—";
                        _mmLoadDetailText.text =
                            $"<color=#FFC800>Class:</color> {cls}\n" +
                            $"<color=#FFC800>Zone:</color>  {zone}\n\n" +
                            $"<color=#FFC800>Level:</color> {info.level}     " +
                            $"<color=#FFC800>XP:</color>  {info.experience}\n" +
                            $"<color=#FFC800>HP:</color>    {hp}\n\n" +
                            $"<color=#FFC800>Saved:</color> {info.timestamp}\n\n" +
                            $"<color=#808080><size=12>{info.fileName}</size></color>";
                    }
                }
                else
                {
                    _mmLoadDetailText.text = "Select a save.";
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
            };

        private static Rect GetFaceUvRect(string playerClass)
        {
            if (!string.IsNullOrEmpty(playerClass) &&
                ClassFaceUvRects.TryGetValue(playerClass, out var rect))
                return rect;
            return new Rect(0f, 0f, 1f, 1f);
        }

        private Texture2D GetCachedPortraitTexture(string playerKey)
        {
            if (string.IsNullOrEmpty(playerKey)) return null;
            if (_portraitSpriteCache.TryGetValue(playerKey, out var cached) && cached != null)
                return cached.texture;
            if (!ClassPortraitPaths.TryGetValue(playerKey, out var path)) return null;
            return Resources.Load<Texture2D>(path);
        }
    }
}
