using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Attached to a floating Tile-Editor panel root.  Holds direct references to every
    /// chrome element on the panel (background image, outline, header bg, header
    /// separator, header title, header buttons) so <see cref="TileEditorTheme"/>
    /// can repaint them live when the user tweaks colors via the UX panel.
    ///
    /// All instances self-register in <see cref="_all"/> in <c>OnEnable</c> and
    /// deregister in <c>OnDisable</c>/<c>OnDestroy</c>.
    /// </summary>
    public class PanelChrome : MonoBehaviour
    {
        // ── Wired by MakeDropdownPanel ──
        [Tooltip("Background fill image of the panel root.")]
        public Image PanelBgImage;

        [Tooltip("Outline component on the panel root.")]
        public Outline PanelOutline;

        [Tooltip("Background fill image of the panel header.")]
        public Image HeaderBgImage;

        [Tooltip("1-pixel separator image between header and content.")]
        public Image HeaderSeparator;

        [Tooltip("Title TMP text in the header (null for narrow panels with no title).")]
        public TextMeshProUGUI HeaderTitle;

        // ── Static registry ──
        private static readonly List<PanelChrome> _all = new List<PanelChrome>();

        private void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
            ApplyTheme();   // bring this freshly-shown panel up-to-date with the live theme
        }

        private void OnDisable() => _all.Remove(this);
        private void OnDestroy() => _all.Remove(this);

        /// <summary>Pulls the current values from <see cref="TileEditorTheme"/> onto this panel.</summary>
        public void ApplyTheme()
        {
            if (PanelBgImage    != null) PanelBgImage.color    = TileEditorTheme.PanelBg;
            if (HeaderBgImage   != null) HeaderBgImage.color   = TileEditorTheme.HeaderBg;
            if (HeaderSeparator != null) HeaderSeparator.color = TileEditorTheme.Separator;
            if (HeaderTitle     != null) HeaderTitle.color     = TileEditorTheme.HeaderTitle;

            if (PanelOutline != null)
            {
                PanelOutline.effectColor    = TileEditorTheme.Border;
                PanelOutline.effectDistance = new Vector2(TileEditorTheme.OutlinePx,
                                                          TileEditorTheme.OutlinePx);
            }
        }

        /// <summary>Apply the current theme to every active panel chrome in the scene.</summary>
        public static void ApplyThemeToAll()
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i] != null) _all[i].ApplyTheme();
        }
    }
}
