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
    }
}
