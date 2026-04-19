using UnityEngine;
using UnityEngine.UI;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Attached to the menu bar root.  Lets <see cref="TileEditorTheme"/> repaint the
    /// menu bar background + outline live when the user tweaks colors via the UX panel.
    /// </summary>
    public class MenuBarChrome : MonoBehaviour
    {
        public Image   BgImage;
        public Outline BorderOutline;

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
            if (BgImage != null) BgImage.color = TileEditorTheme.MenuBarBg;
            if (BorderOutline != null)
            {
                BorderOutline.effectColor = TileEditorTheme.Border;
                BorderOutline.effectDistance = new Vector2(0f, -TileEditorTheme.OutlinePx);
            }
        }

        public static void ApplyThemeToAll()
        {
            if (_instance != null) _instance.ApplyTheme();
        }
    }
}
