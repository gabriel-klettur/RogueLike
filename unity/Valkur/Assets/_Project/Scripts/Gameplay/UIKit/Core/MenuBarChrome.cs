using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Attached to a menu-bar root. Lets the registered <see cref="ColorSource"/>
    /// repaint the menu-bar background + outline live when the user (or any
    /// theme-driven UI) tweaks colors. Defaults to <see cref="UITheme"/> via
    /// <see cref="DefaultMenuBarChromeColors"/>.
    /// </summary>
    public class MenuBarChrome : MonoBehaviour
    {
        public Image   BgImage;
        public Outline BorderOutline;

        public static IMenuBarChromeColors ColorSource = DefaultMenuBarChromeColors.Instance;

        private static MenuBarChrome _instance;

        private void OnEnable()
        {
            _instance = this;
            ApplyTheme();
        }

        private void OnDisable()
        {
            if (_instance == this) _instance = null;
        }

        public void ApplyTheme()
        {
            var s = ColorSource ?? DefaultMenuBarChromeColors.Instance;
            if (BgImage != null) BgImage.color = s.MenuBarBg;
            if (BorderOutline != null)
            {
                BorderOutline.effectColor = s.Border;
                BorderOutline.effectDistance = new Vector2(0f, -s.OutlinePx);
            }
        }

        public static void ApplyThemeToAll()
        {
            if (_instance != null) _instance.ApplyTheme();
        }
    }
}
