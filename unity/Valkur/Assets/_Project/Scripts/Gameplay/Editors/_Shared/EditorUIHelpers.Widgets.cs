using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors
{
    public static partial class EditorUIHelpers
    {
        public static (Button button, Image icon, TextMeshProUGUI label) MakeSlotButton(
            Transform parent, string text, float size = 64f,
            UnityEngine.Events.UnityAction onClick = null)
            => UIButton.MakeSlot(parent, text, size, onClick);

        public static TextMeshProUGUI MakeTitleBar(Transform parent, string title, float height = 36f)
            => UILabel.MakeTitleBar(parent, title, height);

        public static (ScrollRect scroll, RectTransform content) MakeGridPicker(
            Transform parent, string name, int columns = 5, float cellSize = 64f, float spacing = 4f)
            => UIGridPicker.Make(parent, name, columns, cellSize, spacing);

        /// <summary>
        /// Responsive grid picker — cell size and column count reflow as the
        /// host panel is resized. See <see cref="UIGridPicker.MakeResponsive"/>.
        /// </summary>
        public static (ScrollRect scroll, RectTransform content, GridAutoSize autoSize) MakeResponsiveGridPicker(
            Transform parent, string name,
            float minCellSize = 56f, float maxCellSize = 96f, float spacing = 4f)
            => UIGridPicker.MakeResponsive(parent, name, minCellSize, maxCellSize, spacing);

        public static VerticalLayoutGroup AddVLG(GameObject panel, int pad = 8, float spacing = 6f)
            => UIPanel.AddVLG(panel, pad, spacing);

        public static TextMeshProUGUI MakeStatusText(Transform parent)
            => UILabel.MakeStatus(parent);

        public static TMP_InputField MakeInputField(Transform parent, string placeholder = "...",
            float height = 30f)
            => UIInputField.MakeWithPlaceholder(parent, placeholder, height);

        public static Scrollbar AddVerticalScrollbar(ScrollRect scrollRect, float sbWidth = 12f)
            => UIFactory.AddVerticalScrollbar(scrollRect, sbWidth);

        public static (GameObject root, TextMeshProUGUI message, Button confirmBtn, Button cancelBtn)
            MakeConfirmDialog(Transform parent, string title)
            => UIConfirmDialog.Make(parent, title);

        /// <summary>
        /// Ensures a solid-color backdrop sits behind <paramref name="icon"/>.
        /// Used for HUD spell icons whose source PNGs have a transparent
        /// background — without a backdrop they read as floating pixels on
        /// the slot/preview surface. Idempotent: re-uses the same backdrop
        /// child on subsequent calls. The backdrop tracks the icon's rect
        /// (anchors, offsets, pivot) so panels can resize without breakage.
        /// </summary>
        public static Image EnsureIconBackdrop(Image icon, Color? color = null)
        {
            if (icon == null) return null;
            var parent = icon.transform.parent;
            if (parent == null) return null;

            const string BackdropName = "IconBackdrop";
            var existing = parent.Find(BackdropName);
            Image backdrop = existing != null ? existing.GetComponent<Image>() : null;

            if (backdrop == null)
            {
                var go = new GameObject(BackdropName, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, worldPositionStays: false);
                backdrop = go.GetComponent<Image>();
                backdrop.raycastTarget = false;
            }

            // Match the icon's rect + sit immediately behind it in sibling
            // order so it doesn't intercept clicks or hide the icon.
            var iconRt = (RectTransform)icon.transform;
            var rt = (RectTransform)backdrop.transform;
            rt.anchorMin = iconRt.anchorMin;
            rt.anchorMax = iconRt.anchorMax;
            rt.pivot = iconRt.pivot;
            rt.anchoredPosition = iconRt.anchoredPosition;
            rt.sizeDelta = iconRt.sizeDelta;
            backdrop.transform.SetSiblingIndex(icon.transform.GetSiblingIndex());

            backdrop.color = color ?? Color.black;
            backdrop.enabled = true;
            return backdrop;
        }

        /// <summary>
        /// Hides the backdrop installed by <see cref="EnsureIconBackdrop"/>
        /// without destroying it (cheap toggle for empty slots / fallbacks).
        /// </summary>
        public static void HideIconBackdrop(Image icon)
        {
            if (icon == null) return;
            var parent = icon.transform.parent;
            if (parent == null) return;
            var existing = parent.Find("IconBackdrop");
            if (existing == null) return;
            var img = existing.GetComponent<Image>();
            if (img != null) img.enabled = false;
        }
    }
}
