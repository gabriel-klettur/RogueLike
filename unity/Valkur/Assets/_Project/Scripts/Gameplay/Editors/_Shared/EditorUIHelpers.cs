using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// Facade that delegates to <see cref="Valkur.UIKit"/>. Kept so callers
    /// across the Buildings/FSM/Items/Spells/Particles/Lighting/Inventory
    /// editor builders keep compiling while the migration to the unified
    /// kit lands. New code should call <c>UIKit</c> primitives directly.
    /// </summary>
    public static partial class EditorUIHelpers
    {
        // ── Design tokens ──
        public static readonly Color BG_PANEL       = UITheme.BG_PANEL;
        public static readonly Color BG_SURFACE     = UITheme.BG_SURFACE;
        public static readonly Color BG_ELEVATED    = UITheme.BG_ELEVATED;
        public static readonly Color ACCENT         = UITheme.ACCENT;
        public static readonly Color ACCENT_DIM     = UITheme.ACCENT_DIM;
        public static readonly Color ACCENT_BG      = UITheme.ACCENT_BG;
        public static readonly Color TEXT_PRIMARY   = UITheme.TEXT_PRIMARY;
        public static readonly Color TEXT_SECONDARY = UITheme.TEXT_SECONDARY;
        public static readonly Color TEXT_MUTED     = UITheme.TEXT_MUTED;
        public static readonly Color BTN_NORMAL     = UITheme.BTN_NORMAL;
        public static readonly Color BTN_HOVER      = UITheme.BTN_HOVER;
        public static readonly Color BTN_ACTIVE     = UITheme.BTN_ACTIVE;
        public static readonly Color SLOT_BG        = UITheme.SLOT_BG;
        public static readonly Color SLOT_HOVER     = UITheme.SLOT_HOVER;
        public static readonly Color SLOT_SELECTED  = UITheme.SLOT_SELECTED;
        public static readonly Color BORDER         = UITheme.BORDER;
        public static readonly Color SELECTION_BORDER = UITheme.SELECTION_BORDER;
        public static readonly Color SEPARATOR      = UITheme.SEPARATOR;
        public static readonly Color DANGER         = UITheme.DANGER;
        public static readonly Color SUCCESS        = UITheme.SUCCESS;

        public const float PANEL_PAD       = UITheme.PANEL_PAD;
        public const float SECTION_SPACING = UITheme.SECTION_SPACING;
        public const float SIDEBAR_WIDTH   = UITheme.SIDEBAR_WIDTH;

        // ── Canvas / GameObject ──
        public static Canvas CreateEditorCanvas(string name, int sortOrder = 100)
            => UICanvasFactory.CreateOverlayCanvas(name, sortOrder);

        public static GameObject CreateUI(string name, Transform parent)
            => UIFactory.CreateUI(name, parent);

        public static void StretchFill(GameObject go) => UIFactory.StretchFill(go);

        // ── Panels ──
        public static GameObject MakePanel(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
            => UIPanel.Make(name, parent, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta);

        public static GameObject MakeSidebar(string name, Transform parent, float width = 300f)
            => UIPanel.MakeSidebar(name, parent, width);

        public static GameObject MakeRightPanel(string name, Transform parent, float width = 300f)
            => UIPanel.MakeRightPanel(name, parent, width);

        // ── Buttons ──
        public static Button MakeButton(Transform parent, string label,
            UnityEngine.Events.UnityAction onClick, float height = 30f, float fontSize = 13f)
            => UIButton.Make(parent, label, onClick, height, fontSize);

        public static Button MakeDangerButton(Transform parent, string label,
            UnityEngine.Events.UnityAction onClick, float height = 30f)
            => UIButton.MakeDanger(parent, label, onClick, height);

        // ── Labels ──
        public static TextMeshProUGUI AddCenteredText(Transform parent, string text,
            float size, FontStyles style, Color color)
            => UILabel.AddCenteredText(parent, text, size, style, color);

        public static TextMeshProUGUI AddLabel(Transform parent, string text,
            float fontSize = 12f, TextAlignmentOptions align = TextAlignmentOptions.Left)
            => UILabel.Add(parent, text, fontSize, align);

        public static void BuildSectionHeader(Transform parent, string text, float fontSize = 14f)
            => UILabel.BuildSectionHeader(parent, text, fontSize);

        // ── Inputs ──
        public static TMP_InputField AddInputField(Transform parent, string initial,
            Action<string> onCommit, float height = 24f, float fontSize = 11f)
            => UIInputField.AddCommit(parent, initial, onCommit, height, fontSize);

        // ── Misc ──
        public static void BuildSeparator(Transform parent) => UISeparator.Build(parent);

        public static (ScrollRect scroll, RectTransform content) MakeScrollView(
            Transform parent, string name, float height = 0f)
            => UIFactory.MakeScrollView(parent, name, height);
    }
}
