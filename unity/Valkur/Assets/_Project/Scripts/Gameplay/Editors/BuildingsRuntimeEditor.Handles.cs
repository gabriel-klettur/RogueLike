using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {

        private void BuildZBadges()
        {
            _zTopBadgeRt = BuildZBadge("ZTopBadge",
                () => AdjustZ(_activeBuilding, bottom: false, delta: -1),
                () => AdjustZ(_activeBuilding, bottom: false, delta: +1),
                out _zTopBadgeTmp);
            _zBotBadgeRt = BuildZBadge("ZBotBadge",
                () => AdjustZ(_activeBuilding, bottom: true, delta: -1),
                () => AdjustZ(_activeBuilding, bottom: true, delta: +1),
                out _zBotBadgeTmp);
        }

        private RectTransform BuildZBadge(string name, Action onMinus, Action onPlus,
            out TextMeshProUGUI valueTmp)
        {
            var go = EditorUIHelpers.CreateUI(name, _root.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(100f, 22f);  // updated each frame in UpdateZBadges

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.07f, 0.10f, 0.88f);

            var ol = go.AddComponent<Outline>();
            ol.effectColor    = new Color(0.90f, 0.76f, 0.38f, 0.50f); // gold matches selection frame
            ol.effectDistance = new Vector2(1f, -1f);

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding               = new RectOffset(2, 2, 2, 2);
            hlg.spacing               = 1f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleCenter;

            // [−] button
            var minusGo  = EditorUIHelpers.CreateUI("Minus", go.transform);
            minusGo.AddComponent<LayoutElement>().preferredWidth = 18f;
            var minusImg = minusGo.AddComponent<Image>();
            minusImg.color = EditorUIHelpers.BTN_NORMAL;
            var minusBtn = minusGo.AddComponent<Button>();
            var mc = minusBtn.colors;
            mc.normalColor = EditorUIHelpers.BTN_NORMAL; mc.highlightedColor = EditorUIHelpers.BTN_HOVER;
            mc.pressedColor = EditorUIHelpers.BTN_ACTIVE; mc.fadeDuration = 0.08f;
            minusBtn.colors = mc; minusBtn.targetGraphic = minusImg;
            minusBtn.onClick.AddListener(() => { if (_activeBuilding != null) onMinus(); });
            EditorUIHelpers.AddCenteredText(minusGo.transform, "\u2212", 12f, FontStyles.Bold, EditorUIHelpers.TEXT_PRIMARY);

            // Z: N label
            var valGo = EditorUIHelpers.CreateUI("Val", go.transform);
            valGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            valueTmp           = valGo.AddComponent<TextMeshProUGUI>();
            valueTmp.text      = "Z: 0";
            valueTmp.fontSize  = 10f;
            valueTmp.fontStyle = FontStyles.Bold;
            valueTmp.color     = EditorUIHelpers.ACCENT;
            valueTmp.alignment = TextAlignmentOptions.Center;

            // [+] button
            var plusGo  = EditorUIHelpers.CreateUI("Plus", go.transform);
            plusGo.AddComponent<LayoutElement>().preferredWidth = 18f;
            var plusImg = plusGo.AddComponent<Image>();
            plusImg.color = EditorUIHelpers.BTN_NORMAL;
            var plusBtn = plusGo.AddComponent<Button>();
            var pc = plusBtn.colors;
            pc.normalColor = EditorUIHelpers.BTN_NORMAL; pc.highlightedColor = EditorUIHelpers.BTN_HOVER;
            pc.pressedColor = EditorUIHelpers.BTN_ACTIVE; pc.fadeDuration = 0.08f;
            plusBtn.colors = pc; plusBtn.targetGraphic = plusImg;
            plusBtn.onClick.AddListener(() => { if (_activeBuilding != null) onPlus(); });
            EditorUIHelpers.AddCenteredText(plusGo.transform, "+", 12f, FontStyles.Bold, EditorUIHelpers.TEXT_PRIMARY);

            go.SetActive(false);
            return rt;
        }

        private void UpdateZBadges()
        {
            if (_zTopBadgeRt == null || _zBotBadgeRt == null) return;
            bool show = _activeBuilding != null && !_removeMode;
            _zTopBadgeRt.gameObject.SetActive(show);
            _zBotBadgeRt.gameObject.SetActive(show);
            if (!show) return;

            if (!_activeBuilding.TryGetWorldRect(out var rect))
            {
                _zTopBadgeRt.gameObject.SetActive(false);
                _zBotBadgeRt.gameObject.SetActive(false);
                return;
            }
            var cam = Camera.main;
            if (cam == null) return;

            // Building canvas-space width for proportional badge sizing
            Vector3 screenTR = cam.WorldToScreenPoint(new Vector3(rect.xMax, rect.yMax, 0f));
            Vector3 screenTL = cam.WorldToScreenPoint(new Vector3(rect.xMin, rect.yMax, 0f));
            float   canvasW  = Mathf.Abs(ScreenToCanvasPos(screenTR).x - ScreenToCanvasPos(screenTL).x);
            float   badgeW   = Mathf.Clamp(canvasW * 0.65f, 60f, 160f);
            float   badgeH   = Mathf.Clamp(canvasW * 0.08f, 18f, 26f);
            float   inset    = badgeH * 0.5f + 4f;  // distance from top/bottom edge to badge center

            // Horizontal center of building in canvas space
            Vector3 screenBL  = cam.WorldToScreenPoint(new Vector3(rect.xMin, rect.yMin, 0f));
            float   centerX   = (ScreenToCanvasPos(screenTR).x + ScreenToCanvasPos(screenTL).x) * 0.5f;

            // Top badge: just inside the top edge
            Vector2 canvasTop = ScreenToCanvasPos(cam.WorldToScreenPoint(new Vector3(rect.center.x, rect.yMax, 0f)));
            _zTopBadgeRt.sizeDelta        = new Vector2(badgeW, badgeH);
            _zTopBadgeRt.anchoredPosition = new Vector2(centerX, canvasTop.y - inset);

            // Bottom badge: just inside the bottom edge
            Vector2 canvasBot = ScreenToCanvasPos(cam.WorldToScreenPoint(new Vector3(rect.center.x, rect.yMin, 0f)));
            _zBotBadgeRt.sizeDelta        = new Vector2(badgeW, badgeH);
            _zBotBadgeRt.anchoredPosition = new Vector2(centerX, canvasBot.y + inset);

            // Update Z values
            if (_zTopBadgeTmp != null) _zTopBadgeTmp.text = $"Z: {_activeBuilding.ZTopOffset}";
            if (_zBotBadgeTmp != null) _zBotBadgeTmp.text = $"Z: {_activeBuilding.ZBottomOffset}";
        }

        private void BuildTutorial()
        {
            _tutorialRoot = EditorUIHelpers.MakePanel("Tutorial", _root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 0), new Vector2(520f, 240f));
            var vlg = _tutorialRoot.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 14, 14);
            vlg.spacing = 8f; vlg.childForceExpandWidth = true;

            EditorUIHelpers.MakeTitleBar(_tutorialRoot.transform, "BUILDINGS TUTORIAL");

            _tutorialStepLabel = EditorUIHelpers.AddLabel(_tutorialRoot.transform, "", 14f);
            _tutorialStepLabel.fontStyle = FontStyles.Bold;
            _tutorialStepLabel.color = EditorUIHelpers.ACCENT;

            var bodyGo = EditorUIHelpers.CreateUI("Body", _tutorialRoot.transform);
            var bodyLe = bodyGo.AddComponent<LayoutElement>();
            bodyLe.flexibleHeight = 1f;
            _tutorialBodyTmp = bodyGo.AddComponent<TextMeshProUGUI>();
            _tutorialBodyTmp.fontSize = 12f;
            _tutorialBodyTmp.color = EditorUIHelpers.TEXT_PRIMARY;
            _tutorialBodyTmp.alignment = TextAlignmentOptions.TopLeft;
            _tutorialBodyTmp.enableWordWrapping = true;

            // Nav row
            var nav = EditorUIHelpers.CreateUI("Nav", _tutorialRoot.transform);
            nav.AddComponent<LayoutElement>().preferredHeight = 32f;
            var hlg = nav.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f; hlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeButton(nav.transform, "Prev",  () => StepTutorial(-1), 28f, 12f);
            EditorUIHelpers.MakeButton(nav.transform, "Next",  () => StepTutorial(+1), 28f, 12f);
            EditorUIHelpers.MakeButton(nav.transform, "Close", () => _tutorialRoot.SetActive(false), 28f, 12f);

            _tutorialStep = 0;
            RefreshTutorial();
            _tutorialRoot.SetActive(false);
        }

        private void BuildConfirmModal()
        {
            _confirmModal = EditorUIHelpers.MakePanel("ConfirmModal", _root.transform,
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            var bgImg = _confirmModal.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 140f / 255f);

            // Inner panel
            var inner = EditorUIHelpers.MakePanel("Inner", _confirmModal.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(520f, 200f));
            var vlg = inner.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 18, 18);
            vlg.spacing = 12f; vlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeTitleBar(inner.transform, "CONFIRM DELETE");

            _confirmText = EditorUIHelpers.AddLabel(inner.transform, "?", 13f);
            _confirmText.color = EditorUIHelpers.TEXT_PRIMARY;
            _confirmText.alignment = TextAlignmentOptions.MidlineLeft;

            var btnRow = EditorUIHelpers.CreateUI("Btns", inner.transform);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f; hlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeDangerButton(btnRow.transform, "Eliminar",
                () => { var cb = _pendingConfirmYes; HideConfirm(); cb?.Invoke(); }, 32f);
            EditorUIHelpers.MakeButton(btnRow.transform, "Cancelar", () => HideConfirm(), 32f, 12f);

            _confirmModal.SetActive(false);
        }

        private void CreatePerfProbe()
        {
            var probeGo = new GameObject("BuildingsPerfProbe");
            probeGo.transform.SetParent(transform);
            _perfProbe = probeGo.AddComponent<BuildingsPerfProbe>();
            _perfProbe.Visible = false;
            Debug.Log("[BuildingsEditor] Perf probe created (toggle via PERF button in menu bar).");
        }

        private void TogglePerfProbe()
        {
            if (_perfProbe == null) return;
            _perfProbe.Visible = !_perfProbe.Visible;
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.PerfProbeMenuBtnImg, _uiRefs.PerfProbeMenuBtnTmp, _perfProbe.Visible);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  PICKER + MODE
        // ──────────────────────────────────────────────────────────────────────────

    }
}