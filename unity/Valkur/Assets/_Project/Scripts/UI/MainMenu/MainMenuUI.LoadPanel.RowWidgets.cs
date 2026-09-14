using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core.UI;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        // ── Run list rows (left column) ───────────────────────────────────────────

        private void BuildRunListRows(Transform panel, float splitX)
        {
            var runList = CreateUIObject("MMRunList", panel);
            var rlR = runList.GetComponent<RectTransform>();
            rlR.anchorMin = new Vector2(0.01f, 0.12f); rlR.anchorMax = new Vector2(splitX, 0.81f);
            rlR.pivot = new Vector2(0f, 1f); rlR.sizeDelta = Vector2.zero;
            rlR.anchoredPosition = Vector2.zero;

            _mmRunPills        = new Graphic[MM_RUN_ROWS];
            _mmRunTexts        = new TextMeshProUGUI[MM_RUN_ROWS];
            _mmRunFaceImages   = new RawImage[MM_RUN_ROWS];
            _mmRunHoverBorders = new Graphic[MM_RUN_ROWS][];

            // Two lines per row now — a name and, under it, the level and the date — so the row
            // is tall enough to hold them. The shipped row was 37 px because its entire content
            // was the string "Lv.1".
            const float runRowH = 46f;
            const float runGap  = 4f;

            for (int i = 0; i < MM_RUN_ROWS; i++)
            {
                float cy = -i * (runRowH + runGap);

                _mmRunPills[i] = BuildSlotFill($"RnPill_{i}", runList.transform, cy, runRowH);

                // Character face thumbnail (crops portrait to face area via uvRect)
                float faceSize = runRowH - 4f;
                var faceGo = CreateUIObject($"RnFace_{i}", runList.transform);
                var faceRt = faceGo.GetComponent<RectTransform>();
                faceRt.anchorMin = new Vector2(0f, 1f); faceRt.anchorMax = new Vector2(0f, 1f);
                faceRt.pivot = new Vector2(0f, 1f);
                faceRt.anchoredPosition = new Vector2(6f, cy - 2f);
                faceRt.sizeDelta = new Vector2(faceSize, faceSize);
                _mmRunFaceImages[i] = faceGo.AddComponent<RawImage>();
                _mmRunFaceImages[i].color = Color.clear;

                var txtGo = CreateUIObject($"RnTxt_{i}", runList.transform);
                var txtR = txtGo.GetComponent<RectTransform>();
                txtR.anchorMin = new Vector2(0f, 1f); txtR.anchorMax = new Vector2(1f, 1f);
                txtR.pivot = new Vector2(0f, 1f);
                txtR.anchoredPosition = new Vector2(46f, cy); txtR.sizeDelta = new Vector2(-46f, runRowH);
                _mmRunTexts[i] = MenuTypography.Label(txtGo, Style, string.Empty,
                                                      Style.detailFontSize + 1f, Style.TextPrimary,
                                                      TextAlignmentOptions.Left);
                _mmRunTexts[i].lineSpacing = -18f;

                var runHitGo = CreateUIObject($"RnHit_{i}", runList.transform);
                var runHitRt = runHitGo.GetComponent<RectTransform>();
                runHitRt.anchorMin = new Vector2(0f, 1f); runHitRt.anchorMax = new Vector2(1f, 1f);
                runHitRt.pivot = new Vector2(0.5f, 1f);
                runHitRt.anchoredPosition = new Vector2(0f, cy); runHitRt.sizeDelta = new Vector2(0f, runRowH);
                var runHitImg = runHitGo.AddComponent<Image>(); runHitImg.color = Color.clear;
                var runBtn = runHitGo.AddComponent<Button>(); runBtn.targetGraphic = runHitImg;
                int rCap = i;
                runBtn.onClick.AddListener(() =>
                {
                    int idx = _mmLoadRunScroll + rCap;
                    if (idx < 0 || idx >= _mmLoadRuns.Count) return;
                    _mmLoadRunSel = idx; _mmLoadSaveSel = 0; UpdateMMLoadVisuals();
                });
                var runTrig = runHitGo.AddComponent<EventTrigger>();
                var rEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                rEnter.callback.AddListener(_ =>
                {
                    int di = _mmLoadRunScroll + rCap;
                    _mmRunHover = (di >= 0 && di < _mmLoadRuns.Count) ? rCap : -1;
                    UpdateMMLoadHoverBorders();
                });
                runTrig.triggers.Add(rEnter);
                var rExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                rExit.callback.AddListener(_ => { _mmRunHover = -1; UpdateMMLoadHoverBorders(); });
                runTrig.triggers.Add(rExit);

                _mmRunHoverBorders[i] = BuildHoverBorderStrips(runList.transform, cy, runRowH);
            }
        }

        // ── Save list rows (right column, top) ───────────────────────────────────

        private void BuildSaveListRows(Transform panel, float splitX)
        {
            var saveList = CreateUIObject("MMSaveList", panel);
            var svR = saveList.GetComponent<RectTransform>();
            svR.anchorMin = new Vector2(splitX + 0.02f, 0.51f); svR.anchorMax = new Vector2(0.98f, 0.81f);
            svR.pivot = new Vector2(0f, 1f); svR.sizeDelta = Vector2.zero;
            svR.anchoredPosition = Vector2.zero;

            _mmSavePills        = new Graphic[MM_SAVE_ROWS];
            _mmSaveTexts        = new TextMeshProUGUI[MM_SAVE_ROWS];
            _mmSaveHoverBorders = new Graphic[MM_SAVE_ROWS][];

            const float svRowH = 31f;
            const float svGap  = 3f;

            for (int i = 0; i < MM_SAVE_ROWS; i++)
            {
                float cy = -i * (svRowH + svGap);

                _mmSavePills[i] = BuildSlotFill($"SvPill_{i}", saveList.transform, cy, svRowH);

                var txtGo = CreateUIObject($"SvTxt_{i}", saveList.transform);
                var txtR = txtGo.GetComponent<RectTransform>();
                txtR.anchorMin = new Vector2(0f, 1f); txtR.anchorMax = new Vector2(1f, 1f);
                txtR.pivot = new Vector2(0f, 1f);
                txtR.anchoredPosition = new Vector2(12f, cy); txtR.sizeDelta = new Vector2(-12f, svRowH);
                _mmSaveTexts[i] = MenuTypography.Label(txtGo, Style, string.Empty,
                                                       Style.detailFontSize + 2f, Style.TextPrimary,
                                                       TextAlignmentOptions.Left);

                var svHitGo = CreateUIObject($"SvHit_{i}", saveList.transform);
                var svHitRt = svHitGo.GetComponent<RectTransform>();
                svHitRt.anchorMin = new Vector2(0f, 1f); svHitRt.anchorMax = new Vector2(1f, 1f);
                svHitRt.pivot = new Vector2(0.5f, 1f);
                svHitRt.anchoredPosition = new Vector2(0f, cy); svHitRt.sizeDelta = new Vector2(0f, svRowH);
                var svHitImg = svHitGo.AddComponent<Image>(); svHitImg.color = Color.clear;
                var svBtn = svHitGo.AddComponent<Button>(); svBtn.targetGraphic = svHitImg;
                int sCap = i;
                svBtn.onClick.AddListener(() =>
                {
                    if (_mmLoadRunSel < 0 || _mmLoadRunSel >= _mmLoadRuns.Count) return;
                    if (sCap >= _mmLoadRuns[_mmLoadRunSel].saves.Count) return;
                    _mmLoadSaveSel = sCap; UpdateMMLoadVisuals();
                });
                var svTrig = svHitGo.AddComponent<EventTrigger>();
                var sEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                sEnter.callback.AddListener(_ =>
                {
                    int saveCount = (_mmLoadRunSel >= 0 && _mmLoadRunSel < _mmLoadRuns.Count)
                        ? _mmLoadRuns[_mmLoadRunSel].saves.Count : 0;
                    _mmSaveHover = (sCap < saveCount) ? sCap : -1;
                    UpdateMMLoadHoverBorders();
                });
                svTrig.triggers.Add(sEnter);
                var sExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                sExit.callback.AddListener(_ => { _mmSaveHover = -1; UpdateMMLoadHoverBorders(); });
                svTrig.triggers.Add(sExit);

                _mmSaveHoverBorders[i] = BuildHoverBorderStrips(saveList.transform, cy, svRowH);
            }
        }

        // ── Detail panel (right column, bottom) ──────────────────────────────────

        private void BuildDetailPanel(Transform panel, float splitX)
        {
            var detC = CreateUIObject("MMSaveDetails", panel);
            var dcR = detC.GetComponent<RectTransform>();
            dcR.anchorMin = new Vector2(splitX + 0.02f, 0.11f); dcR.anchorMax = new Vector2(0.98f, 0.49f);
            dcR.pivot = new Vector2(0f, 1f); dcR.sizeDelta = Vector2.zero;
            dcR.anchoredPosition = Vector2.zero;

            var detGo = CreateUIObject("MMDetailText", detC.transform);
            var detRt = detGo.GetComponent<RectTransform>();
            detRt.anchorMin = Vector2.zero; detRt.anchorMax = Vector2.one;
            detRt.sizeDelta = Vector2.zero; detRt.anchoredPosition = Vector2.zero;
            _mmLoadDetailText = MenuTypography.Label(detGo, Style, MenuText.LoadPickOne,
                                                     Style.detailFontSize + 1f, Style.TextPrimary,
                                                     TextAlignmentOptions.TopLeft);
            _mmLoadDetailText.enableWordWrapping = true;
        }

        // ── Hover border helpers ──────────────────────────────────────────────────

        /// <summary>What a shown hover outline is multiplied by. The gold lives in the graphic.</summary>
        private static Color HoverBorderColor => Color.white;

        /// <summary>
        /// A slot's selection: the loading bar FILLED at row size (gold light profile, bright
        /// border, accent core at its start). Shown by colour — white shows, clear hides — so the
        /// visibility contract the fixtures pin is unchanged from the sprite it replaces.
        /// </summary>
        private Graphic BuildSlotFill(string name, Transform parent, float cy, float rowH)
        {
            var fill = Frontend.FrontendFillGraphic.Create(parent, name);
            fill.Profile = Frontend.FrontendFillProfile.Row;
            fill.Border = true;
            fill.StartCore = true;
            fill.Tint = PillColor;
            var rt = fill.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, cy); rt.sizeDelta = new Vector2(0f, rowH);
            fill.color = Color.clear;
            return fill;
        }

        /// <summary>
        /// The hover outline: the bar's groove, empty. Dimmer than the selection fill on purpose:
        /// hovering says "this is what you would pick", selecting says "this is what you picked".
        /// One graphic in an array, so the update loop that used to walk four strips still walks.
        /// </summary>
        private Graphic[] BuildHoverBorderStrips(Transform parent, float cy, float rowH)
        {
            var hover = Frontend.FrontendHoverGraphic.Create(parent, "Hover");
            hover.Tint = Style.Gold;
            var rt = hover.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, cy); rt.sizeDelta = new Vector2(0f, rowH);
            hover.color = Color.clear;
            // Under the row's text and face: its faint recess would otherwise dim the label.
            rt.SetAsFirstSibling();
            return new Graphic[] { hover };
        }
    }
}
