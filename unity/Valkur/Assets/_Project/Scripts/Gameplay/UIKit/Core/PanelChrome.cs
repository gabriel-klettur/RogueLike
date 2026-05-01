using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Holds direct references to every chrome element on a floating panel
    /// (background image, outline, header bg, header separator, header
    /// title) so the registered <see cref="ColorSource"/> can repaint them
    /// live when the user (or anything else) tweaks colors.
    ///
    /// Color values are pulled from a pluggable <see cref="IPanelChromeColors"/>
    /// — defaults to <see cref="DefaultPanelChromeColors"/> (i.e. <see cref="UITheme"/>);
    /// the tile editor installs its own live-tweakable source via static ctor.
    ///
    /// All instances self-register in <c>_all</c> in <c>OnEnable</c> and
    /// deregister in <c>OnDisable</c> / <c>OnDestroy</c>.
    /// </summary>
    public class PanelChrome : MonoBehaviour
    {
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

        /// <summary>
        /// Pluggable color source. Defaults to <see cref="UITheme"/> via
        /// <see cref="DefaultPanelChromeColors"/>; replace with a live-tweakable
        /// source (e.g. the tile editor's theme adapter) on app startup.
        /// </summary>
        public static IPanelChromeColors ColorSource = DefaultPanelChromeColors.Instance;

        private static readonly List<PanelChrome> _all = new List<PanelChrome>();

        private void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
            ApplyTheme();
        }

        private void OnDisable() => _all.Remove(this);
        private void OnDestroy() => _all.Remove(this);

        /// <summary>Pulls the current values from <see cref="ColorSource"/> onto this panel.</summary>
        public void ApplyTheme()
        {
            var s = ColorSource ?? DefaultPanelChromeColors.Instance;
            if (PanelBgImage    != null) PanelBgImage.color    = s.PanelBg;
            if (HeaderBgImage   != null) HeaderBgImage.color   = s.HeaderBg;
            if (HeaderSeparator != null) HeaderSeparator.color = s.Separator;
            if (HeaderTitle     != null) HeaderTitle.color     = s.HeaderTitleColor;

            if (PanelOutline != null)
            {
                PanelOutline.effectColor    = s.Border;
                PanelOutline.effectDistance = new Vector2(s.OutlinePx, s.OutlinePx);
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
