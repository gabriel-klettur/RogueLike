using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.Gameplay.Editors.EditorKit
{
    /// <summary>
    /// Small help/tutorial overlay showing a list of hotkeys.
    /// Mirrors Python editors' tutorial panels (e.g. entities/panels/tutorial_panel.py).
    /// </summary>
    public static class TutorialOverlay
    {
        /// <summary>
        /// Builds a tutorial panel docked to the right side of the parent canvas.
        /// Returns the root GameObject (call SetActive to toggle).
        /// </summary>
        public static GameObject Build(Transform parent, string title, (string key, string action)[] lines)
        {
            var panel = EditorUIHelpers.MakePanel("TutorialOverlay", parent,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-16f, -60f), new Vector2(300f, 0f));
            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f; vlg.padding = new RectOffset(10, 10, 8, 10);
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            EditorUIHelpers.MakeTitleBar(panel.transform, title, 26f);

            foreach (var (key, action) in lines)
            {
                var row = EditorUIHelpers.CreateUI("Row", panel.transform);
                row.AddComponent<LayoutElement>().preferredHeight = 20f;
                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 6f; hlg.childForceExpandWidth = false;
                hlg.childControlWidth = true; hlg.childControlHeight = true;

                // Key chip
                var chip = EditorUIHelpers.CreateUI("Key", row.transform);
                chip.AddComponent<LayoutElement>().preferredWidth = 80f;
                var chipImg = chip.AddComponent<Image>();
                chipImg.color = EditorUIHelpers.ACCENT_BG;
                var kt = EditorUIHelpers.AddCenteredText(chip.transform, key, 11f,
                    FontStyles.Bold, EditorUIHelpers.ACCENT);
                kt.characterSpacing = 0f;

                // Action text
                var actGo = EditorUIHelpers.CreateUI("Act", row.transform);
                var actLe = actGo.AddComponent<LayoutElement>();
                actLe.flexibleWidth = 1f;
                var at = actGo.AddComponent<TextMeshProUGUI>();
                at.text = action; at.fontSize = 12f;
                at.color = EditorUIHelpers.TEXT_PRIMARY;
                at.alignment = TextAlignmentOptions.MidlineLeft;
            }
            return panel;
        }
    }
}
