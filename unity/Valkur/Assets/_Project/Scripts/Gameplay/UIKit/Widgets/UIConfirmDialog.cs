using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UIKit
{
    /// <summary>
    /// Modal confirmation dialog with a dim backdrop, accent-bordered panel,
    /// title, message and Confirm/Cancel button row. Returns inert objects;
    /// the caller wires up <c>onClick</c> handlers and toggles
    /// <c>root.SetActive</c>.
    /// </summary>
    public static class UIConfirmDialog
    {
        public static (GameObject root, TextMeshProUGUI message, Button confirmBtn, Button cancelBtn)
            Make(Transform parent, string title)
        {
            var overlay = UIFactory.CreateUI("ConfirmDialog", parent);
            var rt = overlay.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            var overlayImg = overlay.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.6f);

            var panel = UIFactory.CreateUI("Panel", overlay.transform);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.3f, 0.35f);
            panelRt.anchorMax = new Vector2(0.7f, 0.65f);
            panelRt.sizeDelta = Vector2.zero;
            panel.AddComponent<Image>().color = UITheme.BG_PANEL;
            panel.AddComponent<Outline>().effectColor = UITheme.ACCENT;
            UIPanel.AddVLG(panel, 16, 12f);

            UILabel.BuildSectionHeader(panel.transform, title);
            var msg = UILabel.Add(panel.transform, "", 13f, TextAlignmentOptions.Center);
            msg.color = UITheme.TEXT_PRIMARY;

            var btnRow = UIFactory.CreateUI("BtnRow", panel.transform);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f; hlg.childForceExpandWidth = true;

            var confirmBtn = UIButton.MakeDanger(btnRow.transform, "Confirm", null, 32f);
            var cancelBtn  = UIButton.Make(btnRow.transform, "Cancel", null, 32f);

            overlay.SetActive(false);
            return (overlay, msg, confirmBtn, cancelBtn);
        }
    }
}
