using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

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

            _mmRunPills        = new Image[MM_RUN_ROWS];
            _mmRunBars         = new Image[MM_RUN_ROWS];
            _mmRunTexts        = new TextMeshProUGUI[MM_RUN_ROWS];
            _mmRunFaceImages   = new RawImage[MM_RUN_ROWS];
            _mmRunHoverBorders = new Image[MM_RUN_ROWS][];

            const float runRowH = 37f;
            const float runGap  = 3f;

            for (int i = 0; i < MM_RUN_ROWS; i++)
            {
                float cy = -i * (runRowH + runGap);

                var pillGo = CreateUIObject($"RnPill_{i}", runList.transform);
                var pRt = pillGo.GetComponent<RectTransform>();
                pRt.anchorMin = new Vector2(0f, 1f); pRt.anchorMax = new Vector2(1f, 1f);
                pRt.pivot = new Vector2(0.5f, 1f);
                pRt.anchoredPosition = new Vector2(0f, cy); pRt.sizeDelta = new Vector2(0f, runRowH);
                _mmRunPills[i] = pillGo.AddComponent<Image>(); _mmRunPills[i].color = Color.clear;

                var barGo = CreateUIObject($"RnBar_{i}", runList.transform);
                var bRt = barGo.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0f, 1f); bRt.anchorMax = new Vector2(0f, 1f);
                bRt.pivot = new Vector2(0f, 1f);
                bRt.anchoredPosition = new Vector2(0f, cy); bRt.sizeDelta = new Vector2(4f, runRowH);
                _mmRunBars[i] = barGo.AddComponent<Image>(); _mmRunBars[i].color = Color.clear;

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
                _mmRunTexts[i] = txtGo.AddComponent<TextMeshProUGUI>();
                _mmRunTexts[i].text = ""; _mmRunTexts[i].fontSize = 12f;
                _mmRunTexts[i].alignment = TextAlignmentOptions.Left; _mmRunTexts[i].color = TextNormal;
                _mmRunTexts[i].enableWordWrapping = false;

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

            _mmSavePills        = new Image[MM_SAVE_ROWS];
            _mmSaveBars         = new Image[MM_SAVE_ROWS];
            _mmSaveTexts        = new TextMeshProUGUI[MM_SAVE_ROWS];
            _mmSaveHoverBorders = new Image[MM_SAVE_ROWS][];

            const float svRowH = 31f;
            const float svGap  = 3f;

            for (int i = 0; i < MM_SAVE_ROWS; i++)
            {
                float cy = -i * (svRowH + svGap);

                var pillGo = CreateUIObject($"SvPill_{i}", saveList.transform);
                var pRt = pillGo.GetComponent<RectTransform>();
                pRt.anchorMin = new Vector2(0f, 1f); pRt.anchorMax = new Vector2(1f, 1f);
                pRt.pivot = new Vector2(0.5f, 1f);
                pRt.anchoredPosition = new Vector2(0f, cy); pRt.sizeDelta = new Vector2(0f, svRowH);
                _mmSavePills[i] = pillGo.AddComponent<Image>(); _mmSavePills[i].color = Color.clear;

                var barGo = CreateUIObject($"SvBar_{i}", saveList.transform);
                var bRt = barGo.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0f, 1f); bRt.anchorMax = new Vector2(0f, 1f);
                bRt.pivot = new Vector2(0f, 1f);
                bRt.anchoredPosition = new Vector2(0f, cy); bRt.sizeDelta = new Vector2(4f, svRowH);
                _mmSaveBars[i] = barGo.AddComponent<Image>(); _mmSaveBars[i].color = Color.clear;

                var txtGo = CreateUIObject($"SvTxt_{i}", saveList.transform);
                var txtR = txtGo.GetComponent<RectTransform>();
                txtR.anchorMin = new Vector2(0f, 1f); txtR.anchorMax = new Vector2(1f, 1f);
                txtR.pivot = new Vector2(0f, 1f);
                txtR.anchoredPosition = new Vector2(12f, cy); txtR.sizeDelta = new Vector2(-12f, svRowH);
                _mmSaveTexts[i] = txtGo.AddComponent<TextMeshProUGUI>();
                _mmSaveTexts[i].text = ""; _mmSaveTexts[i].fontSize = 14f;
                _mmSaveTexts[i].alignment = TextAlignmentOptions.Left; _mmSaveTexts[i].color = TextNormal;
                _mmSaveTexts[i].enableWordWrapping = false;

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
            _mmLoadDetailText = detGo.AddComponent<TextMeshProUGUI>();
            _mmLoadDetailText.fontSize = 14f;
            _mmLoadDetailText.alignment = TextAlignmentOptions.TopLeft;
            _mmLoadDetailText.color = TextNormal;
            _mmLoadDetailText.text = "Select a save.";
        }

        // ── Hover border helpers ──────────────────────────────────────────────────

        private static readonly Color HoverBorderColor = new Color(1f, 0.84f, 0f, 0.85f);

        /// <summary>Creates 4 thin strip Images around a row rect to form an outline.</summary>
        private Image[] BuildHoverBorderStrips(Transform parent, float cy, float rowH)
        {
            const float T = 2f;
            var strips = new Image[4];
            strips[0] = MakeBorderStrip("BT", parent, new Vector2(0f,1f), new Vector2(1f,1f),
                new Vector2(0.5f,1f), new Vector2(0f, cy),        new Vector2(0f, T));
            strips[1] = MakeBorderStrip("BB", parent, new Vector2(0f,1f), new Vector2(1f,1f),
                new Vector2(0.5f,1f), new Vector2(0f, cy-rowH+T), new Vector2(0f, T));
            strips[2] = MakeBorderStrip("BL", parent, new Vector2(0f,1f), new Vector2(0f,1f),
                new Vector2(0f,1f),   new Vector2(0f, cy),        new Vector2(T, rowH));
            strips[3] = MakeBorderStrip("BR", parent, new Vector2(1f,1f), new Vector2(1f,1f),
                new Vector2(1f,1f),   new Vector2(0f, cy),        new Vector2(T, rowH));
            return strips;
        }

        private Image MakeBorderStrip(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.pivot = pivot; rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = false;
            return img;
        }
    }
}
