using TMPro;
using UnityEngine;
using Valkur.Data;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// The one place that turns <see cref="MenuStyle.menuFontResource"/> into a real
    /// <see cref="TMP_FontAsset"/>, and the one place that builds a menu label.
    ///
    /// <para><b>Why the style stores a path and not the asset.</b> <c>Valkur.Data</c> sees only
    /// <c>Valkur.Core</c> — no TextMeshPro, no UI — which is what lets an EditMode fixture load a
    /// catalogue without dragging the whole drawing stack in behind it. A typed
    /// <c>TMP_FontAsset</c> field would have made the data layer depend on the UI layer for one
    /// reference. Same call <c>LoadoutStateSheets.state</c> and
    /// <c>SpellDefinition.previewAnimState</c> already make.</para>
    ///
    /// <para><b>A missing font is not an error.</b> An unresolvable path leaves TMP's own default
    /// in place and logs once — the menu draws in the stock face rather than not drawing. The
    /// shipped menu used that default for all 113 of its labels, so "no font asset" is the state
    /// this replaces, not a regression.</para>
    /// </summary>
    public static class MenuTypography
    {
        private static TMP_FontAsset s_font;
        private static string s_resolvedFrom;
        private static bool s_warned;

        /// <summary>
        /// Static mutable state with Domain Reload off. Direct assignments, because
        /// <c>DomainReloadStaticResetTests</c> reads this method's raw IL.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_font = null;
            s_resolvedFrom = null;
            s_warned = false;
        }

        /// <summary>The menu's font, or null to keep whatever TMP defaults to.</summary>
        public static TMP_FontAsset Font(MenuStyle style)
        {
            string path = style != null ? style.menuFontResource : null;
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (s_font != null && s_resolvedFrom == path) return s_font;

            s_resolvedFrom = path;
            s_font = Resources.Load<TMP_FontAsset>(path);
            if (s_font == null && !s_warned)
            {
                s_warned = true;
                Debug.LogWarning($"[Menu] No hay fuente en Resources/{path}; se usa la de TMP por defecto.");
            }
            return s_font;
        }

        /// <summary>
        /// A menu label, built the same way every time. Never a raycast target: in the menus
        /// exactly one object per row catches the pointer, and a label that also did would take
        /// the hover away from the row it sits on.
        /// </summary>
        public static TextMeshProUGUI Label(GameObject host, MenuStyle style, string text,
                                            float size, Color colour,
                                            TextAlignmentOptions align = TextAlignmentOptions.Left,
                                            bool bold = false)
        {
            var tmp = host.AddComponent<TextMeshProUGUI>();
            var font = Font(style);
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = colour;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            if (bold) tmp.fontStyle = FontStyles.Bold;
            return tmp;
        }
    }
}
