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

        /// <summary>Default frame thickness, in pixels, for <see cref="MakeSelectionBorder"/>.</summary>
        public const float SELECTION_BORDER_THICKNESS = 4f;

        /// <summary>
        /// Draws a thick frame around <paramref name="target"/> to mark it as the
        /// selected slot, and returns the container so callers can keep or destroy it.
        ///
        /// Picker grids normally signal selection by tinting the slot background with
        /// <see cref="SLOT_SELECTED"/>. That is invisible whenever the slot's content
        /// — an icon, or a live RenderTexture preview — covers the whole cell, so
        /// those grids draw this frame on top instead.
        ///
        /// Built from four stretched strips rather than a sprite so it needs no art
        /// and stays crisp at any cell size. The container is pushed to the end of the
        /// sibling list so it renders above the slot's content, and nothing in it
        /// takes raycasts, so the slot button still receives clicks normally.
        /// </summary>
        public static GameObject MakeSelectionBorder(
            RectTransform target,
            float thickness = SELECTION_BORDER_THICKNESS,
            Color? color = null,
            string name = "SelectionBorder")
        {
            if (target == null) return null;

            Color c = color ?? SELECTION_BORDER;

            var root = CreateUI(name, target);
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            root.transform.SetAsLastSibling();

            MakeBorderStrip(rootRt, "Top",    new Vector2(0f, 1f), Vector2.one,        new Vector2(0.5f, 1f), new Vector2(0f, thickness), c);
            MakeBorderStrip(rootRt, "Bottom", Vector2.zero,        new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, thickness), c);
            MakeBorderStrip(rootRt, "Left",   Vector2.zero,        new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(thickness, 0f), c);
            MakeBorderStrip(rootRt, "Right",  new Vector2(1f, 0f), Vector2.one,        new Vector2(1f, 0.5f), new Vector2(thickness, 0f), c);

            return root;
        }

        private static void MakeBorderStrip(RectTransform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Color color)
        {
            var go = CreateUI(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot     = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = sizeDelta;

            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

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
