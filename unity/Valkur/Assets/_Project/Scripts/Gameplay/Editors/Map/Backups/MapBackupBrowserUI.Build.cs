using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.Gameplay.MapEditor.Backups
{
    public partial class MapBackupBrowserUI
    {
        private void BuildUI()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200; // above pause menu
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 800f);
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = MakeStretch("Root", canvasGo.transform);
            _root.AddComponent<Image>().color = OverlayBg;

            // Main panel (centered, fixed size).
            var panel = MakeRect("Panel", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(1100f, 660f);
            panel.AddComponent<Image>().color = PanelBg;
            var ol = panel.AddComponent<Outline>();
            ol.effectColor    = Accent;
            ol.effectDistance = new Vector2(2f, 2f);

            BuildHeader(panel.transform);
            BuildBody(panel.transform);
            BuildStatusBar(panel.transform);
            BuildDeleteDialog(canvasGo.transform);
        }

        private void BuildHeader(Transform panel)
        {
            var bar = MakeRect("Header", panel, new Vector2(0f, 1f), new Vector2(1f, 1f));
            var rt = bar.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 56f);
            bar.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.14f, 1f);

            var title = AddText(bar.transform, "MAP BACKUPS", 22f, Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0f);
            titleRt.anchorMax = new Vector2(0f, 1f);
            titleRt.pivot     = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(20f, 0f);
            titleRt.sizeDelta = new Vector2(300f, 0f);

            // Create-backup button (left of close)
            var createBtn = AddButton(bar.transform, "+ Create backup", 200f, 36f, BtnNormal, BtnHover, () =>
            {
                var manifest = _store.CreateSnapshot(GuessActiveSlot(), "Manual snapshot",
                                                     MapBackupSchema.KindManual);
                if (manifest != null)
                {
                    SetStatus($"Created snapshot '{manifest.id}' ({MapBackupStore.FormatBytes(manifest.totalBytes)}).");
                    RefreshList();
                    SelectBackup(manifest.id);
                }
                else SetStatus("Snapshot failed — see console.");
            });
            var crRt = createBtn.GetComponent<RectTransform>();
            crRt.anchorMin = new Vector2(1f, 0.5f);
            crRt.anchorMax = new Vector2(1f, 0.5f);
            crRt.pivot     = new Vector2(1f, 0.5f);
            crRt.anchoredPosition = new Vector2(-130f, 0f);

            // Close button
            var closeBtn = AddButton(bar.transform, "Close", 100f, 36f, BtnNormal, BtnHover, Hide);
            var clRt = closeBtn.GetComponent<RectTransform>();
            clRt.anchorMin = new Vector2(1f, 0.5f);
            clRt.anchorMax = new Vector2(1f, 0.5f);
            clRt.pivot     = new Vector2(1f, 0.5f);
            clRt.anchoredPosition = new Vector2(-20f, 0f);
        }

        private void BuildBody(Transform panel)
        {
            var body = MakeRect("Body", panel, new Vector2(0f, 0f), new Vector2(1f, 1f));
            var rt = body.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(10f, 38f);  // leave room for status bar
            rt.offsetMax = new Vector2(-10f, -60f); // leave room for header

            // Left: list of backups
            var leftCol = MakeRect("LeftCol", body.transform, new Vector2(0f, 0f), new Vector2(0.45f, 1f));
            leftCol.GetComponent<RectTransform>().offsetMin = new Vector2(0f, 0f);
            leftCol.GetComponent<RectTransform>().offsetMax = new Vector2(-6f, 0f);
            leftCol.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 1f);
            BuildBackupList(leftCol.transform);

            // Right: details
            var rightCol = MakeRect("RightCol", body.transform, new Vector2(0.45f, 0f), new Vector2(1f, 1f));
            rightCol.GetComponent<RectTransform>().offsetMin = new Vector2(6f, 0f);
            rightCol.GetComponent<RectTransform>().offsetMax = new Vector2(0f, 0f);
            rightCol.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 1f);
            BuildDetails(rightCol.transform);
        }

        private void BuildBackupList(Transform parent)
        {
            var hdr = AddText(parent, "BACKUPS", 13f, TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            var hdrRt = hdr.rectTransform;
            hdrRt.anchorMin = new Vector2(0f, 1f); hdrRt.anchorMax = new Vector2(1f, 1f);
            hdrRt.pivot = new Vector2(0f, 1f);
            hdrRt.anchoredPosition = new Vector2(12f, -8f);
            hdrRt.sizeDelta = new Vector2(0f, 20f);

            var scrollGo = MakeRect("Scroll", parent, new Vector2(0f, 0f), new Vector2(1f, 1f));
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.offsetMin = new Vector2(8f, 8f);
            scrollRt.offsetMax = new Vector2(-8f, -32f);

            var viewport = MakeRect("Viewport", scrollGo.transform, Vector2.zero, Vector2.one);
            viewport.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot     = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta        = Vector2.zero;
            _listContent = contentRt;

            var v = contentGo.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(4, 4, 4, 4);
            v.spacing = 4f;
            v.childControlWidth = true; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childForceExpandHeight = false;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content  = contentRt;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.scrollSensitivity = 24f;
        }

        private void BuildDetails(Transform parent)
        {
            _detailHeader = AddText(parent, "Select a backup", 16f, Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            var hRt = _detailHeader.rectTransform;
            hRt.anchorMin = new Vector2(0f, 1f); hRt.anchorMax = new Vector2(1f, 1f);
            hRt.pivot = new Vector2(0f, 1f);
            hRt.anchoredPosition = new Vector2(12f, -8f);
            hRt.sizeDelta = new Vector2(-12f, 24f);

            _detailBody = AddText(parent, "", 12f, TextPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal);
            var bRt = _detailBody.rectTransform;
            bRt.anchorMin = new Vector2(0f, 1f); bRt.anchorMax = new Vector2(1f, 1f);
            bRt.pivot = new Vector2(0f, 1f);
            bRt.anchoredPosition = new Vector2(12f, -38f);
            bRt.sizeDelta = new Vector2(-24f, 100f);
            _detailBody.enableWordWrapping = true;

            // File list inside the details column.
            var filesHdr = AddText(parent, "FILES", 12f, TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            var fhRt = filesHdr.rectTransform;
            fhRt.anchorMin = new Vector2(0f, 1f); fhRt.anchorMax = new Vector2(1f, 1f);
            fhRt.pivot = new Vector2(0f, 1f);
            fhRt.anchoredPosition = new Vector2(12f, -148f);
            fhRt.sizeDelta = new Vector2(-24f, 18f);

            var filesScroll = MakeRect("FilesScroll", parent, new Vector2(0f, 0f), new Vector2(1f, 1f));
            var fsRt = filesScroll.GetComponent<RectTransform>();
            fsRt.offsetMin = new Vector2(12f, 60f);   // leave room for action row
            fsRt.offsetMax = new Vector2(-12f, -170f);

            var viewport = MakeRect("Viewport", filesScroll.transform, Vector2.zero, Vector2.one);
            viewport.AddComponent<RectMask2D>();
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.30f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot     = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = Vector2.zero;
            _detailFilesContent = contentRt;

            var v = contentGo.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(8, 8, 6, 6);
            v.spacing = 1f;
            v.childControlWidth = true; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childForceExpandHeight = false;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = filesScroll.AddComponent<ScrollRect>();
            scroll.viewport   = viewport.GetComponent<RectTransform>();
            scroll.content    = contentRt;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.scrollSensitivity = 24f;

            // Action row at the bottom of the right column.
            var actionRow = MakeRect("Actions", parent, new Vector2(0f, 0f), new Vector2(1f, 0f));
            var arRt = actionRow.GetComponent<RectTransform>();
            arRt.pivot = new Vector2(0.5f, 0f);
            arRt.anchoredPosition = new Vector2(0f, 12f);
            arRt.sizeDelta = new Vector2(-24f, 36f);

            _restoreBtn = AddButton(actionRow.transform, "Restore", 140f, 36f, BtnNormal, BtnHover, OnRestoreClicked);
            var rRt = _restoreBtn.GetComponent<RectTransform>();
            rRt.anchorMin = new Vector2(0f, 0.5f); rRt.anchorMax = new Vector2(0f, 0.5f);
            rRt.pivot = new Vector2(0f, 0.5f);
            rRt.anchoredPosition = new Vector2(12f, 0f);

            _deleteBtn = AddButton(actionRow.transform, "Delete…", 140f, 36f, BtnDanger, BtnDangerH, OnDeleteClicked);
            var dRt = _deleteBtn.GetComponent<RectTransform>();
            dRt.anchorMin = new Vector2(1f, 0.5f); dRt.anchorMax = new Vector2(1f, 0.5f);
            dRt.pivot = new Vector2(1f, 0.5f);
            dRt.anchoredPosition = new Vector2(-12f, 0f);

            SetActionButtonsEnabled(false);
        }

        private void BuildStatusBar(Transform panel)
        {
            var bar = MakeRect("Status", panel, new Vector2(0f, 0f), new Vector2(1f, 0f));
            var rt = bar.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 28f);
            bar.AddComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 1f);

            _statusLine = AddText(bar.transform, "", 12f, TextDim, TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
            var sRt = _statusLine.rectTransform;
            sRt.anchorMin = new Vector2(0f, 0f); sRt.anchorMax = new Vector2(1f, 1f);
            sRt.offsetMin = new Vector2(12f, 0f); sRt.offsetMax = new Vector2(-12f, 0f);
        }
    }
}
